using LightSync.Core.Capture;
using LightSync.Core.Configuration;
using LightSync.Core.Mapping;

namespace LightSync.Core.Tests;

public class ConfigurationLoaderTests
{
    private const string ExampleConfig = """
        {
          "capture":    { "displayId": 0, "x": 3200, "y": 200, "width": 1600, "height": 1000, "fps": 30 },
          "mapping":    { "zoneCount": 24, "layout": "vertical", "direction": "left-to-right", "reverse": false },
          "processing": { "brightness": 1.0, "gamma": 1.0, "saturation": 1.0, "smoothing": 0.2, "blackLevel": 0.01 },
          "device":     { "adapter": "nanoleaf",
                          "settings": { "host": "192.168.1.24", "port": "16021",
                                        "tokenEnvironmentVariable": "NANOLEAF_TOKEN" } }
        }
        """;

    [Fact]
    public void ParsesTheDocumentedExample()
    {
        var config = ConfigurationLoader.Parse(ExampleConfig, "test");

        Assert.Equal(new CaptureArea(3200, 200, 1600, 1000), config.Capture.Area);
        Assert.Equal(30, config.Capture.Fps);
        Assert.Equal(24, config.Mapping.ZoneCount);
        Assert.Equal("nanoleaf", config.Device.Adapter);
        Assert.Equal("192.168.1.24", config.Device.Settings["host"]);
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void DefaultsMatchTheDocumentedDefaults()
    {
        var config = new AppConfig();

        Assert.Equal(24, config.Mapping.ZoneCount);
        Assert.Equal("vertical", config.Mapping.Layout);
        Assert.Equal("left-to-right", config.Mapping.Direction);
        Assert.False(config.Mapping.Reverse);
        Assert.Equal(30, config.Capture.Fps);
        Assert.Equal(1.0, config.Processing.Brightness);
        Assert.Equal(0.2, config.Processing.Smoothing);
        Assert.Equal(0.01, config.Processing.BlackLevel);
    }

    [Fact]
    public void NeverPersistsASecretAlongsideTheConfiguration()
    {
        var config = ConfigurationLoader.Parse(ExampleConfig, "test");

        var json = ConfigurationLoader.Serialize(config);

        // The config names the environment variable holding the token; it must not be able
        // to carry the token itself.
        Assert.Contains("tokenEnvironmentVariable", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"token\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoundTripsThroughDisk()
    {
        var directory = Directory.CreateTempSubdirectory("light-sync-test");
        try
        {
            var path = Path.Combine(directory.FullName, "nested", "config.json");
            var original = ConfigurationLoader.Parse(ExampleConfig, "test");

            await ConfigurationLoader.SaveAsync(path, original, TestContext.Current.CancellationToken);
            var reloaded = await ConfigurationLoader.LoadAsync(path, TestContext.Current.CancellationToken);

            Assert.Equal(original, reloaded);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsDefaultsWhenTheFileDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"light-sync-missing-{Guid.NewGuid():N}.json");

        var config = await ConfigurationLoader.LoadAsync(missing, TestContext.Current.CancellationToken);

        Assert.Equal(new AppConfig(), config);
    }

    [Fact]
    public void ReportsMalformedJsonWithTheSourceName()
    {
        var exception = Assert.Throws<ConfigurationException>(
            () => ConfigurationLoader.Parse("{ not json", "my-config.json"));

        Assert.Contains("my-config.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectsEveryValidationProblemAtOnce()
    {
        var config = new AppConfig
        {
            Capture = new CaptureConfig { Width = 0, Height = -5, Fps = 500, DisplayId = -1 },
            Mapping = new MappingConfig { ZoneCount = -1, Layout = "diagonal", Direction = "sideways" },
            Processing = new ProcessingConfig { Gamma = 0 },
        };

        var problems = config.Validate();

        Assert.Contains(problems, p => p.Contains("capture.displayId", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("capture.fps", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("mapping.zoneCount", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("mapping.layout", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("mapping.direction", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("processing", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyAdapterIsReportedWhenCheckedDirectly()
    {
        // At the AppConfig level a missing adapter is filled in with the default rather than
        // reported, since an omitted string is indistinguishable from an unset one. Checked on
        // its own, though, an empty adapter is still a problem worth naming.
        Assert.Contains(
            new DeviceConfig { Adapter = "  " }.Validate(),
            p => p.Contains("device.adapter", StringComparison.Ordinal));
    }

    [Fact]
    public void TreatsUnconfiguredCaptureAreaAsNotYetSetUp()
    {
        Assert.False(new CaptureConfig().IsConfigured);
        Assert.True(new CaptureConfig { Width = 100, Height = 100 }.IsConfigured);
    }

    [Fact]
    public void BuildsCaptureConfigFromAPortalReportedArea()
    {
        var config = CaptureConfig.FromArea(0, new CaptureArea(3200, 200, 1600, 1000), 60, "token-abc");

        Assert.Equal(new CaptureArea(3200, 200, 1600, 1000), config.Area);
        Assert.Equal(60, config.Fps);
        Assert.Equal("token-abc", config.RestoreToken);
    }

    [Theory]
    [InlineData("vertical", ZoneLayout.Vertical)]
    [InlineData("Horizontal", ZoneLayout.Horizontal)]
    [InlineData(" VERTICAL ", ZoneLayout.Vertical)]
    public void ParsesLayoutCaseAndWhitespaceInsensitively(string value, ZoneLayout expected)
    {
        Assert.True(MappingConfig.TryParseLayout(value, out var layout));
        Assert.Equal(expected, layout);
    }

    [Theory]
    [InlineData("left-to-right", ZoneDirection.LeftToRight)]
    [InlineData("right-to-left", ZoneDirection.RightToLeft)]
    [InlineData("top-to-bottom", ZoneDirection.TopToBottom)]
    [InlineData("bottom-to-top", ZoneDirection.BottomToTop)]
    public void ParsesEveryDirection(string value, ZoneDirection expected)
    {
        Assert.True(MappingConfig.TryParseDirection(value, out var direction));
        Assert.Equal(expected, direction);
    }

    [Fact]
    public void ComparesDeviceSettingsByValueRatherThanByReference()
    {
        var a = new DeviceConfig { Adapter = "nanoleaf", Settings = { ["host"] = "10.0.0.1" } };
        var b = new DeviceConfig { Adapter = "nanoleaf", Settings = { ["host"] = "10.0.0.1" } };
        var c = new DeviceConfig { Adapter = "nanoleaf", Settings = { ["host"] = "10.0.0.2" } };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ComparesCustomOrderByValueRatherThanByReference()
    {
        var a = new MappingConfig { ZoneCount = 3, CustomOrder = [2, 0, 1] };
        var b = new MappingConfig { ZoneCount = 3, CustomOrder = [2, 0, 1] };
        var c = new MappingConfig { ZoneCount = 3, CustomOrder = [0, 1, 2] };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void AcceptsACustomOrderThatIsAPermutation()
    {
        var mapping = new MappingConfig { ZoneCount = 4, CustomOrder = [3, 1, 0, 2] };

        Assert.Empty(mapping.Validate());
    }

    [Fact]
    public void RejectsACustomOrderWithTheWrongLength()
    {
        var mapping = new MappingConfig { ZoneCount = 4, CustomOrder = [0, 1] };

        Assert.Contains(mapping.Validate(), p => p.Contains("zoneCount is 4", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACustomOrderWithDuplicates()
    {
        var mapping = new MappingConfig { ZoneCount = 4, CustomOrder = [0, 1, 1, 2] };

        Assert.Contains(mapping.Validate(), p => p.Contains("exactly once", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACustomOrderWithAnOutOfRangeIndex()
    {
        var mapping = new MappingConfig { ZoneCount = 4, CustomOrder = [0, 1, 2, 9] };

        Assert.Contains(mapping.Validate(), p => p.Contains("exactly once", StringComparison.Ordinal));
    }
}
