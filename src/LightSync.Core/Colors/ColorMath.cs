namespace LightSync.Core.Colors;

public static class ColorMath
{
    /// <summary>Rec. 709 luma coefficients.</summary>
    private const double LumaR = 0.2126;
    private const double LumaG = 0.7152;
    private const double LumaB = 0.0722;

    /// <summary>
    /// Relative luminance of a colour, in [0, 1].
    /// </summary>
    public static double Luminance(RgbColor color) =>
        ((LumaR * color.R) + (LumaG * color.G) + (LumaB * color.B)) / 255.0;

    /// <summary>
    /// Applies gamma, saturation, brightness and the black-level cut, in that order.
    /// Gamma is applied first because it is a property of the display signal, whereas
    /// saturation and brightness are user taste applied on top of a linearised value.
    /// </summary>
    public static RgbColor Adjust(RgbColor color, in ColorAdjustment adjustment)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        if (adjustment.Gamma != 1.0)
        {
            r = Math.Pow(r, adjustment.Gamma);
            g = Math.Pow(g, adjustment.Gamma);
            b = Math.Pow(b, adjustment.Gamma);
        }

        if (adjustment.Saturation != 1.0)
        {
            // Interpolate away from (or past) the luma-preserving grey.
            var grey = (LumaR * r) + (LumaG * g) + (LumaB * b);
            r = grey + ((r - grey) * adjustment.Saturation);
            g = grey + ((g - grey) * adjustment.Saturation);
            b = grey + ((b - grey) * adjustment.Saturation);
        }

        if (adjustment.Brightness != 1.0)
        {
            r *= adjustment.Brightness;
            g *= adjustment.Brightness;
            b *= adjustment.Brightness;
        }

        r = Math.Clamp(r, 0.0, 1.0);
        g = Math.Clamp(g, 0.0, 1.0);
        b = Math.Clamp(b, 0.0, 1.0);

        if (adjustment.BlackLevel > 0.0
            && (LumaR * r) + (LumaG * g) + (LumaB * b) < adjustment.BlackLevel)
        {
            return RgbColor.Black;
        }

        return new RgbColor(ToByte(r), ToByte(g), ToByte(b));
    }

    /// <summary>
    /// Blends towards <paramref name="target"/>, keeping <paramref name="previousWeight"/> of
    /// the previous value. A weight of 0 returns the target unchanged.
    /// </summary>
    public static RgbColor Smooth(RgbColor previous, RgbColor target, double previousWeight)
    {
        if (previousWeight <= 0.0)
        {
            return target;
        }

        var targetWeight = 1.0 - previousWeight;
        return new RgbColor(
            SmoothChannel(previous.R, target.R, previousWeight, targetWeight),
            SmoothChannel(previous.G, target.G, previousWeight, targetWeight),
            SmoothChannel(previous.B, target.B, previousWeight, targetWeight));
    }

    private static byte SmoothChannel(byte previous, byte target, double previousWeight, double targetWeight)
    {
        var blended = ToByte(((previous * previousWeight) + (target * targetWeight)) / 255.0);

        // Rounding can leave the blend equal to where it started, which strands the channel
        // one step short of its target forever — a black screen would keep the lamp faintly
        // lit. Force a single step towards the target whenever that happens.
        if (blended == previous && previous != target)
        {
            return target > previous ? (byte)(previous + 1) : (byte)(previous - 1);
        }

        return blended;
    }

    /// <summary>
    /// Averages a run of BGRx pixels. The capture backend downscales before frames reach
    /// managed code, so this only ever walks a few hundred pixels per zone.
    /// </summary>
    public static RgbColor AverageBgrx(ReadOnlySpan<byte> pixels)
    {
        var pixelCount = pixels.Length / 4;
        if (pixelCount == 0)
        {
            return RgbColor.Black;
        }

        uint sumB = 0;
        uint sumG = 0;
        uint sumR = 0;

        for (var index = 0; index + 4 <= pixels.Length; index += 4)
        {
            sumB += pixels[index];
            sumG += pixels[index + 1];
            sumR += pixels[index + 2];
        }

        var count = (uint)pixelCount;
        return new RgbColor(
            (byte)(sumR / count),
            (byte)(sumG / count),
            (byte)(sumB / count));
    }

    private static byte ToByte(double normalised) =>
        (byte)Math.Clamp(Math.Round(normalised * 255.0, MidpointRounding.AwayFromZero), 0.0, 255.0);
}

/// <summary>
/// The per-frame adjustment parameters, flattened into a struct so the frame loop reads them
/// without touching a reference type.
/// </summary>
public readonly record struct ColorAdjustment(
    double Brightness,
    double Gamma,
    double Saturation,
    double BlackLevel);
