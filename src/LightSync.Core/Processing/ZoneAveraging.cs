namespace LightSync.Core.Processing;

/// <summary>How the pixels in a zone are combined into one colour.</summary>
public enum ZoneAveraging
{
    /// <summary>
    /// Plain arithmetic mean. Faithful, but a large mostly-dark region averages to a dim
    /// mid-tone, which reads as washed out on a lamp.
    /// </summary>
    Mean,

    /// <summary>
    /// Mean weighted by each pixel's luminance, so bright pixels dominate and dark background
    /// dilutes the result far less. Usually the better choice when a zone covers a lot of screen.
    /// </summary>
    LuminanceWeighted,

    /// <summary>
    /// Mean weighted by channel chroma, reducing the influence of white and grey backgrounds.
    /// Entirely neutral zones fall back to luminance weighting.
    /// </summary>
    ColorWeighted,
}
