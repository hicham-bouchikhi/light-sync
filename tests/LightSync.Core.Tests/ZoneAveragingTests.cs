using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Mapping;
using LightSync.Core.Processing;

namespace LightSync.Core.Tests;

public class ZoneAveragingTests
{
    private static CapturedFrame Frame(int width, int height, Func<int, int, RgbColor> pixel)
    {
        var bytes = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = pixel(x, y);
                var offset = ((y * width) + x) * 4;
                bytes[offset] = color.B;
                bytes[offset + 1] = color.G;
                bytes[offset + 2] = color.R;
                bytes[offset + 3] = 255;
            }
        }

        return new CapturedFrame(bytes, width, height, 1, TimeSpan.Zero);
    }

    private static RgbColor SingleZone(CapturedFrame frame, ZoneAveraging averaging)
    {
        var processor = new ColorProcessor(
            new ZoneMapper(1, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false),
            new ColorProcessorOptions
            {
                Brightness = 1.0,
                Gamma = 1.0,
                Saturation = 1.0,
                Smoothing = 0.0,
                BlackLevel = 0.0,
                Averaging = averaging,
            });

        var output = new RgbColor[1];
        processor.Process(frame, output);
        return output[0];
    }

    [Fact]
    public void LuminanceWeightingKeepsABrightAccentFromBeingDilutedByDarkBackground()
    {
        // One bright red pixel against fifteen black ones: the situation that made the lamp look
        // washed out when syncing a mostly dark desktop.
        var frame = Frame(16, 1, (x, _) => x == 0 ? ColorConstants.Red : RgbColor.Black);

        var mean = SingleZone(frame, ZoneAveraging.Mean);
        var weighted = SingleZone(frame, ZoneAveraging.LuminanceWeighted);

        Assert.Equal(15, mean.R);
        Assert.Equal(255, weighted.R);
        Assert.True(weighted.R > mean.R);
    }

    [Fact]
    public void BothModesAgreeOnAUniformZone()
    {
        var frame = Frame(8, 4, (_, _) => new RgbColor(90, 140, 200));

        Assert.Equal(
            SingleZone(frame, ZoneAveraging.Mean),
            SingleZone(frame, ZoneAveraging.LuminanceWeighted));
    }

    [Fact]
    public void WeightingFavoursTheBrighterOfTwoColours()
    {
        var frame = Frame(2, 1, (x, _) => x == 0 ? new RgbColor(255, 0, 0) : new RgbColor(20, 0, 0));

        var weighted = SingleZone(frame, ZoneAveraging.LuminanceWeighted);
        var mean = SingleZone(frame, ZoneAveraging.Mean);

        Assert.True(weighted.R > mean.R);
    }

    [Fact]
    public void AnAllBlackZoneIsBlackUnderBothModes()
    {
        var frame = Frame(4, 4, (_, _) => RgbColor.Black);

        Assert.Equal(RgbColor.Black, SingleZone(frame, ZoneAveraging.Mean));
        Assert.Equal(RgbColor.Black, SingleZone(frame, ZoneAveraging.LuminanceWeighted));
    }

    [Fact]
    public void WeightingPreservesHueOfTheDominantColour()
    {
        // A blue accent on black must stay blue, not drift towards grey.
        var frame = Frame(10, 1, (x, _) => x < 2 ? ColorConstants.Blue : RgbColor.Black);

        var weighted = SingleZone(frame, ZoneAveraging.LuminanceWeighted);

        Assert.Equal(0, weighted.R);
        Assert.Equal(0, weighted.G);
        Assert.Equal(255, weighted.B);
    }

    [Fact]
    public void MeanRemainsAvailableForFaithfulReproduction()
    {
        var frame = Frame(2, 1, (x, _) => x == 0 ? ColorConstants.White : RgbColor.Black);

        Assert.Equal(new RgbColor(127, 127, 127), SingleZone(frame, ZoneAveraging.Mean));
    }

    [Fact]
    public void ColorWeightingKeepsAnAccentColourAgainstWhiteBrowserContent()
    {
        var frame = Frame(16, 1, (x, _) => x == 0 ? ColorConstants.Red : ColorConstants.White);

        var luminance = SingleZone(frame, ZoneAveraging.LuminanceWeighted);
        var colorful = SingleZone(frame, ZoneAveraging.ColorWeighted);

        Assert.True(luminance.G > 240);
        Assert.Equal(ColorConstants.Red, colorful);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(255)]
    public void ColorWeightingPreservesUniformNeutralContent(byte channel)
    {
        var color = new RgbColor(channel, channel, channel);
        Assert.Equal(color, SingleZone(Frame(8, 2, (_, _) => color), ZoneAveraging.ColorWeighted));
    }

    [Fact]
    public void ColorWeightingFallsBackToLuminanceForMixedNeutralContent()
    {
        var frame = Frame(8, 1, (x, _) => x < 4 ? ColorConstants.White : new RgbColor(90, 90, 90));
        Assert.Equal(SingleZone(frame, ZoneAveraging.LuminanceWeighted), SingleZone(frame, ZoneAveraging.ColorWeighted));
    }

    [Fact]
    public void ColorWeightingPreservesAUniformColour()
    {
        var color = new RgbColor(90, 140, 200);
        Assert.Equal(color, SingleZone(Frame(8, 2, (_, _) => color), ZoneAveraging.ColorWeighted));
    }

    [Fact]
    public void DefaultsToLuminanceWeighted()
    {
        Assert.Equal(ZoneAveraging.LuminanceWeighted, new ColorProcessorOptions().Averaging);
        Assert.Equal(ZoneAveraging.LuminanceWeighted, new ProcessingConfig().ParsedAveraging);
    }

    [Theory]
    [InlineData("mean", ZoneAveraging.Mean)]
    [InlineData("average", ZoneAveraging.Mean)]
    [InlineData("luminance-weighted", ZoneAveraging.LuminanceWeighted)]
    [InlineData("weighted", ZoneAveraging.LuminanceWeighted)]
    [InlineData(" MEAN ", ZoneAveraging.Mean)]
    [InlineData("colour-weighted", ZoneAveraging.ColorWeighted)]
    [InlineData("color-weighted", ZoneAveraging.ColorWeighted)]
    public void ParsesConfiguredAveragingModes(string value, ZoneAveraging expected)
    {
        Assert.True(ProcessingConfig.TryParseAveraging(value, out var averaging));
        Assert.Equal(expected, averaging);
    }

    [Fact]
    public void RejectsAnUnknownAveragingMode()
    {
        var config = new ProcessingConfig { Averaging = "median" };

        Assert.Contains(
            config.Validate(),
            p => p.Contains("processing.averaging", StringComparison.Ordinal));
    }

    [Fact]
    public void WeightingStillSplitsZonesIndependently()
    {
        // Left half red on black, right half blue on black; zones must not bleed into each other.
        var frame = Frame(8, 1, (x, _) => x switch
        {
            0 => ColorConstants.Red,
            4 => ColorConstants.Blue,
            _ => RgbColor.Black,
        });

        var processor = new ColorProcessor(
            new ZoneMapper(2, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false),
            new ColorProcessorOptions
            {
                Brightness = 1.0,
                Gamma = 1.0,
                Saturation = 1.0,
                Smoothing = 0.0,
                BlackLevel = 0.0,
                Averaging = ZoneAveraging.LuminanceWeighted,
            });

        var output = new RgbColor[2];
        processor.Process(frame, output);

        Assert.Equal(ColorConstants.Red, output[0]);
        Assert.Equal(ColorConstants.Blue, output[1]);
    }
}
