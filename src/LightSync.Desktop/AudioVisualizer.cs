using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LightSync.Core.Audio;
using LightSync.Core.Colors;

namespace LightSync.Desktop;

internal sealed class AudioVisualizer : Control
{
    private readonly double[] spectrum = new double[AudioAnalyzer.SpectrumBandCount];
    private readonly SolidColorBrush[] bars = CreateBrushes(AudioAnalyzer.SpectrumBandCount);
    private SolidColorBrush[] colors = [];
    private double gain = 1;

    public void Update(AudioSyncStatus status, double sensitivity)
    {
        status.Spectrum.Span.CopyTo(spectrum);
        gain = sensitivity;
        if (colors.Length != status.Colors.Length)
        {
            colors = CreateBrushes(status.Colors.Length);
        }

        for (var i = 0; i < colors.Length; i++)
        {
            var color = status.Colors.Span[i];
            colors[i].Color = Color.FromRgb(color.R, color.G, color.B);
        }

        InvalidateVisual();
    }

    public void Clear()
    {
        Array.Clear(spectrum);
        foreach (var brush in colors)
        {
            brush.Color = Colors.Black;
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var plotHeight = Math.Max(0, Bounds.Height - 30);
        var width = Bounds.Width / spectrum.Length;
        for (var i = 0; i < spectrum.Length; i++)
        {
            var height = Math.Clamp(spectrum[i] * gain, 0, 1) * plotHeight;
            bars[i].Color = Color.FromRgb((byte)(255 * (1 - (double)i / spectrum.Length)),
                (byte)(180 * Math.Sin(Math.PI * i / spectrum.Length)), (byte)(255 * i / spectrum.Length));
            context.DrawRectangle(bars[i], null,
                new Rect(i * width, plotHeight - height, Math.Max(0, width - 2), Math.Max(1, height)));
        }

        if (colors.Length > 0)
        {
            var ledWidth = Bounds.Width / colors.Length;
            for (var i = 0; i < colors.Length; i++)
            {
                context.DrawRectangle(colors[i], null, new Rect(i * ledWidth, plotHeight + 10, ledWidth, 20));
            }
        }
    }

    private static SolidColorBrush[] CreateBrushes(int count)
    {
        var result = new SolidColorBrush[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = new SolidColorBrush(Colors.Black);
        }

        return result;
    }
}
