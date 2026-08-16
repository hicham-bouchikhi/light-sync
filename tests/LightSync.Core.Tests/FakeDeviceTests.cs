using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Core.Tests;

public class FakeDeviceTests
{
    [Fact]
    public async Task RecordsEveryFrameItReceives()
    {
        await using var device = new FakeDevice(maximumZones: 4);
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        RgbColor[] first = [ColorConstants.Red, ColorConstants.Green];
        RgbColor[] second = [ColorConstants.Blue, ColorConstants.White];

        await device.SendFrameAsync(first, TestContext.Current.CancellationToken);
        await device.SendFrameAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(2, device.FrameCount);
        Assert.Equal(first, device.SnapshotFrames()[0]);
        Assert.Equal(second, device.GetLastFrame());
    }

    [Fact]
    public async Task CopiesFramesSoLaterBufferReuseCannotCorruptHistory()
    {
        await using var device = new FakeDevice();
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        var buffer = new RgbColor[] { ColorConstants.Red };
        await device.SendFrameAsync(buffer, TestContext.Current.CancellationToken);
        buffer[0] = ColorConstants.Blue;

        Assert.Equal(ColorConstants.Red, device.GetLastFrame()![0]);
    }

    [Fact]
    public async Task RejectsFrameWithMoreZonesThanTheDeviceSupports()
    {
        await using var device = new FakeDevice(maximumZones: 2);
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        RgbColor[] tooMany = [ColorConstants.Red, ColorConstants.Green, ColorConstants.Blue];

        await Assert.ThrowsAsync<DeviceException>(
            () => device.SendFrameAsync(tooMany, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsFramesBeforeConnect()
    {
        await using var device = new FakeDevice();

        await Assert.ThrowsAsync<DeviceException>(
            () => device.SendFrameAsync(new RgbColor[1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordsStaticColor()
    {
        await using var device = new FakeDevice();
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        await device.SetStaticColorAsync(ColorConstants.Blue, TestContext.Current.CancellationToken);

        Assert.Equal(ColorConstants.Blue, device.LastStaticColor);
    }

    [Fact]
    public async Task TracksConnectionState()
    {
        await using var device = new FakeDevice();
        Assert.False(device.IsConnected);

        await device.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.True(device.IsConnected);

        await device.DisconnectAsync(TestContext.Current.CancellationToken);
        Assert.False(device.IsConnected);
    }

    [Fact]
    public async Task HonoursCancellation()
    {
        await using var device = new FakeDevice();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => device.ConnectAsync(cts.Token));
    }

    [Fact]
    public void RejectsNonPositiveMaximumZones()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeDevice(maximumZones: 0));
    }
}
