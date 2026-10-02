using LightSync.Core.Audio;
using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Mapping;
using LightSync.Core.Processing;

namespace LightSync.Core.Tests;

public sealed class ScreenIntensityTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"screen\":null}")]
    [InlineData("{\"screen\":{}}")]
    public void ExistingConfigurationsGetSeventyFivePercentAndAudioIntensity(string json)
    {
        var config = ConfigurationLoader.Parse(json, "test");
        Assert.Equal(0.75, config.Screen.Brightness);
        Assert.True(config.Screen.AudioIntensityEnabled);
        Assert.Equal(ScreenConfig.DefaultAudioSource, config.Screen.AudioSource);
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void ScreenSettingsRoundTripIndependentlyOfAudioColourSettings()
    {
        var original = new AppConfig
        {
            Screen = new ScreenConfig { Brightness = 0, AudioIntensityEnabled = false, AudioSource = "screen.monitor" },
            Audio = new AudioConfig { Brightness = 0.4, Gain = 15, Source = "audio.monitor" },
        };
        var reloaded = ConfigurationLoader.Parse(ConfigurationLoader.Serialize(original), "test");
        Assert.Equal(original, reloaded);
        Assert.Equal(0, reloaded.Screen.Brightness);
        Assert.False(reloaded.Screen.AudioIntensityEnabled);
        Assert.Equal(15, reloaded.Audio.Gain);
    }

    [Fact]
    public void PartialSettingsPreserveTheExplicitToggleAndNormalizeMissingSource()
    {
        var config = ConfigurationLoader.Parse("{\"screen\":{\"audioIntensityEnabled\":false}}", "test");
        Assert.False(config.Screen.AudioIntensityEnabled);
        Assert.Equal(0.75, config.Screen.Brightness);
        Assert.Equal(ScreenConfig.DefaultAudioSource, config.Screen.AudioSource);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidBrightness(double value)
    {
        var options = new ScreenConfig { Brightness = value };
        Assert.NotEmpty(options.Validate());
        Assert.Throws<ArgumentException>(() => new ScreenBrightness(options));
    }

    [Fact]
    public void MeasuresStereoPowerWithoutCancellingOppositePhases()
    {
        var mono = new ScreenBrightness(new());
        var stereo = new ScreenBrightness(new());
        mono.UpdateAudio([0.1f, 0.1f], 1);
        stereo.UpdateAudio([0.1f, -0.1f], 1);
        Assert.Equal(-20, stereo.Status.Decibels, precision: 5);
        Assert.Equal(mono.Status.Brightness, stereo.Status.Brightness);
        Assert.Equal(mono.Status.Decibels, stereo.Status.Decibels);
    }

    [Fact]
    public void DoublingAmplitudeAddsSixDecibelsAndIncreasesLightIntensity()
    {
        var quiet = new ScreenBrightness(new());
        var loud = new ScreenBrightness(new());
        quiet.UpdateAudio([0.1f], 5);
        loud.UpdateAudio([0.2f], 5);
        Assert.Equal(6.0206, loud.Status.Decibels - quiet.Status.Decibels, precision: 4);
        Assert.True(loud.Status.Brightness > quiet.Status.Brightness);
        Assert.InRange(loud.Status.Brightness, 0.75, 1);
    }

    [Fact]
    public void SilenceReturnsToBaselineAndFullScaleAudioIsBounded()
    {
        var brightness = new ScreenBrightness(new());
        Assert.Equal(0.75, brightness.Status.Brightness);
        brightness.UpdateAudio([1, -1], 5);
        Assert.InRange(brightness.Status.Brightness, 0.999, 1);
        brightness.UpdateAudio([0, 0], 10);
        Assert.Equal(0.75, brightness.Status.Brightness, precision: 6);
        Assert.Equal(-120, brightness.Status.Decibels);
        brightness.UpdateAudio([float.NaN, float.PositiveInfinity, float.NegativeInfinity], 1);
        Assert.Equal(-120, brightness.Status.Decibels);
    }

    [Fact]
    public void AttacksRiseFasterThanTheyRelease()
    {
        var brightness = new ScreenBrightness(new());
        brightness.UpdateAudio([1], 0.08);
        var peak = brightness.Status.Brightness;
        Assert.InRange(peak, 0.90, 0.92);
        brightness.UpdateAudio([0], 0.08);
        var released = brightness.Status.Brightness;
        Assert.True(peak - released < peak - 0.75);
        Assert.InRange(released, 0.86, peak);
    }

    [Fact]
    public void ToggleOffImmediatelyRestoresBaselineAndDiscardsOldEnvelope()
    {
        var brightness = new ScreenBrightness(new());
        brightness.UpdateAudio([1], 1);
        brightness.UpdateOptions(new ScreenConfig { AudioIntensityEnabled = false });
        brightness.UpdateAudio([1], 1);
        Assert.Equal(0.75, brightness.Status.Brightness);
        Assert.False(brightness.Status.HasAudio);
        brightness.UpdateOptions(new ScreenConfig());
        Assert.Equal(0.75, brightness.Status.Brightness);
        Assert.False(brightness.Status.HasAudio);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ZeroStaysOffAndFullBrightnessDoesNotOverdrive(double baseline)
    {
        var brightness = new ScreenBrightness(new ScreenConfig { Brightness = baseline });
        brightness.UpdateAudio([1], 5);
        Assert.Equal(baseline, brightness.Status.Brightness);
    }

    [Fact]
    public void EveryOutputInOneCapturedFrameUsesTheSameIntensity()
    {
        var brightness = new ScreenBrightness(new());
        var first = brightness.ForFrame(1);
        brightness.UpdateAudio([1], 5);
        Assert.Equal(first, brightness.ForFrame(1));
        Assert.True(brightness.ForFrame(2) > first);
        brightness.UpdateOptions(new ScreenConfig { Brightness = 0.4, AudioIntensityEnabled = false });
        Assert.Equal(0.4, brightness.ForFrame(3));
    }

    [Fact]
    public void AudioFailureRestoresSteadyOutputWithoutLosingScreenSettings()
    {
        var brightness = new ScreenBrightness(new ScreenConfig { Brightness = 0.6 });
        brightness.UpdateAudio([1], 5);
        brightness.ResetAudio("Playback ended");
        Assert.Equal(0.6, brightness.Status.Brightness);
        Assert.Equal("Playback ended", brightness.Status.AudioError);
        Assert.False(brightness.Status.HasAudio);
    }

    [Fact]
    public void InvalidLiveUpdatesLeaveExistingSettingsIntact()
    {
        var brightness = new ScreenBrightness(new());
        Assert.Throws<ArgumentException>(() => brightness.UpdateOptions(new ScreenConfig { Brightness = double.NaN }));
        Assert.Equal(0.75, brightness.Status.Brightness);
    }

    [Fact]
    public void LiveOptionsAreCopiedSoCallerMutationCannotChangeOutput()
    {
        var options = new ScreenConfig();
        var brightness = new ScreenBrightness(options);
        options.Brightness = 0;
        Assert.Equal(0.75, brightness.Status.Brightness);
        brightness.UpdateOptions(options with { Brightness = 0.5 });
        options.Brightness = 1;
        Assert.Equal(0.5, brightness.Status.Brightness);
    }

    [Fact]
    public void DifferentLedCountsPreserveScreenColoursAndShareOneBrightness()
    {
        var brightness = new ScreenBrightness(new());
        var first = Processor(2, brightness);
        var second = Processor(5, brightness);
        RgbColor[] firstColors = new RgbColor[2], secondColors = new RgbColor[5];
        var frame = new CapturedFrame(new byte[] { 40, 100, 200, 255 }, 1, 1, 1, TimeSpan.Zero);
        first.Process(frame, firstColors);
        second.Process(frame, secondColors);
        Assert.All(firstColors, color => Assert.Equal(new RgbColor(150, 75, 30), color));
        Assert.All(secondColors, color => Assert.Equal(firstColors[0], color));
        brightness.UpdateAudio([1], 5);
        frame = frame with { Sequence = 2 };
        first.Process(frame, firstColors);
        second.Process(frame, secondColors);
        Assert.All(firstColors, color => Assert.Equal(new RgbColor(200, 100, 40), color));
        Assert.All(secondColors, color => Assert.Equal(firstColors[0], color));
    }

    private static ScreenBrightnessProcessor Processor(int count, ScreenBrightness brightness) => new(new ColorProcessor(
        new ZoneMapper(count, ZoneLayout.Vertical, ZoneDirection.LeftToRight, false),
        new ColorProcessorOptions { Brightness = 1, Smoothing = 0, BlackLevel = 0, Averaging = ZoneAveraging.Mean }), brightness);
}
