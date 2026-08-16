using LightSync.Core.Colors;

namespace LightSync.Core.Capture;

/// <summary>
/// Generates a moving test pattern at the requested frame rate. Lets the pipeline, the
/// dry-run output and the device adapters be exercised without a portal session.
/// </summary>
public sealed class SyntheticScreenCapture(TimeProvider? timeProvider = null) : IScreenCapture
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private byte[] buffer = [];
    private int width;
    private int height;
    private long sequence;
    private TimeSpan frameInterval;
    private long startedAt;
    private bool started;

    /// <summary>Stops after this many frames when set, so tests terminate.</summary>
    public long? FrameLimit { get; init; }

    public Task<CapturedFrame> StartAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(request));
        }

        width = request.TargetWidth;
        height = request.TargetHeight;
        buffer = new byte[width * height * CapturedFrame.BytesPerPixel];
        frameInterval = TimeSpan.FromSeconds(1.0 / request.Fps);
        startedAt = time.GetTimestamp();
        sequence = 0;
        started = true;

        return Task.FromResult(Render());
    }

    public async Task<CapturedFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        if (!started)
        {
            throw new InvalidOperationException("StartAsync must be called before ReadFrameAsync.");
        }

        if (FrameLimit is { } limit && sequence >= limit)
        {
            return default;
        }

        await Task.Delay(frameInterval, time, cancellationToken);
        return Render();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        started = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        started = false;
        return ValueTask.CompletedTask;
    }

    private CapturedFrame Render()
    {
        sequence++;
        var elapsed = time.GetElapsedTime(startedAt);

        // A hue sweep that travels across the frame, so every zone sees a different colour
        // and the whole strip visibly animates.
        var phase = elapsed.TotalSeconds * 0.25;

        for (var x = 0; x < width; x++)
        {
            var hue = ((x / (double)Math.Max(width, 1)) + phase) % 1.0;
            var color = FromHue(hue);

            for (var y = 0; y < height; y++)
            {
                var offset = (((y * width) + x) * CapturedFrame.BytesPerPixel);
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = 255;
            }
        }

        return new CapturedFrame(buffer, width, height, sequence, elapsed);
    }

    /// <summary>Fully saturated, full-value HSV to RGB.</summary>
    private static RgbColor FromHue(double hue)
    {
        var sector = hue * 6.0;
        var offset = sector - Math.Floor(sector);
        var rising = (byte)(offset * 255);
        var falling = (byte)(255 - rising);

        return (int)sector switch
        {
            0 => new RgbColor(255, rising, 0),
            1 => new RgbColor(falling, 255, 0),
            2 => new RgbColor(0, 255, rising),
            3 => new RgbColor(0, falling, 255),
            4 => new RgbColor(rising, 0, 255),
            _ => new RgbColor(255, 0, falling),
        };
    }
}
