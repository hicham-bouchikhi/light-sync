using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Mapping;

namespace LightSync.Core.Processing;

/// <summary>
/// Splits a frame into zone slices, averages each slice, applies the adjustment chain and
/// blends against the previous frame. Allocates only at construction.
/// </summary>
public sealed class ColorProcessor : IColorProcessor
{
    private readonly ZoneMapper mapper;
    private readonly ColorAdjustment adjustment;
    private readonly double smoothing;
    private readonly RgbColor[] sliceColors;
    private readonly RgbColor[] previous;
    private bool hasPreviousFrame;

    public ColorProcessor(ZoneMapper mapper, ColorProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(options));
        }

        this.mapper = mapper;
        smoothing = options.Smoothing;
        adjustment = new ColorAdjustment(
            options.Brightness,
            options.Gamma,
            options.Saturation,
            options.BlackLevel);

        sliceColors = new RgbColor[mapper.ZoneCount];
        previous = new RgbColor[mapper.ZoneCount];
    }

    public int ZoneCount => mapper.ZoneCount;

    public void Process(in CapturedFrame frame, Span<RgbColor> destination)
    {
        if (destination.Length < ZoneCount)
        {
            throw new ArgumentException(
                $"Destination holds {destination.Length} colours but {ZoneCount} zones are configured.",
                nameof(destination));
        }

        if (frame.IsEmpty)
        {
            destination[..ZoneCount].Clear();
            return;
        }

        AverageSlices(frame);

        for (var output = 0; output < ZoneCount; output++)
        {
            var adjusted = ColorMath.Adjust(sliceColors[mapper.SliceForOutput(output)], adjustment);

            if (hasPreviousFrame && smoothing > 0.0)
            {
                adjusted = ColorMath.Smooth(previous[output], adjusted, smoothing);
            }

            previous[output] = adjusted;
            destination[output] = adjusted;
        }

        hasPreviousFrame = true;
    }

    public void Reset()
    {
        hasPreviousFrame = false;
        Array.Clear(previous);
    }

    private void AverageSlices(in CapturedFrame frame)
    {
        var pixels = frame.Pixels.Span;

        if (mapper.Layout == ZoneLayout.Vertical)
        {
            AverageColumns(pixels, frame);
        }
        else
        {
            AverageRows(pixels, frame);
        }
    }

    private void AverageColumns(ReadOnlySpan<byte> pixels, in CapturedFrame frame)
    {
        for (var slice = 0; slice < ZoneCount; slice++)
        {
            var startX = SliceStart(slice, frame.Width);
            var endX = SliceStart(slice + 1, frame.Width);

            if (endX <= startX)
            {
                // Fewer captured columns than zones: fall back to the nearest single column.
                startX = Math.Min(slice * frame.Width / ZoneCount, frame.Width - 1);
                endX = startX + 1;
            }

            uint sumB = 0;
            uint sumG = 0;
            uint sumR = 0;
            uint count = 0;

            for (var y = 0; y < frame.Height; y++)
            {
                var rowStart = y * frame.Stride;
                for (var x = startX; x < endX; x++)
                {
                    var offset = rowStart + (x * CapturedFrame.BytesPerPixel);
                    sumB += pixels[offset];
                    sumG += pixels[offset + 1];
                    sumR += pixels[offset + 2];
                    count++;
                }
            }

            sliceColors[slice] = count == 0
                ? RgbColor.Black
                : new RgbColor((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
        }
    }

    private void AverageRows(ReadOnlySpan<byte> pixels, in CapturedFrame frame)
    {
        for (var slice = 0; slice < ZoneCount; slice++)
        {
            var startY = SliceStart(slice, frame.Height);
            var endY = SliceStart(slice + 1, frame.Height);

            if (endY <= startY)
            {
                startY = Math.Min(slice * frame.Height / ZoneCount, frame.Height - 1);
                endY = startY + 1;
            }

            var rowBytes = frame.Width * CapturedFrame.BytesPerPixel;
            uint sumB = 0;
            uint sumG = 0;
            uint sumR = 0;
            uint count = 0;

            for (var y = startY; y < endY; y++)
            {
                var row = pixels.Slice(y * frame.Stride, rowBytes);
                for (var offset = 0; offset + 4 <= row.Length; offset += 4)
                {
                    sumB += row[offset];
                    sumG += row[offset + 1];
                    sumR += row[offset + 2];
                    count++;
                }
            }

            sliceColors[slice] = count == 0
                ? RgbColor.Black
                : new RgbColor((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
        }
    }

    /// <summary>
    /// Boundary of slice <paramref name="slice"/> along an axis of <paramref name="length"/>
    /// pixels. Multiplying before dividing spreads the remainder across slices instead of
    /// piling it onto the last one.
    /// </summary>
    private int SliceStart(int slice, int length) => (int)((long)slice * length / ZoneCount);
}
