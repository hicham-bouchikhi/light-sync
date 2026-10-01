using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Mapping;

namespace LightSync.Desktop.Screen;

internal sealed class ScreenVisualizer : Control, IDisposable
{
    private readonly Pen zonePen = new(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1);
    private readonly Pen selectedPen = new(Brushes.White, 2);
    private readonly IBrush background = new SolidColorBrush(Color.Parse("#0B111C"));
    private WriteableBitmap? bitmap;
    private byte[] pixels = [];
    private RgbColor[] colors = [];
    private SolidColorBrush[] brushes = [];
    private FormattedText[] labels = [];
    private double maximumLabelWidth;
    private ZoneMapper? mapper;
    private Rect imageBounds;
    private Rect stripBounds;
    private bool showZones = true;

    public event EventHandler? SelectionChanged;

    public int SelectedOutput { get; private set; }

    public string SelectionText => colors.Length == 0 ? "Click a zone to inspect its output colour."
        : $"LED {SelectedOutput + 1} ← slice {mapper!.SliceForOutput(SelectedOutput) + 1} · {colors[SelectedOutput]} · RGB {colors[SelectedOutput].R}, {colors[SelectedOutput].G}, {colors[SelectedOutput].B}";

    public void ShowZones(bool value)
    {
        showZones = value;
        InvalidateVisual();
    }

    public void Update(in CapturedFrame frame, ReadOnlySpan<RgbColor> output, ZoneMapper zoneMapper)
    {
        if (bitmap is null || bitmap.PixelSize.Width != frame.Width || bitmap.PixelSize.Height != frame.Height)
        {
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            pixels = new byte[frame.Stride * frame.Height];
        }

        frame.Pixels.Span[..pixels.Length].CopyTo(pixels);
        // Capture is BGRx: the fourth byte is padding, not an alpha channel.
        for (var offset = 3; offset < pixels.Length; offset += CapturedFrame.BytesPerPixel)
        {
            pixels[offset] = 255;
        }

        using (var framebuffer = bitmap.Lock())
        {
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(pixels, y * frame.Stride, framebuffer.Address + (y * framebuffer.RowBytes), frame.Stride);
            }
        }

        mapper = zoneMapper;
        if (colors.Length != output.Length)
        {
            colors = new RgbColor[output.Length];
            brushes = new SolidColorBrush[output.Length];
            labels = new FormattedText[output.Length];
            maximumLabelWidth = 0;
            SelectedOutput = 0;
            for (var i = 0; i < output.Length; i++)
            {
                brushes[i] = new SolidColorBrush(Colors.Black);
                labels[i] = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
                maximumLabelWidth = Math.Max(maximumLabelWidth, labels[i].Width);
            }
        }

        output.CopyTo(colors);
        for (var i = 0; i < colors.Length; i++)
        {
            brushes[i].Color = Color.FromRgb(colors[i].R, colors[i].G, colors[i].B);
        }

        InvalidateVisual();
    }

    public void Clear()
    {
        bitmap?.Dispose();
        bitmap = null;
        colors = [];
        mapper = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(background, null, new Rect(Bounds.Size), 8, 8);
        var availableHeight = Math.Max(0, Bounds.Height - 54);
        if (bitmap is null || mapper is null || availableHeight == 0)
        {
            imageBounds = default;
            stripBounds = default;
            return;
        }

        var scale = Math.Min(Bounds.Width / bitmap.PixelSize.Width, availableHeight / bitmap.PixelSize.Height);
        var width = bitmap.PixelSize.Width * scale;
        var height = bitmap.PixelSize.Height * scale;
        imageBounds = new Rect((Bounds.Width - width) / 2, (availableHeight - height) / 2, width, height);
        context.DrawImage(bitmap, new Rect(bitmap.Size), imageBounds);
        if (showZones)
        {
            for (var i = 0; i < colors.Length; i++)
            {
                var zone = ZoneBounds(i);
                context.DrawRectangle(null, i == SelectedOutput ? selectedPen : zonePen, zone);
            }

            DrawZoneLabels(context);
        }

        stripBounds = new Rect(0, Bounds.Height - 34, Bounds.Width, 30);
        var ledWidth = stripBounds.Width / colors.Length;
        for (var i = 0; i < colors.Length; i++)
        {
            var led = new Rect(i * ledWidth, stripBounds.Y, ledWidth, stripBounds.Height);
            context.DrawRectangle(brushes[i], i == SelectedOutput ? selectedPen : null, led);
        }
    }

    private void DrawZoneLabels(DrawingContext context)
    {
        const double badgeHeight = 19;
        const double gap = 3;
        var badgeWidth = maximumLabelWidth + 6;
        var vertical = mapper!.Layout == ZoneLayout.Vertical;
        var zoneSize = (vertical ? imageBounds.Width : imageBounds.Height) / colors.Length;
        var labelSize = vertical ? badgeWidth : badgeHeight;
        // Use one common width for all numbers. Narrow columns alternate label rows;
        // narrow rows alternate label columns, rather than dropping two-digit labels.
        var requiredLanes = zoneSize >= labelSize + gap ? 1
            : Math.Max(1, (int)Math.Ceiling((labelSize + gap) / Math.Max(zoneSize, 0.01)));
        var availableLanes = Math.Max(1, (int)((vertical ? imageBounds.Height : imageBounds.Width)
            / ((vertical ? badgeHeight : badgeWidth) + gap)));
        var step = Math.Max(1, (int)Math.Ceiling((double)requiredLanes / availableLanes));
        var lanes = Math.Min(requiredLanes, availableLanes);

        for (var i = 0; i < colors.Length; i++)
        {
            var slice = mapper.SliceForOutput(i);
            if (slice % step != 0 && slice != colors.Length - 1 && i != SelectedOutput)
            {
                continue;
            }

            var zone = ZoneBounds(i);
            var lane = (slice / step) % lanes;
            var x = vertical ? zone.X + ((zone.Width - badgeWidth) / 2)
                : imageBounds.X + gap + (lane * (badgeWidth + gap));
            var y = vertical ? imageBounds.Y + gap + (lane * (badgeHeight + gap))
                : zone.Y + ((zone.Height - badgeHeight) / 2);
            // Badges can extend into letterboxing while remaining inside the control.
            x = Math.Clamp(x, 0, Math.Max(0, Bounds.Width - badgeWidth));
            y = Math.Clamp(y, 0, Math.Max(0, Bounds.Height - 54 - badgeHeight));
            var badge = new Rect(x, y, badgeWidth, badgeHeight);
            context.DrawRectangle(background, i == SelectedOutput ? selectedPen : null, badge, 3, 3);
            context.DrawText(labels[i], new Point(badge.X + ((badgeWidth - labels[i].Width) / 2), badge.Y + 2));
        }
    }

    private Rect ZoneBounds(int output)
    {
        var slice = mapper!.SliceForOutput(output);
        // Match ColorProcessor's integer pixel boundaries, including its nearest-line
        // fallback when there are more LEDs than captured columns or rows.
        var vertical = mapper.Layout == ZoneLayout.Vertical;
        var length = vertical ? bitmap!.PixelSize.Width : bitmap!.PixelSize.Height;
        var start = (int)((long)slice * length / colors.Length);
        var end = (int)((long)(slice + 1) * length / colors.Length);
        if (end <= start)
        {
            start = Math.Min(start, length - 1);
            end = start + 1;
        }

        return vertical
            ? new Rect(imageBounds.X + (imageBounds.Width * start / length), imageBounds.Y,
                imageBounds.Width * (end - start) / length, imageBounds.Height)
            : new Rect(imageBounds.X, imageBounds.Y + (imageBounds.Height * start / length),
                imageBounds.Width, imageBounds.Height * (end - start) / length);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (mapper is null || colors.Length == 0)
        {
            return;
        }

        var point = e.GetPosition(this);
        var output = -1;
        if (stripBounds.Contains(point))
        {
            output = Math.Min(colors.Length - 1, (int)(point.X / stripBounds.Width * colors.Length));
        }
        else if (imageBounds.Contains(point))
        {
            for (var i = 0; i < colors.Length; i++)
            {
                if (ZoneBounds(i).Contains(point))
                {
                    output = i;
                    break;
                }
            }
        }

        if (output >= 0)
        {
            SelectedOutput = output;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        bitmap?.Dispose();
        bitmap = null;
        GC.SuppressFinalize(this);
    }
}
