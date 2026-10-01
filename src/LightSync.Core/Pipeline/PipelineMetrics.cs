using System.Diagnostics;

namespace LightSync.Core.Pipeline;

/// <summary>
/// Running counters for the pipeline. Updated from the stage threads with interlocked
/// operations and read by whatever is reporting, so no lock sits in the frame path.
/// </summary>
public sealed class PipelineMetrics
{
    private readonly long startedAt = Stopwatch.GetTimestamp();
    private long capturedFrames;
    private long processedFrames;
    private long sentFrames;
    private long droppedFrames;
    private long deviceErrors;
    private string? lastDeviceError;
    private long totalLatencyTicks;
    private long windowStartTicks;
    private long windowFrames;
    private double currentFps;

    public long CapturedFrames => Interlocked.Read(ref capturedFrames);

    public long ProcessedFrames => Interlocked.Read(ref processedFrames);

    public long SentFrames => Interlocked.Read(ref sentFrames);

    public long DroppedFrames => Interlocked.Read(ref droppedFrames);

    public long DeviceErrors => Interlocked.Read(ref deviceErrors);

    public string? LastDeviceError => Volatile.Read(ref lastDeviceError);

    public TimeSpan Uptime => Stopwatch.GetElapsedTime(startedAt);

    /// <summary>Frames per second over the most recent measurement window.</summary>
    public double Fps => Volatile.Read(ref currentFps);

    /// <summary>Mean capture-to-device latency across the whole run.</summary>
    public TimeSpan AverageLatency
    {
        get
        {
            var frames = Interlocked.Read(ref sentFrames);
            return frames == 0
                ? TimeSpan.Zero
                : TimeSpan.FromTicks(Interlocked.Read(ref totalLatencyTicks) / frames);
        }
    }

    public void RecordCaptured() => Interlocked.Increment(ref capturedFrames);

    public void RecordProcessed() => Interlocked.Increment(ref processedFrames);

    public void RecordDropped() => Interlocked.Increment(ref droppedFrames);

    public void RecordDeviceError(string? message = null)
    {
        if (message is not null)
        {
            Volatile.Write(ref lastDeviceError, message);
        }

        Interlocked.Increment(ref deviceErrors);
    }

    public void RecordSent(TimeSpan latency)
    {
        Interlocked.Increment(ref sentFrames);
        Interlocked.Add(ref totalLatencyTicks, latency.Ticks);
        UpdateFpsWindow();
    }

    private void UpdateFpsWindow()
    {
        var frames = Interlocked.Increment(ref windowFrames);
        var now = Stopwatch.GetTimestamp();
        var start = Interlocked.Read(ref windowStartTicks);

        if (start == 0)
        {
            Interlocked.CompareExchange(ref windowStartTicks, now, 0);
            return;
        }

        // Recompute roughly once a second so the reported rate is current without the
        // whole-run average smearing away real changes. Stopwatch timestamps are in
        // Stopwatch.Frequency units, not TimeSpan ticks, so convert rather than assume.
        var elapsed = Stopwatch.GetElapsedTime(start, now);
        if (elapsed < TimeSpan.FromSeconds(1))
        {
            return;
        }

        Volatile.Write(ref currentFps, frames / elapsed.TotalSeconds);
        Interlocked.Exchange(ref windowFrames, 0);
        Interlocked.Exchange(ref windowStartTicks, now);
    }

    public override string ToString() =>
        $"{Fps:0.0} fps | latency {AverageLatency.TotalMilliseconds:0.0} ms | " +
        $"sent {SentFrames} | dropped {DroppedFrames}" +
        (DeviceErrors > 0 ? $" | device errors {DeviceErrors}" : string.Empty);
}
