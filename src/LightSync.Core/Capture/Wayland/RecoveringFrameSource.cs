using System.Diagnostics;

namespace LightSync.Core.Capture.Wayland;

/// <summary>
/// Reconnects a stalled reader to the same portal-authorized node. The portal session stays
/// open, so recovery does not ask the user to select a window again.
/// </summary>
internal sealed class RecoveringFrameSource(
    Func<IFrameSource> createSource,
    TimeSpan frameTimeout,
    TimeSpan startupTimeout,
    Action<bool>? onRecovering = null,
    Func<CancellationToken, Task>? prepareRestart = null) : IFrameSource
{
    private const int MaximumRecoveryAttempts = 3;
    private IFrameSource? source;
    private CancellationTokenSource? deadline;
    private long sequence;
    private long startedAt;

    public async Task<CapturedFrame> StartAsync(CancellationToken cancellationToken)
    {
        if (source is not null)
        {
            throw new InvalidOperationException("The frame source is already started.");
        }

        startedAt = Stopwatch.GetTimestamp();
        CreateSource();
        return Stamp(await ReadAsync(start: true, startupTimeout, cancellationToken));
    }

    public async Task<CapturedFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Stamp(await ReadAsync(start: false, frameTimeout, cancellationToken));
        }
        catch (Exception ex) when (ex is TimeoutException or CaptureException
            && !cancellationToken.IsCancellationRequested)
        {
            onRecovering?.Invoke(true);
            try
            {
                var lastError = ex;
                for (var attempt = 0; attempt < MaximumRecoveryAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // The timed-out read has observed cancellation before any buffer/process
                    // is disposed. Never leave a borrowed-buffer read running across restart.
                    await CloseSourceAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (prepareRestart is not null)
                        {
                            await prepareRestart(cancellationToken);
                        }

                        CreateSource();
                        var frame = await ReadAsync(start: true, startupTimeout, cancellationToken);
                        if (!frame.IsEmpty)
                        {
                            return Stamp(frame);
                        }

                        lastError = new CaptureException("The reconnected screen source ended without a frame.");
                    }
                    catch (Exception retryError) when (retryError is TimeoutException or CaptureException
                        && !cancellationToken.IsCancellationRequested)
                    {
                        lastError = retryError;
                    }
                }

                throw new CaptureException(
                    "Screen capture stopped delivering frames and could not recover after three reconnects. " +
                    "Choose the source again. On Hyprland, this can be caused by the desktop portal's window-resize bug.",
                    lastError);
            }
            finally
            {
                onRecovering?.Invoke(false);
            }
        }
    }

    private void CreateSource()
    {
        source = createSource();
        deadline = new CancellationTokenSource();
    }

    private async Task<CapturedFrame> ReadAsync(bool start, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var current = source ?? throw new InvalidOperationException("StartAsync must be called before ReadFrameAsync.");
        var currentDeadline = deadline!;
        cancellationToken.ThrowIfCancellationRequested();
        // Reuse one deadline/timer for the lifetime of the reader instead of allocating a
        // linked cancellation source and timeout task for every captured frame.
        using var registration = cancellationToken.UnsafeRegister(
            static state => ((CancellationTokenSource)state!).Cancel(), currentDeadline);
        currentDeadline.CancelAfter(timeout);
        try
        {
            return await (start ? current.StartAsync(currentDeadline.Token) : current.ReadFrameAsync(currentDeadline.Token));
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested
            && currentDeadline.IsCancellationRequested)
        {
            throw new TimeoutException("The screen source stopped delivering frames.", ex);
        }
        finally
        {
            currentDeadline.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    private CapturedFrame Stamp(CapturedFrame frame) => frame.IsEmpty ? frame
        : frame with { Sequence = ++sequence, Timestamp = Stopwatch.GetElapsedTime(startedAt) };

    private async ValueTask CloseSourceAsync()
    {
        if (source is not null)
        {
            await source.DisposeAsync();
            source = null;
        }

        deadline?.Dispose();
        deadline = null;
    }

    public ValueTask DisposeAsync() => CloseSourceAsync();
}
