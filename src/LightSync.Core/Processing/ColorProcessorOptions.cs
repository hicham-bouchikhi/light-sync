namespace LightSync.Core.Processing;

public sealed record ColorProcessorOptions
{
    public double Brightness { get; init; } = 1.0;

    public double Gamma { get; init; } = 1.0;

    public double Saturation { get; init; } = 1.0;

    /// <summary>
    /// Weight of the previous frame when blending, in [0, 1). 0 disables smoothing.
    /// </summary>
    public double Smoothing { get; init; } = 0.2;

    /// <summary>
    /// Normalised luminance below which a zone is forced to black, in [0, 1].
    /// </summary>
    public double BlackLevel { get; init; } = 0.01;

    /// <summary>
    /// How pixels within a zone are combined. Luminance weighting is the default because a zone
    /// covering a large, mostly dark area averages to a washed-out mid-tone otherwise.
    /// </summary>
    public ZoneAveraging Averaging { get; init; } = ZoneAveraging.LuminanceWeighted;

    public bool TryValidate(out string? error)
    {
        if (Brightness is < 0 or > 4)
        {
            error = $"Brightness must be between 0 and 4, got {Brightness}.";
            return false;
        }

        if (Gamma is <= 0 or > 5)
        {
            error = $"Gamma must be greater than 0 and at most 5, got {Gamma}.";
            return false;
        }

        if (Saturation is < 0 or > 4)
        {
            error = $"Saturation must be between 0 and 4, got {Saturation}.";
            return false;
        }

        if (Smoothing is < 0 or >= 1)
        {
            error = $"Smoothing must be in [0, 1), got {Smoothing}.";
            return false;
        }

        if (BlackLevel is < 0 or > 1)
        {
            error = $"BlackLevel must be in [0, 1], got {BlackLevel}.";
            return false;
        }

        error = null;
        return true;
    }
}
