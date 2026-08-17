using LightSync.Core.Configuration;

namespace LightSync.Core.Tests;

/// <summary>
/// Regression cover for a real defect: deserialization does not run property initializers, so an
/// omitted section arrived as null and a hand-written partial configuration threw a
/// NullReferenceException. Defaults are now applied explicitly rather than assumed.
/// </summary>
public class ConfigurationNormalizationTests
{
    [Fact]
    public void AConfigThatOmitsWholeSectionsIsStillUsable()
    {
        var config = ConfigurationLoader.Parse("""{ "capture": { "width": 100, "height": 50 } }""", "test");

        Assert.NotNull(config.Mapping);
        Assert.NotNull(config.Processing);
        Assert.NotNull(config.Device);
        Assert.NotNull(config.Device.Settings);
    }

    [Fact]
    public void OmittedSectionsGetTheDocumentedDefaults()
    {
        var config = ConfigurationLoader.Parse("""{ "capture": { "width": 100, "height": 50 } }""", "test");

        Assert.Equal(MappingConfig.DefaultZoneCount, config.Mapping.ZoneCount);
        Assert.Equal(MappingConfig.DefaultLayout, config.Mapping.Layout);
        Assert.Equal(MappingConfig.DefaultDirection, config.Mapping.Direction);
        Assert.Equal(ProcessingConfig.DefaultAveraging, config.Processing.Averaging);
        Assert.Equal(DeviceConfig.DefaultAdapter, config.Device.Adapter);
        Assert.Equal(CaptureConfig.DefaultFps, config.Capture.Fps);
    }

    [Fact]
    public void AnEmptyObjectValidatesCleanly()
    {
        var config = ConfigurationLoader.Parse("{}", "test");

        // Nothing is malformed; only the capture area is simply not chosen yet.
        Assert.Empty(config.Validate());
        Assert.False(config.Capture.IsConfigured);
    }

    [Fact]
    public void OmittedStringsAreNotReportedAsInvalidValues()
    {
        var config = ConfigurationLoader.Parse("""{ "mapping": { "zoneCount": 8 } }""", "test");

        Assert.Empty(config.Validate());
        Assert.Equal(8, config.Mapping.ZoneCount);
    }

    [Fact]
    public void AnOmittedFrameRateFallsBackToTheDefault()
    {
        var config = ConfigurationLoader.Parse("""{ "capture": { "width": 10, "height": 10 } }""", "test");

        Assert.Equal(30, config.Capture.Fps);
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void NormalizationDoesNotPaperOverValuesTheUserActuallyWrote()
    {
        var config = ConfigurationLoader.Parse(
            """{ "mapping": { "layout": "diagonal" }, "processing": { "averaging": "median" } }""",
            "test");

        var problems = config.Validate();

        Assert.Contains(problems, p => p.Contains("mapping.layout", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("processing.averaging", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExplicitlyNegativeZoneCountIsStillRejected()
    {
        // Zero means "omitted" and is filled in; a negative value is a real mistake.
        var config = ConfigurationLoader.Parse("""{ "mapping": { "zoneCount": -1 } }""", "test");

        Assert.Contains(config.Validate(), p => p.Contains("mapping.zoneCount", StringComparison.Ordinal));
    }

    [Fact]
    public void NormalizingIsIdempotent()
    {
        var once = new AppConfig().Normalized();
        var twice = once.Normalized();

        Assert.Equal(once, twice);
    }

    [Fact]
    public void ExplicitValuesSurviveNormalization()
    {
        var config = ConfigurationLoader.Parse(
            """
            { "capture": { "width": 100, "height": 50, "fps": 60 },
              "mapping": { "zoneCount": 12, "layout": "horizontal", "direction": "bottom-to-top" },
              "processing": { "averaging": "mean" },
              "device": { "adapter": "nanoleaf" } }
            """,
            "test");

        Assert.Equal(60, config.Capture.Fps);
        Assert.Equal(12, config.Mapping.ZoneCount);
        Assert.Equal("horizontal", config.Mapping.Layout);
        Assert.Equal("bottom-to-top", config.Mapping.Direction);
        Assert.Equal("mean", config.Processing.Averaging);
        Assert.Equal("nanoleaf", config.Device.Adapter);
    }
}
