using System.Diagnostics;
using LightSync.Core.Audio;
using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Mapping;
using LightSync.Core.Processing;

namespace LightSync.Desktop.Screen;

/// <summary>
/// Keeps one bounded preview snapshot. The UI samples it at 10 Hz; it never queues
/// borrowed capture buffers or holds up the device pipeline waiting for the UI.
/// </summary>
internal sealed class ScreenPreviewProcessor(ZoneMapper mapper, ColorProcessorOptions options,
    ScreenBrightness brightness) : IColorProcessor
{
    private readonly ScreenBrightnessProcessor processor = new(new ColorProcessor(mapper,
        options with { Brightness = 1 }), brightness);
    private readonly Lock gate = new();
    private readonly RgbColor[] colors = new RgbColor[mapper.ZoneCount];
    private byte[] pixels = [];
    private CapturedFrame snapshot;
    private long lastSnapshot;
    private long displayedSequence = -1;

    public int ZoneCount => processor.ZoneCount;

    public void Process(in CapturedFrame frame, Span<RgbColor> destination)
    {
        processor.Process(frame, destination);
        if (frame.IsEmpty || (lastSnapshot != 0 && Stopwatch.GetElapsedTime(lastSnapshot).TotalMilliseconds < 100))
        {
            return;
        }

        lock (gate)
        {
            var length = frame.Stride * frame.Height;
            if (pixels.Length != length)
            {
                pixels = new byte[length];
            }

            frame.Pixels.Span[..length].CopyTo(pixels);
            destination[..ZoneCount].CopyTo(colors);
            snapshot = frame with { Pixels = pixels };
            lastSnapshot = Stopwatch.GetTimestamp();
        }
    }

    public bool UpdatePreview(ScreenVisualizer visualizer)
    {
        lock (gate)
        {
            if (snapshot.IsEmpty || displayedSequence == snapshot.Sequence)
            {
                return false;
            }

            visualizer.Update(snapshot, colors, mapper);
            displayedSequence = snapshot.Sequence;
            return true;
        }
    }

    public void Reset() => processor.Reset();
}
