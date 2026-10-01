using LightSync.Core.Audio;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;

namespace LightSync.Core.Tests;

public sealed class AudioResponseTests
{
    [Fact]
    public void RainbowRetainsIntensityChangesAtGainFifteen()
    {
        var mapper = new AudioColorMapper(new AudioConfig { Mode = "rainbow", Gain = 15, Smoothing = 0 });
        var frame = new RgbColor[1];
        mapper.Map(new AudioFeatures(0.2, 0.2, 0, 0), frame);
        var quiet = frame[0].R;
        mapper.Map(new AudioFeatures(0.4, 0.4, 0, 0), frame);
        Assert.InRange(quiet, 220, 245);
        Assert.InRange(frame[0].R, quiet + 1, 254);
    }

    [Fact]
    public void GainChangesIntensityWithoutChangingRainbowMotion()
    {
        var low = new AudioColorMapper(new AudioConfig { Mode = "rainbow", Gain = 1, Smoothing = 0 });
        var high = new AudioColorMapper(new AudioConfig { Mode = "rainbow", Gain = 15, Smoothing = 0 });
        var feature = new AudioFeatures(0.2, 0.1, 0.1, 0);
        var first = new RgbColor[1];
        var second = new RgbColor[1];
        for (var i = 0; i < 100; i++)
        {
            low.Advance(feature, 0.02);
            high.Advance(feature, 0.02);
        }

        low.Map(feature, first);
        high.Map(feature, second);
        Assert.Equal(0, first[0].R);
        Assert.Equal(0, second[0].R);
        Assert.InRange((double)first[0].G / first[0].B - (double)second[0].G / second[0].B, -0.025, 0.025);
    }

    [Fact]
    public void RainbowMovesAcrossAllDevicesOncePerAudioBlockAndPausesInSilence()
    {
        var mapper = new AudioColorMapper(new AudioConfig { Mode = "rainbow", Smoothing = 0 });
        var feature = new AudioFeatures(0.2, 0.1, 0.1, 0);
        var shortFrame = new RgbColor[4];
        var longFrame = new RgbColor[8];
        mapper.Map(feature, shortFrame);
        var initial = shortFrame[0];
        mapper.Advance(feature, 0.5);
        mapper.Map(feature, shortFrame);
        mapper.Map(feature, longFrame);
        Assert.NotEqual(initial, shortFrame[0]);
        Assert.Equal(shortFrame[0], longFrame[0]);
        Assert.Equal(shortFrame[2], longFrame[4]);
        var moving = shortFrame[0];
        mapper.Advance(default, 20);
        mapper.Map(feature, shortFrame);
        Assert.Equal(moving, shortFrame[0]);
        mapper.Map(default, shortFrame);
        Assert.All(shortFrame, color => Assert.Equal(RgbColor.Black, color));
    }

    [Fact]
    public void LiveOptionsAreSnapshotsAndInvalidUpdatesLeavePreviousSettingsIntact()
    {
        var options = new AudioConfig { Mode = "volume", Gain = 1, Smoothing = 0, Color = new(200, 100, 0) };
        var mapper = new AudioColorMapper(options);
        var frame = new RgbColor[1];
        options.Brightness = 0;
        mapper.Map(new AudioFeatures(0.5, 0, 0, 0), frame);
        Assert.Equal(new RgbColor(100, 50, 0), frame[0]);
        mapper.UpdateOptions(options with { Brightness = 0.5 });
        Assert.Throws<ArgumentException>(() => mapper.UpdateOptions(options with { Motion = double.NaN }));
        mapper.Map(new AudioFeatures(0.5, 0, 0, 0), frame);
        Assert.Equal(new RgbColor(50, 25, 0), frame[0]);
    }

    [Theory]
    [InlineData(93.75)]
    [InlineData(750)]
    [InlineData(6000)]
    public void VisualizerBinsPreserveToneEnergyAndClearAfterSilence(double frequency)
    {
        const int size = 1024;
        const int rate = 48000;
        var samples = new float[size * 2];
        for (var i = 0; i < size; i++)
        {
            samples[i * 2] = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * i / rate));
            samples[(i * 2) + 1] = -samples[i * 2];
        }

        var analyzer = new AudioAnalyzer(rate, size);
        analyzer.Analyze(samples, 2);
        Assert.Equal(AudioAnalyzer.SpectrumBandCount, analyzer.Spectrum.Length);
        double power = 0;
        foreach (var level in analyzer.Spectrum.Span)
        {
            Assert.True(double.IsFinite(level));
            power += level * level;
        }

        Assert.InRange(Math.Sqrt(power), 0.34, 0.36);
        Array.Clear(samples);
        analyzer.Analyze(samples, 2);
        foreach (var level in analyzer.Spectrum.Span)
        {
            Assert.Equal(0, level);
        }
    }
}
