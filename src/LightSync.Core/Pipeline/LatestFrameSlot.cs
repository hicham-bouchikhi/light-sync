using System.Collections.Concurrent;
using LightSync.Core.Colors;

namespace LightSync.Core.Pipeline;

/// <summary>
/// A one-deep handoff between the processing and output stages that keeps only the newest
/// frame. Publishing over an unconsumed frame counts as a drop and recycles its buffer, so
/// the pipeline never queues stale frames and never allocates once running.
/// </summary>
public sealed class LatestFrameSlot(int zoneCount) : IDisposable
{
    private readonly ConcurrentStack<ZoneFrame> free = new();
    private readonly SemaphoreSlim available = new(0, 1);
    private ZoneFrame? pending;
    private bool completed;

    /// <summary>Takes a buffer to write the next frame into.</summary>
    public ZoneFrame Rent() => free.TryPop(out var frame) ? frame : new ZoneFrame(zoneCount);

    /// <summary>
    /// Publishes <paramref name="frame"/>, returning true if it displaced an unconsumed frame
    /// — that is, if a frame was dropped.
    /// </summary>
    public bool Publish(ZoneFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var displaced = Interlocked.Exchange(ref pending, frame);

        if (displaced is not null)
        {
            free.Push(displaced);
            return true;
        }

        // Only signal when the slot went from empty to full, so the count stays within the
        // semaphore's maximum of one.
        available.Release();
        return false;
    }

    /// <summary>
    /// Waits for a frame. Returns null once <see cref="Complete"/> has been called and no
    /// frame remains.
    /// </summary>
    public async Task<ZoneFrame?> TakeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (Volatile.Read(ref completed) && Volatile.Read(ref pending) is null)
            {
                return null;
            }

            await available.WaitAsync(cancellationToken);

            var frame = Interlocked.Exchange(ref pending, null);
            if (frame is not null)
            {
                return frame;
            }

            if (Volatile.Read(ref completed))
            {
                return null;
            }
        }
    }

    /// <summary>Hands a consumed buffer back for reuse.</summary>
    public void Return(ZoneFrame frame) => free.Push(frame);

    public void Complete()
    {
        Volatile.Write(ref completed, true);

        // Wake a waiting consumer so it can observe completion.
        try
        {
            available.Release();
        }
        catch (SemaphoreFullException)
        {
            // A frame is already pending; the consumer will wake on its own.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose() => available.Dispose();
}

/// <summary>A reusable buffer of zone colours plus the capture timestamp it came from.</summary>
public sealed class ZoneFrame(int zoneCount)
{
    public RgbColor[] Colors { get; } = new RgbColor[zoneCount];

    public long CapturedAt { get; set; }
}
