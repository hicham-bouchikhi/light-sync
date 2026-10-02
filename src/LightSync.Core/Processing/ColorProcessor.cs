using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Mapping;

namespace LightSync.Core.Processing;

/// <summary>
/// Splits a frame into zone slices, combines each slice into one colour, applies the adjustment
/// chain and blends against the previous frame. Allocates only at construction.
/// </summary>
public sealed class ColorProcessor : IColorProcessor
{
    private readonly ZoneMapper mapper;
    private readonly ColorAdjustment adjustment;
    private readonly ZoneAveraging averaging;
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
        averaging = options.Averaging;
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
        var vertical = mapper.Layout == ZoneLayout.Vertical;
        var axisLength = vertical ? frame.Width : frame.Height;

        for (var slice = 0; slice < ZoneCount; slice++)
        {
            var start = SliceStart(slice, axisLength);
            var end = SliceStart(slice + 1, axisLength);

            if (end <= start)
            {
                // Fewer captured columns or rows than zones: fall back to the nearest single
                // line rather than emitting black for the empty slices.
                start = Math.Min(slice * axisLength / ZoneCount, axisLength - 1);
                end = start + 1;
            }

            sliceColors[slice] = vertical
                ? Combine(pixels, frame, start, end, 0, frame.Height)
                : Combine(pixels, frame, 0, frame.Width, start, end);
        }
    }

    /// <summary>
    /// Combines the pixels of one rectangular slice into a single colour.
    /// </summary>
    private RgbColor Combine(
        ReadOnlySpan<byte> pixels,
        in CapturedFrame frame,
        int startX,
        int endX,
        int startY,
        int endY)
    {
        ulong sumR = 0;
        ulong sumG = 0;
        ulong sumB = 0;
        ulong totalWeight = 0;
        ulong neutralR = 0;
        ulong neutralG = 0;
        ulong neutralB = 0;
        ulong neutralWeight = 0;

        for (var y = startY; y < endY; y++)
        {
            var rowStart = y * frame.Stride;

            for (var x = startX; x < endX; x++)
            {
                var offset = rowStart + (x * CapturedFrame.BytesPerPixel);
                ulong b = pixels[offset];
                ulong g = pixels[offset + 1];
                ulong r = pixels[offset + 2];

                // Weighting by luminance stops a dark background from diluting the few bright
                // pixels that actually characterise the zone. Rec. 709 coefficients, scaled to
                // integers so the inner loop stays free of floating point.
                var weight = 1UL;
                if (averaging != ZoneAveraging.Mean)
                {
                    weight = ((2126 * r) + (7152 * g) + (722 * b)) / 10000;
                    if (averaging == ZoneAveraging.ColorWeighted)
                    {
                        // Preserve neutral-only content, but let colourful pixels determine
                        // the hue when a browser's white/grey chrome shares the same zone.
                        neutralR += r * weight;
                        neutralG += g * weight;
                        neutralB += b * weight;
                        neutralWeight += weight;
                        weight = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                    }
                }

                sumR += r * weight;
                sumG += g * weight;
                sumB += b * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight == 0 && neutralWeight > 0)
        {
            sumR = neutralR;
            sumG = neutralG;
            sumB = neutralB;
            totalWeight = neutralWeight;
        }

        if (totalWeight == 0)
        {
            // Either the slice was empty, or luminance weighting found nothing but black.
            return RgbColor.Black;
        }

        return new RgbColor(
            (byte)(sumR / totalWeight),
            (byte)(sumG / totalWeight),
            (byte)(sumB / totalWeight));
    }

    /// <summary>
    /// Boundary of slice <paramref name="slice"/> along an axis of <paramref name="length"/>
    /// pixels. Multiplying before dividing spreads the remainder across slices instead of
    /// piling it onto the last one.
    /// </summary>
    private int SliceStart(int slice, int length) => (int)((long)slice * length / ZoneCount);
}
