namespace LightSync.Core.Audio;

/// <summary>Owns optional audio capture without coupling capture failures to screen output.</summary>
public sealed class ScreenAudioMonitor(ScreenBrightness brightness, Func<IAudioCapture> createCapture) : IAsyncDisposable
{
    private readonly Lock gate = new();
    private Task transition = Task.CompletedTask;
    private CancellationTokenSource? stopping;
    private Task? running;
    private bool disposed;

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return disposed ? Task.CompletedTask
                : transition = ChangeAfterAsync(transition, enabled, cancellationToken);
        }
    }

    private async Task ChangeAfterAsync(Task previous, bool enabled, CancellationToken cancellationToken)
    {
        await previous;
        if (enabled && running is { IsCompleted: false })
        {
            return;
        }
        await StopAsync();
        stopping?.Dispose();
        stopping = null;
        running = null;
        brightness.ResetAudio();
        if (enabled && !cancellationToken.IsCancellationRequested)
        {
            stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = stopping.Token;
            running = Task.Run(() => CaptureAsync(token), CancellationToken.None);
        }
    }

    private async Task CaptureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var capture = createCapture();
            await capture.StartAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var samples = await capture.ReadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (samples.IsEmpty)
                {
                    throw new IOException("The playback audio source ended.");
                }
                brightness.UpdateAudio(samples.Span, (double)capture.SamplesPerChannel / capture.SampleRate);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            brightness.ResetAudio();
        }
        catch (Exception ex)
        {
            brightness.ResetAudio(ex.Message);
        }
    }

    private async Task StopAsync()
    {
        if (stopping is { } cancellation)
        {
            await cancellation.CancelAsync();
            if (running is { } task)
            {
                await task;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return new(transition);
            }
            disposed = true;
            transition = DisposeAfterAsync(transition);
            return new(transition);
        }
    }

    private async Task DisposeAfterAsync(Task previous)
    {
        await previous;
        await StopAsync();
        stopping?.Dispose();
        stopping = null;
        running = null;
        brightness.ResetAudio();
    }
}
