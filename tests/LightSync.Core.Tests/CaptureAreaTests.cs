using LightSync.Core.Capture;

namespace LightSync.Core.Tests;

public class CaptureAreaTests
{
    private static readonly DisplayInfo UltraWide =
        new(0, "HDMI-A-1", "Microstep MPG 491C OLED", 0, 0, 5120, 1440, 144.0, 1.0);

    private static readonly DisplayInfo SecondaryToTheRight =
        new(1, "DP-1", "Secondary", 5120, 0, 1920, 1080, 60.0, 1.0);

    [Fact]
    public void AcceptsRectangleInsideTheDisplay()
    {
        var area = new CaptureArea(3200, 200, 1600, 1000);

        Assert.True(area.TryValidateWithin(UltraWide, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void AcceptsRectangleExactlyFillingTheDisplay()
    {
        Assert.True(UltraWide.FullArea.TryValidateWithin(UltraWide, out _));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    public void RejectsNonPositiveSize(int width, int height)
    {
        var area = new CaptureArea(0, 0, width, height);

        Assert.False(area.TryValidate(out var error));
        Assert.Contains("positive", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNegativeOrigin()
    {
        var area = new CaptureArea(-10, 0, 100, 100);

        Assert.False(area.TryValidate(out var error));
        Assert.Contains("negative", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsRectangleRunningOffTheRightEdge()
    {
        var area = new CaptureArea(5000, 0, 200, 100);

        Assert.False(area.TryValidateWithin(UltraWide, out var error));
        Assert.Contains("does not fit", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsRectangleRunningOffTheBottomEdge()
    {
        var area = new CaptureArea(0, 1400, 100, 100);

        Assert.False(area.TryValidateWithin(UltraWide, out _));
    }

    [Fact]
    public void RejectsRectangleBelongingToAnotherDisplay()
    {
        var onSecondary = new CaptureArea(5200, 100, 400, 300);

        Assert.False(onSecondary.TryValidateWithin(UltraWide, out _));
        Assert.True(onSecondary.TryValidateWithin(SecondaryToTheRight, out _));
    }

    [Fact]
    public void ComputesEdgesAndPixelCount()
    {
        var area = new CaptureArea(3200, 200, 1600, 1000);

        Assert.Equal(4800, area.Right);
        Assert.Equal(1200, area.Bottom);
        Assert.Equal(1_600_000L, area.PixelCount);
    }

    [Fact]
    public void ConvertsToDisplayRelativeCoordinates()
    {
        var area = new CaptureArea(5200, 100, 400, 300);

        var relative = area.ToDisplayRelative(SecondaryToTheRight);

        Assert.Equal(new CaptureArea(80, 100, 400, 300), relative);
    }

    [Fact]
    public void DisplayRelativeIsIdentityForDisplayAtOrigin()
    {
        var area = new CaptureArea(3200, 200, 1600, 1000);

        Assert.Equal(area, area.ToDisplayRelative(UltraWide));
    }

    [Fact]
    public void FormatsAsGeometryString()
    {
        Assert.Equal("1600x1000+3200+200", new CaptureArea(3200, 200, 1600, 1000).ToString());
    }
}
