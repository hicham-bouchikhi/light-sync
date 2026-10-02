using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Devices;
using LightSync.Core.Mapping;
using LightSync.Core.Pipeline;
using LightSync.Core.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightSync.Core.Tests;

public class LightSyncPipelineTests
{
    private const int ZoneCount = 4;

    private static CaptureRequest Request(int fps = 60) =>
        new(DisplayId: 0, Area: new CaptureArea(0, 0, 400, 100), Fps: fps, TargetWidth: 16, TargetHeight: 4);

    private static ColorProcessor Processor() => new(
        new ZoneMapper(ZoneCount, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false),
        new ColorProcessorOptions { Smoothing = 0.0 });

    private static LightSyncPipeline Build(
        IScreenCapture capture,
        ILightDevice device,
        PipelineMetrics metrics) =>
        new(capture, Processor(), device, metrics, NullLogger<LightSyncPipeline>.Instance);

    [Fact]
    public async Task DeliversFramesFromCaptureThroughToTheDevice()
    {
        await using var capture = new SyntheticScreenCapture { FrameLimit = 10 };
        await using var device = new FakeDevice(maximumZones: ZoneCount);
        await device.ConnectAsync(TestContext.Current.CancellationToken);
        var metrics = new PipelineMetrics();

        await Build(capture, device, metrics).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(metrics.CapturedFrames >= 10);
        Assert.True(device.FrameCount > 0);
        Assert.All(device.SnapshotFrames(), frame => Assert.Equal(ZoneCount, frame.Length));
    }

    [Fact]
    public async Task ProducesDistinctColoursPerZoneFromTheTestPattern()
    {
        await using var capture = new SyntheticScreenCapture { FrameLimit = 3 };
        await using var device = new FakeDevice(maximumZones: ZoneCount);
        await device.ConnectAsync(TestContext.Current.CancellationToken);

        await Build(capture, device, new PipelineMetrics())
            .RunAsync(Request(), TestContext.Current.CancellationToken);

        var frame = device.GetLastFrame();
        Assert.NotNull(frame);
        Assert.True(frame.Distinct().Count() > 1, "the sweeping test pattern should differ across zones");
    }

    [Fact]
    public async Task DropsOldFramesRatherThanQueueingWhenTheDeviceIsSlow()
    {
        await using var capture = new SyntheticScreenCapture { FrameLimit = 40 };
        await using var device = new SlowDevice(ZoneCount, delay: TimeSpan.FromMilliseconds(20));
        await device.ConnectAsync(TestContext.Current.CancellationToken);
        var metrics = new PipelineMetrics();

        await Build(capture, device, metrics).RunAsync(Request(fps: 240), TestContext.Current.CancellationToken);

        Assert.True(metrics.DroppedFrames > 0, "a device slower than capture must cause drops");
        Assert.True(metrics.SentFrames < metrics.CapturedFrames);
    }

    [Fact]
    public async Task StopsCleanlyOnCancellation()
    {
        await using var capture = new SyntheticScreenCapture();
        await using var device = new FakeDevice(maximumZones: ZoneCount);
        await device.ConnectAsync(TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();

        var run = Build(capture, device, new PipelineMetrics()).RunAsync(Request(), cts.Token);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        // Completes rather than hanging, and does not surface cancellation as a failure.
        await run;
    }

    [Fact]
    public async Task KeepsRunningAfterATransientDeviceError()
    {
        await using var capture = new SyntheticScreenCapture { FrameLimit = 20 };
        await using var device = new FlakyDevice(ZoneCount, failEveryNth: 2);
        await device.ConnectAsync(TestContext.Current.CancellationToken);
        var metrics = new PipelineMetrics();

        await Build(capture, device, metrics).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(metrics.DeviceErrors > 0);
        Assert.Equal("simulated transient failure", metrics.LastDeviceError);
        Assert.True(metrics.SentFrames > 0, "frames after a failure should still be delivered");
    }

    [Fact]
    public async Task RefusesToStartWhenTheDeviceCannotRenderTheConfiguredZones()
    {
        await using var capture = new SyntheticScreenCapture();
        await using var device = new FakeDevice(maximumZones: 2);

        var pipeline = Build(capture, device, new PipelineMetrics());

        var exception = await Assert.ThrowsAsync<DeviceException>(
            () => pipeline.RunAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Contains("at most 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAnInvalidCaptureRequest()
    {
        await using var capture = new SyntheticScreenCapture();
        await using var device = new FakeDevice(maximumZones: ZoneCount);
        var invalid = Request() with { Fps = 0 };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Build(capture, device, new PipelineMetrics())
                .RunAsync(invalid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecordsLatencyAndFrameRate()
    {
        await using var capture = new SyntheticScreenCapture { FrameLimit = 15 };
        await using var device = new FakeDevice(maximumZones: ZoneCount);
        await device.ConnectAsync(TestContext.Current.CancellationToken);
        var metrics = new PipelineMetrics();

        await Build(capture, device, metrics).RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(metrics.SentFrames > 0);
        Assert.True(metrics.AverageLatency > TimeSpan.Zero);
        Assert.True(metrics.AverageLatency < TimeSpan.FromSeconds(1));
        Assert.Contains("fps", metrics.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneCaptureFeedsDevicesWithDifferentLedCounts()
    {
        await using var capture = new SyntheticScreenCapture { FrameLimit = 10 };
        await using var first = new FakeDevice(maximumZones: 4);
        await using var second = new FakeDevice(maximumZones: 7);
        await first.ConnectAsync(TestContext.Current.CancellationToken);
        await second.ConnectAsync(TestContext.Current.CancellationToken);
        var secondProcessor = new ColorProcessor(
            new ZoneMapper(7, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false),
            new ColorProcessorOptions { Smoothing = 0 });
        var metrics = new PipelineMetrics();
        var pipeline = new LightSyncPipeline(capture,
            [new(Processor(), first), new(secondProcessor, second)], metrics, NullLogger<LightSyncPipeline>.Instance);

        await pipeline.RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(10, metrics.CapturedFrames);
        Assert.True(first.FrameCount > 0);
        Assert.True(second.FrameCount > 0);
        Assert.All(first.SnapshotFrames(), frame => Assert.Equal(4, frame.Length));
        Assert.All(second.SnapshotFrames(), frame => Assert.Equal(7, frame.Length));
        Assert.True(first.GetLastFrame()!.Distinct().Count() > 1);
        Assert.True(second.GetLastFrame()!.Distinct().Count() > 1);
    }

    [Fact]
    public async Task ASlowPeripheralDoesNotHoldUpOtherScreenOutputs()
    {
        await using var capture = new SyntheticScreenCapture { FrameLimit = 60 };
        await using var slow = new SlowDevice(ZoneCount, TimeSpan.FromMilliseconds(40));
        await using var fast = new FakeDevice(maximumZones: ZoneCount);
        await fast.ConnectAsync(TestContext.Current.CancellationToken);
        var pipeline = new LightSyncPipeline(capture,
            [new(Processor(), slow), new(Processor(), fast)], new PipelineMetrics(), NullLogger<LightSyncPipeline>.Instance);

        await pipeline.RunAsync(Request(fps: 240), TestContext.Current.CancellationToken);

        Assert.True(fast.FrameCount > slow.FrameCount * 2,
            $"fast peripheral received {fast.FrameCount} frames, slow peripheral received {slow.FrameCount}");
    }

    [Fact]
    public async Task AFailingPeripheralDoesNotStopOtherScreenOutputs()
    {
        await using var capture = new SyntheticScreenCapture { FrameLimit = 10 };
        await using var failing = new FlakyDevice(ZoneCount, failEveryNth: 1);
        await using var healthy = new FakeDevice(maximumZones: ZoneCount);
        await healthy.ConnectAsync(TestContext.Current.CancellationToken);
        var metrics = new PipelineMetrics();
        var pipeline = new LightSyncPipeline(capture,
            [new(Processor(), failing), new(Processor(), healthy)], metrics, NullLogger<LightSyncPipeline>.Instance);

        await pipeline.RunAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(metrics.DeviceErrors > 0);
        Assert.True(healthy.FrameCount > 0);
        Assert.Contains("Flaky", metrics.LastDeviceError, StringComparison.Ordinal);
    }

    private sealed class SlowDevice(int zones, TimeSpan delay) : ILightDevice
    {
        public string Name => "Slow";

        public DeviceCapabilities Capabilities { get; } = new(zones, true, true, true, false, true);

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public long FrameCount { get; private set; }

        public async Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            FrameCount++;
        }

        public Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FlakyDevice(int zones, int failEveryNth) : ILightDevice
    {
        private int calls;

        public string Name => "Flaky";

        public DeviceCapabilities Capabilities { get; } = new(zones, true, true, true, false, true);

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
        {
            calls++;
            return calls % failEveryNth == 0
                ? Task.FromException(new DeviceException("simulated transient failure"))
                : Task.CompletedTask;
        }

        public Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
