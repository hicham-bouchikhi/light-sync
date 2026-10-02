using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;

namespace LightSync.Core.Tests;

public class RecoveringFrameSourceTests
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly bool[] RecoveryStates = [true, false];

    [Fact]
    public async Task ReconnectsAfterAStallAndKeepsPreviewSequenceIncreasing()
    {
        await using var frozen = new TestSource(stallReads: true);
        await using var healthy = new TestSource();
        var opened = 0;
        var prepared = 0;
        List<bool> states = [];
        await using var source = new RecoveringFrameSource(
            () => ++opened == 1 ? frozen : healthy, FrameTimeout, FrameTimeout, states.Add,
            _ =>
            {
                Assert.Equal(1, frozen.DisposeCount);
                prepared++;
                return Task.CompletedTask;
            });

        var first = await source.StartAsync(TestContext.Current.CancellationToken);
        var recovered = await source.ReadFrameAsync(TestContext.Current.CancellationToken);
        var next = await source.ReadFrameAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, opened);
        Assert.Equal(1, prepared);
        Assert.Equal(1, frozen.DisposeCount);
        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, recovered.Sequence);
        Assert.Equal(3, next.Sequence);
        Assert.True(recovered.Timestamp >= first.Timestamp);
        Assert.Equal(RecoveryStates, states);
    }

    [Fact]
    public async Task ASecondResizeCanRecoverAgain()
    {
        var opened = 0;
        await using var source = new RecoveringFrameSource(() =>
        {
            opened++;
            return new TestSource(stallReads: true);
        }, FrameTimeout, FrameTimeout);

        await source.StartAsync(TestContext.Current.CancellationToken);
        await source.ReadFrameAsync(TestContext.Current.CancellationToken);
        var secondRecovery = await source.ReadFrameAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, opened);
        Assert.Equal(3, secondRecovery.Sequence);
    }

    [Fact]
    public async Task StopsRetryingAndReportsAPersistentStall()
    {
        var opened = 0;
        List<TestSource> readers = [];
        List<bool> states = [];
        await using var source = new RecoveringFrameSource(() =>
        {
            var reader = new TestSource(stallStart: ++opened > 1, stallReads: true);
            readers.Add(reader);
            return reader;
        }, FrameTimeout, FrameTimeout, states.Add);

        await source.StartAsync(TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<CaptureException>(
            () => source.ReadFrameAsync(TestContext.Current.CancellationToken));

        Assert.Contains("three reconnects", error.Message, StringComparison.Ordinal);
        Assert.Equal(4, opened);
        Assert.All(readers, reader => Assert.False(reader.Reading));
        Assert.Equal(RecoveryStates, states);
    }

    [Fact]
    public async Task UserCancellationDoesNotReconnect()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var opened = 0;
        List<bool> states = [];
        await using var source = new RecoveringFrameSource(() =>
        {
            opened++;
            return new TestSource(stallReads: true, onRead: cancellation.Cancel);
        }, TimeSpan.FromSeconds(10), FrameTimeout, states.Add);

        await source.StartAsync(cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadFrameAsync(cancellation.Token));

        Assert.Equal(1, opened);
        Assert.Empty(states);
    }

    [Fact]
    public async Task CleanEndDoesNotReopenTheSharedSource()
    {
        var opened = 0;
        await using var source = new RecoveringFrameSource(() =>
        {
            opened++;
            return new TestSource(endReads: true);
        }, FrameTimeout, FrameTimeout);

        await source.StartAsync(TestContext.Current.CancellationToken);
        Assert.True((await source.ReadFrameAsync(TestContext.Current.CancellationToken)).IsEmpty);
        Assert.Equal(1, opened);
    }

    private sealed class TestSource(bool stallStart = false, bool stallReads = false,
        bool endReads = false, Action? onRead = null) : IFrameSource
    {
        private long sequence;

        public bool Reading { get; private set; }

        public int DisposeCount { get; private set; }

        public Task<CapturedFrame> StartAsync(CancellationToken cancellationToken) => stallStart
            ? StallAsync(cancellationToken) : Task.FromResult(Frame());

        public Task<CapturedFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            onRead?.Invoke();
            return stallReads ? StallAsync(cancellationToken)
                : Task.FromResult(endReads ? default : Frame());
        }

        private CapturedFrame Frame() => new(new byte[4], 1, 1, ++sequence, TimeSpan.Zero);

        private async Task<CapturedFrame> StallAsync(CancellationToken cancellationToken)
        {
            Reading = true;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return default;
            }
            finally
            {
                Reading = false;
            }
        }

        public ValueTask DisposeAsync()
        {
            // A cancelled read must finish before its source/buffer is torn down.
            Assert.False(Reading);
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
