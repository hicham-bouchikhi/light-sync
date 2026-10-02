using LightSync.Core.Audio;
using LightSync.Core.Capture;
using LightSync.Core.Colors;

namespace LightSync.Core.Processing;

/// <summary>Scales screen colours uniformly after colour smoothing, without changing their palette.</summary>
public sealed class ScreenBrightnessProcessor(IColorProcessor processor, ScreenBrightness brightness) : IColorProcessor
{
    public int ZoneCount => processor.ZoneCount;

    public void Process(in CapturedFrame frame, Span<RgbColor> destination)
    {
        processor.Process(frame, destination);
        var scale = brightness.ForFrame(frame.Sequence);
        for (var i = 0; i < ZoneCount; i++)
        {
            var color = destination[i];
            destination[i] = new((byte)Math.Round(color.R * scale),
                (byte)Math.Round(color.G * scale), (byte)Math.Round(color.B * scale));
        }
    }

    public void Reset() => processor.Reset();
}
