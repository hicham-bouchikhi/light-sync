using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;

namespace LightSync.Core.Tests;

public class HyprlandDisplayEnumeratorTests
{
    /// <summary>Trimmed but otherwise verbatim output from the development machine.</summary>
    private const string RealOutput = """
        [{
            "id": 0,
            "name": "HDMI-A-1",
            "description": "Microstep MPG 491C OLED 0x01010101",
            "make": "Microstep",
            "model": "MPG 491C OLED",
            "width": 5120,
            "height": 1440,
            "refreshRate": 144.00000,
            "x": 0,
            "y": 0,
            "scale": 1,
            "transform": 0,
            "focused": true
        }]
        """;

    [Fact]
    public void DiscoversTheUltraWideWithoutAnyUserInput()
    {
        var displays = HyprlandDisplayEnumerator.ParseMonitors(RealOutput);

        var display = Assert.Single(displays);
        Assert.Equal(0, display.Id);
        Assert.Equal("HDMI-A-1", display.Name);
        Assert.Equal(5120, display.Width);
        Assert.Equal(1440, display.Height);
        Assert.Equal(0, display.X);
        Assert.Equal(0, display.Y);
        Assert.Equal(144.0, display.RefreshRate, 3);
        Assert.Equal(1.0, display.Scale);
    }

    [Fact]
    public void ParsesMultipleMonitorsWithTheirLayoutOffsets()
    {
        const string json = """
            [
              { "id": 0, "name": "HDMI-A-1", "width": 5120, "height": 1440, "x": 0, "y": 0,
                "refreshRate": 144.0, "scale": 1 },
              { "id": 1, "name": "DP-1", "width": 1920, "height": 1080, "x": 5120, "y": 0,
                "refreshRate": 60.0, "scale": 1 }
            ]
            """;

        var displays = HyprlandDisplayEnumerator.ParseMonitors(json);

        Assert.Equal(2, displays.Count);
        Assert.Equal(5120, displays[1].X);
        Assert.Equal(7040, displays[1].Right);
    }

    [Fact]
    public void TreatsAMissingScaleAsUnscaled()
    {
        const string json = """[{ "id": 0, "name": "HDMI-A-1", "width": 800, "height": 600 }]""";

        var display = Assert.Single(HyprlandDisplayEnumerator.ParseMonitors(json));

        Assert.Equal(1.0, display.Scale);
    }

    [Fact]
    public void ThrowsWhenNoMonitorsAreConnected()
    {
        Assert.Throws<DisplayEnumerationException>(() => HyprlandDisplayEnumerator.ParseMonitors("[]"));
    }

    [Fact]
    public void ThrowsOnUnparseableOutput()
    {
        Assert.Throws<DisplayEnumerationException>(
            () => HyprlandDisplayEnumerator.ParseMonitors("not json at all"));
    }

    [Fact]
    public void ValidatesTheDocumentedCaptureAreaAgainstTheDiscoveredDisplay()
    {
        var display = Assert.Single(HyprlandDisplayEnumerator.ParseMonitors(RealOutput));
        var area = new CaptureArea(3200, 200, 1600, 1000);

        Assert.True(area.TryValidateWithin(display, out _));
    }
}
