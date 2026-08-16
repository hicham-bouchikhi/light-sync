using LightSync.Devices.Nanoleaf;

namespace LightSync.Devices.Nanoleaf.Tests;

public class NanoleafSettingsTests
{
    [Fact]
    public void ReadsTheDocumentedSettingsBlock()
    {
        var settings = NanoleafSettings.FromDictionary(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = "192.168.1.24",
            ["port"] = "16021",
            ["tokenEnvironmentVariable"] = "NANOLEAF_TOKEN",
        });

        Assert.Equal("192.168.1.24", settings.Host);
        Assert.Equal(16021, settings.Port);
        Assert.Equal("NANOLEAF_TOKEN", settings.TokenEnvironmentVariable);
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void DefaultsPortAndTokenVariable()
    {
        var settings = NanoleafSettings.FromDictionary(new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Equal(16021, settings.Port);
        Assert.Equal("NANOLEAF_TOKEN", settings.TokenEnvironmentVariable);
    }

    [Fact]
    public void LeavesHostNullWhenAbsentSoDiscoveryCanFillItIn()
    {
        var settings = NanoleafSettings.FromDictionary(new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Null(settings.Host);
    }

    [Fact]
    public void TreatsABlankHostAsAbsent()
    {
        var settings = NanoleafSettings.FromDictionary(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["host"] = "   " });

        Assert.Null(settings.Host);
    }

    [Fact]
    public void BuildsTheApiBaseUri()
    {
        var settings = new NanoleafSettings { Host = "192.168.1.24", Port = 16021 };

        Assert.Equal("http://192.168.1.24:16021/api/v1/", settings.BaseUri.ToString());
    }

    [Fact]
    public void ParsesACommaSeparatedLedMapping()
    {
        var settings = NanoleafSettings.FromDictionary(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["ledMapping"] = "12, 7,3 ,99" });

        Assert.Equal([12, 7, 3, 99], settings.LedMapping);
    }

    [Fact]
    public void RejectsANonNumericLedMapping()
    {
        Assert.Throws<ArgumentException>(() => NanoleafSettings.FromDictionary(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["ledMapping"] = "1,two,3" }));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    public void RejectsAnOutOfRangePort(string port)
    {
        var settings = NanoleafSettings.FromDictionary(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["port"] = port });

        Assert.Contains(settings.Validate(), p => p.Contains("port", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnEmptyTokenVariableName()
    {
        var settings = new NanoleafSettings { TokenEnvironmentVariable = "  " };

        Assert.Contains(
            settings.Validate(),
            p => p.Contains("tokenEnvironmentVariable", StringComparison.Ordinal));
    }

    [Fact]
    public void IgnoresAnUnparseablePortRatherThanThrowing()
    {
        // Validation reports configuration problems; parsing must not crash first.
        var settings = NanoleafSettings.FromDictionary(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["port"] = "not-a-number" });

        Assert.Equal(16021, settings.Port);
    }
}
