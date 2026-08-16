using LightSync.Core.Colors;

namespace LightSync.Core.Tests;

public class ColorMathTests
{
    private static ColorAdjustment Neutral => new(Brightness: 1.0, Gamma: 1.0, Saturation: 1.0, BlackLevel: 0.0);

    [Fact]
    public void NeutralAdjustmentLeavesColoursUntouched()
    {
        var color = new RgbColor(12, 200, 77);

        Assert.Equal(color, ColorMath.Adjust(color, Neutral));
    }

    [Fact]
    public void GammaAboveOneDarkensMidTones()
    {
        var midGrey = new RgbColor(128, 128, 128);

        var darkened = ColorMath.Adjust(midGrey, Neutral with { Gamma = 2.2 });

        Assert.True(darkened.R < midGrey.R);
        Assert.Equal(darkened.R, darkened.G);
        Assert.Equal(darkened.G, darkened.B);
    }

    [Fact]
    public void GammaBelowOneBrightensMidTones()
    {
        var midGrey = new RgbColor(128, 128, 128);

        var brightened = ColorMath.Adjust(midGrey, Neutral with { Gamma = 0.5 });

        Assert.True(brightened.R > midGrey.R);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void GammaFixesTheEndpointsOfTheRange(byte value)
    {
        var color = new RgbColor(value, value, value);

        Assert.Equal(color, ColorMath.Adjust(color, Neutral with { Gamma = 2.2 }));
    }

    [Fact]
    public void ZeroSaturationProducesGrey()
    {
        var result = ColorMath.Adjust(ColorConstants.Red, Neutral with { Saturation = 0.0 });

        Assert.Equal(result.R, result.G);
        Assert.Equal(result.G, result.B);
    }

    [Fact]
    public void ZeroSaturationPreservesLuminance()
    {
        var color = new RgbColor(200, 60, 30);
        var expected = ColorMath.Luminance(color);

        var grey = ColorMath.Adjust(color, Neutral with { Saturation = 0.0 });

        Assert.Equal(expected, ColorMath.Luminance(grey), 0.01);
    }

    [Fact]
    public void SaturationAboveOnePushesChannelsApart()
    {
        var muted = new RgbColor(150, 120, 120);

        var vivid = ColorMath.Adjust(muted, Neutral with { Saturation = 2.0 });

        Assert.True(vivid.R - vivid.B > muted.R - muted.B);
    }

    [Fact]
    public void SaturationLeavesGreyUnchanged()
    {
        var grey = new RgbColor(128, 128, 128);

        var result = ColorMath.Adjust(grey, Neutral with { Saturation = 3.0 });

        Assert.Equal(grey, result);
    }

    [Fact]
    public void BrightnessScalesLinearly()
    {
        var color = new RgbColor(100, 50, 20);

        var halved = ColorMath.Adjust(color, Neutral with { Brightness = 0.5 });

        Assert.Equal(50, halved.R);
        Assert.Equal(25, halved.G);
        Assert.Equal(10, halved.B);
    }

    [Fact]
    public void BrightnessAboveOneClampsRatherThanOverflowing()
    {
        var bright = new RgbColor(200, 200, 200);

        var result = ColorMath.Adjust(bright, Neutral with { Brightness = 4.0 });

        Assert.Equal(new RgbColor(255, 255, 255), result);
    }

    [Fact]
    public void BlackLevelCutsVeryDarkZonesToBlack()
    {
        var nearlyBlack = new RgbColor(2, 2, 2);

        var result = ColorMath.Adjust(nearlyBlack, Neutral with { BlackLevel = 0.05 });

        Assert.Equal(RgbColor.Black, result);
    }

    [Fact]
    public void BlackLevelLeavesZonesAboveTheThresholdAlone()
    {
        var dim = new RgbColor(80, 80, 80);

        var result = ColorMath.Adjust(dim, Neutral with { BlackLevel = 0.05 });

        Assert.NotEqual(RgbColor.Black, result);
    }

    [Fact]
    public void BlackLevelAppliesAfterBrightnessSoDimmingCanTripIt()
    {
        var dim = new RgbColor(40, 40, 40);
        var adjustment = Neutral with { Brightness = 0.1, BlackLevel = 0.05 };

        Assert.Equal(RgbColor.Black, ColorMath.Adjust(dim, adjustment));
    }

    [Fact]
    public void SmoothingWithZeroWeightReturnsTheTarget()
    {
        Assert.Equal(
            ColorConstants.Blue,
            ColorMath.Smooth(ColorConstants.Red, ColorConstants.Blue, previousWeight: 0.0));
    }

    [Fact]
    public void SmoothingBlendsProportionally()
    {
        var result = ColorMath.Smooth(new RgbColor(0, 0, 0), new RgbColor(100, 200, 40), previousWeight: 0.5);

        Assert.Equal(50, result.R);
        Assert.Equal(100, result.G);
        Assert.Equal(20, result.B);
    }

    [Fact]
    public void SmoothingConvergesOnTheTargetOverSuccessiveFrames()
    {
        var current = RgbColor.Black;

        for (var frame = 0; frame < 200; frame++)
        {
            current = ColorMath.Smooth(current, ColorConstants.White, previousWeight: 0.5);
        }

        Assert.Equal(ColorConstants.White, current);
    }

    [Fact]
    public void SmoothingReachesBlackRatherThanStrandingTheLampFaintlyLit()
    {
        // Rounding a 1 -> 0 blend used to land back on 1 forever, so a black screen left the
        // lamp glowing at #010101.
        var current = ColorConstants.White;

        for (var frame = 0; frame < 500; frame++)
        {
            current = ColorMath.Smooth(current, RgbColor.Black, previousWeight: 0.5);
        }

        Assert.Equal(RgbColor.Black, current);
    }

    [Fact]
    public void SmoothingAlwaysMakesProgressTowardsTheTarget()
    {
        // Even at a weight high enough that the arithmetic rounds to a standstill.
        var current = new RgbColor(5, 5, 5);

        var next = ColorMath.Smooth(current, RgbColor.Black, previousWeight: 0.999);

        Assert.True(next.R < current.R);
    }

    [Fact]
    public void SmoothingIsStableWhenAlreadyAtTheTarget()
    {
        var color = new RgbColor(37, 111, 210);

        Assert.Equal(color, ColorMath.Smooth(color, color, previousWeight: 0.9));
    }

    [Fact]
    public void LuminanceWeightsGreenMostHeavily()
    {
        Assert.True(ColorMath.Luminance(ColorConstants.Green) > ColorMath.Luminance(ColorConstants.Red));
        Assert.True(ColorMath.Luminance(ColorConstants.Red) > ColorMath.Luminance(ColorConstants.Blue));
    }

    [Fact]
    public void LuminanceSpansZeroToOne()
    {
        Assert.Equal(0.0, ColorMath.Luminance(RgbColor.Black));
        Assert.Equal(1.0, ColorMath.Luminance(ColorConstants.White), 0.001);
    }

    [Fact]
    public void AveragesBgrxPixelsIntoRgb()
    {
        // Two pixels in BGRx order: pure blue, then pure red.
        byte[] pixels = [255, 0, 0, 0, 0, 0, 255, 0];

        var average = ColorMath.AverageBgrx(pixels);

        Assert.Equal(new RgbColor(127, 0, 127), average);
    }

    [Fact]
    public void AveragingIgnoresThePaddingByte()
    {
        byte[] withPadding = [10, 20, 30, 255];
        byte[] withoutPadding = [10, 20, 30, 0];

        Assert.Equal(ColorMath.AverageBgrx(withPadding), ColorMath.AverageBgrx(withoutPadding));
        Assert.Equal(new RgbColor(30, 20, 10), ColorMath.AverageBgrx(withPadding));
    }

    [Fact]
    public void AveragingAnEmptySpanIsBlack()
    {
        Assert.Equal(RgbColor.Black, ColorMath.AverageBgrx([]));
    }

    [Fact]
    public void AveragingIgnoresATrailingPartialPixel()
    {
        byte[] onePixelPlusJunk = [10, 20, 30, 0, 99];

        Assert.Equal(new RgbColor(30, 20, 10), ColorMath.AverageBgrx(onePixelPlusJunk));
    }
}
