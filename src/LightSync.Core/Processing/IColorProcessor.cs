using LightSync.Core.Capture;
using LightSync.Core.Colors;

namespace LightSync.Core.Processing;

/// <summary>
/// Reduces a frame to one colour per zone. Implementations must not allocate: the caller
/// supplies the destination span and the processor reuses its own internal state.
/// </summary>
public interface IColorProcessor
{
    int ZoneCount { get; }

    void Process(in CapturedFrame frame, Span<RgbColor> destination);

    /// <summary>Clears smoothing state so the next frame is used verbatim.</summary>
    void Reset();
}
