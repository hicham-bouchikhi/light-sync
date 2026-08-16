using LightSync.Core.Colors;

namespace LightSync.Core.Tests;

public class HsvColorTests
{
    [Fact]
    public void ConvertsPrimariesToTheExpectedHues()
    {
        Assert.Equal(0, HsvColor.FromRgb(ColorConstants.Red).Hue);
        Assert.Equal(120, HsvColor.FromRgb(ColorConstants.Green).Hue);
        Assert.Equal(240, HsvColor.FromRgb(ColorConstants.Blue).Hue);
    }

    [Fact]
    public void PrimariesAreFullySaturatedAtFullValue()
    {
        var red = HsvColor.FromRgb(ColorConstants.Red);

        Assert.Equal(100, red.Saturation);
        Assert.Equal(100, red.Value);
    }

    [Fact]
    public void WhiteHasNoSaturation()
    {
        var white = HsvColor.FromRgb(ColorConstants.White);

        Assert.Equal(0, white.Saturation);
        Assert.Equal(100, white.Value);
    }

    [Fact]
    public void BlackHasNoValue()
    {
        var black = HsvColor.FromRgb(RgbColor.Black);

        Assert.Equal(0, black.Saturation);
        Assert.Equal(0, black.Value);
    }

    [Fact]
    public void GreyHasNoSaturationButKeepsItsValue()
    {
        var grey = HsvColor.FromRgb(new RgbColor(128, 128, 128));

        Assert.Equal(0, grey.Saturation);
        Assert.Equal(50, grey.Value);
    }

    [Theory]
    [InlineData(255, 255, 0, 60)]
    [InlineData(0, 255, 255, 180)]
    [InlineData(255, 0, 255, 300)]
    public void ConvertsSecondaryColours(byte r, byte g, byte b, int expectedHue)
    {
        Assert.Equal(expectedHue, HsvColor.FromRgb(new RgbColor(r, g, b)).Hue);
    }

    [Fact]
    public void KeepsHueWithinZeroToThreeFiftyNine()
    {
        for (var r = 0; r < 256; r += 17)
        {
            for (var g = 0; g < 256; g += 17)
            {
                for (var b = 0; b < 256; b += 17)
                {
                    var hsv = HsvColor.FromRgb(new RgbColor((byte)r, (byte)g, (byte)b));

                    Assert.InRange(hsv.Hue, 0, 359);
                    Assert.InRange(hsv.Saturation, 0, 100);
                    Assert.InRange(hsv.Value, 0, 100);
                }
            }
        }
    }

    [Fact]
    public void DarkeningAColourReducesValueButKeepsHue()
    {
        var bright = HsvColor.FromRgb(new RgbColor(200, 100, 50));
        var dark = HsvColor.FromRgb(new RgbColor(100, 50, 25));

        Assert.Equal(bright.Hue, dark.Hue);
        Assert.True(dark.Value < bright.Value);
    }
}
