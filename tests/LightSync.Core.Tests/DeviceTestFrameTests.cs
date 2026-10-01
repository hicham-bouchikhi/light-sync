using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Core.Tests;

public sealed class DeviceTestFrameTests
{
    [Fact]
    public void KeepsExactChannelBytesAndFullZoneCount()
    {
        var frame = new DeviceTestFrame(24);
        var color = new RgbColor(1, 127, 254);
        frame.SetAll(color);
        Assert.Equal(24, frame.Colors.Length);
        Assert.All(frame.Colors.ToArray(), actual => Assert.Equal(color, actual));
    }

    [Fact]
    public void ChangingOneLedPreservesTheOthers()
    {
        var frame = new DeviceTestFrame(3);
        frame.SetAll(new RgbColor(7, 8, 9));
        frame.SetZone(1, new RgbColor(255, 0, 0));
        Assert.Equal(new[] { new RgbColor(7, 8, 9), new RgbColor(255, 0, 0), new RgbColor(7, 8, 9) }, frame.Colors.ToArray());
    }

    [Fact]
    public void IsolatingALedBlacksOutAllOtherLeds()
    {
        var frame = new DeviceTestFrame(3);
        frame.SetAll(new RgbColor(255, 255, 255));
        frame.Isolate(2, new RgbColor(0, 0, 255));
        Assert.Equal(new[] { RgbColor.Black, RgbColor.Black, new RgbColor(0, 0, 255) }, frame.Colors.ToArray());
    }

    [Fact]
    public void RejectsAnInvalidIndexWithoutChangingTheFrame()
    {
        var frame = new DeviceTestFrame(3);
        var color = new RgbColor(12, 34, 56);
        frame.SetAll(color);
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Isolate(3, RgbColor.Black));
        Assert.All(frame.Colors.ToArray(), actual => Assert.Equal(color, actual));
    }
}
