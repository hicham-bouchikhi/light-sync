using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LightSync.Core.Colors;

namespace LightSync.Desktop;

/// <summary>Draws the requested LED frame for the self-running lighting modes.</summary>
internal sealed class LightingVisualizer : Control
{
    private SolidColorBrush[] colors = [];

    public void Update(ReadOnlySpan<RgbColor> frame)
    {
        if (colors.Length != frame.Length)
        {
            colors = CreateBrushes(frame.Length);
        }

        for (var i = 0; i < colors.Length; i++)
        {
            var color = frame[i];
            colors[i].Color = Color.FromRgb(color.R, color.G, color.B);
        }

        InvalidateVisual();
    }

    public void Clear()
    {
        foreach (var brush in colors)
        {
            brush.Color = Colors.Black;
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (colors.Length == 0)
        {
            return;
        }

        var width = Bounds.Width / colors.Length;
        var height = Math.Max(1, Bounds.Height - 24);
        for (var i = 0; i < colors.Length; i++)
        {
            context.DrawRectangle(colors[i], null,
                new Rect(i * width, (Bounds.Height - height) / 2, Math.Max(0, width - 2), height));
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
