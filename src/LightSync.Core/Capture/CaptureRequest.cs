namespace LightSync.Core.Capture;

/// <summary>
/// What the pipeline wants from the capture backend. <paramref name="TargetWidth"/> and
/// <paramref name="TargetHeight"/> let the backend downscale before the frame ever reaches
/// managed code — the processor only needs enough pixels to average per zone.
/// </summary>
public sealed record CaptureRequest(
    int DisplayId,
    CaptureArea Area,
    int Fps,
    int TargetWidth,
    int TargetHeight)
{
    public bool TryValidate(out string? error)
    {
        if (!Area.TryValidate(out error))
        {
            return false;
        }

        if (Fps is < 1 or > 240)
        {
            error = $"Fps must be between 1 and 240, got {Fps}.";
            return false;
        }

        if (TargetWidth <= 0 || TargetHeight <= 0)
        {
            error = $"Target size must be positive, got {TargetWidth}x{TargetHeight}.";
            return false;
        }

        if (TargetWidth > Area.Width || TargetHeight > Area.Height)
        {
            error =
                $"Target size {TargetWidth}x{TargetHeight} must not exceed the capture area " +
                $"{Area.Width}x{Area.Height}; upscaling adds no information.";
            return false;
        }

        error = null;
        return true;
    }
}
