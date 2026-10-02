using System.Threading.Channels;
using LightSync.Core.Audio;

namespace LightSync.Core.Tests;

public sealed class ScreenAudioMonitorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DisabledEffectDoesNotOpenAudioCapture()
    {
        var created = 0;
        await using var monitor = new ScreenAudioMonitor(new ScreenBrightness(new()), () =>
        {
            created++;
            return new ControlledCapture();
        });
        await monitor.SetEnabledAsync(false, Token);
        Assert.Equal(0, created);
    }

    [Fact]
    public async Task DisableCancelsBlockedAudioReadsAndReenableUsesFreshCapture()
    {
        await using var first = new ControlledCapture();
        await using var second = new ControlledCapture();
        var created = 0;
        var brightness = new ScreenBrightness(new());
        await using var monitor = new ScreenAudioMonitor(brightness, () => ++created == 1 ? first : second);
        await monitor.SetEnabledAsync(true, Token);
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await first.Samples.Writer.WriteAsync(new float[] { 1, -1 }, Token);
        await WaitUntilAsync(() => brightness.Status.HasAudio);
        await monitor.SetEnabledAsync(true, Token);
        Assert.Equal(1, created);
        await monitor.SetEnabledAsync(false, Token);
        Assert.True(first.Disposed.Task.IsCompletedSuccessfully);
        Assert.Equal(0.75, brightness.Status.Brightness);
        await monitor.SetEnabledAsync(true, Token);
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Equal(2, created);
        Assert.False(brightness.Status.HasAudio);
        await monitor.DisposeAsync();
        Assert.True(second.Disposed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CaptureFailureFallsBackToBaselineWithAnError()
    {
        await using var capture = new ControlledCapture();
        var brightness = new ScreenBrightness(new());
        await using var monitor = new ScreenAudioMonitor(brightness, () => capture);
        await monitor.SetEnabledAsync(true, Token);
        await capture.Samples.Writer.WriteAsync(ReadOnlyMemory<float>.Empty, Token);
        await WaitUntilAsync(() => brightness.Status.AudioError is not null);
        Assert.Contains("ended", brightness.Status.AudioError!, StringComparison.Ordinal);
        Assert.Equal(0.75, brightness.Status.Brightness);
        Assert.True(capture.Disposed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task MissingAudioDependencyDoesNotFaultTheScreenSession()
    {
        var brightness = new ScreenBrightness(new());
        await using var monitor = new ScreenAudioMonitor(brightness, () => throw new IOException("parec unavailable"));
        await monitor.SetEnabledAsync(true, Token);
        await WaitUntilAsync(() => brightness.Status.AudioError is not null);
        Assert.Equal("parec unavailable", brightness.Status.AudioError);
        Assert.Equal(0.75, brightness.Status.Brightness);
    }

    [Fact]
    public async Task SessionCancellationReleasesAudioCapture()
    {
        await using var capture = new ControlledCapture();
        var brightness = new ScreenBrightness(new());
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await using var monitor = new ScreenAudioMonitor(brightness, () => capture);
        await monitor.SetEnabledAsync(true, stopping.Token);
        await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await stopping.CancelAsync();
        await capture.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await monitor.DisposeAsync();
        Assert.Equal(0.75, brightness.Status.Brightness);
        Assert.Null(brightness.Status.AudioError);
    }

    [Fact]
    public async Task QueuedTogglesAndDisposalReleaseEveryCreatedCapture()
    {
        var captures = new System.Collections.Concurrent.ConcurrentBag<ControlledCapture>();
        await using var monitor = new ScreenAudioMonitor(new ScreenBrightness(new()), () =>
        {
            var capture = new ControlledCapture();
            captures.Add(capture);
            return capture;
        });
        var enable = monitor.SetEnabledAsync(true, Token);
        var disable = monitor.SetEnabledAsync(false, Token);
        var reenable = monitor.SetEnabledAsync(true, Token);
        var dispose = monitor.DisposeAsync().AsTask();
        await Task.WhenAll(enable, disable, reenable, dispose).WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.All(captures, capture => Assert.True(capture.Disposed.Task.IsCompletedSuccessfully));
        var count = captures.Count;
        await monitor.SetEnabledAsync(true, Token);
        Assert.Equal(count, captures.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, deadline.Token);
        }
    }

    private sealed class ControlledCapture : IAudioCapture
    {
        public Channel<ReadOnlyMemory<float>> Samples { get; } = Channel.CreateUnbounded<ReadOnlyMemory<float>>();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SampleRate => 48000;

        public int Channels => 2;

        public int SamplesPerChannel => 1024;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Started.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task<ReadOnlyMemory<float>> ReadAsync(CancellationToken cancellationToken) =>
            await Samples.Reader.ReadAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
