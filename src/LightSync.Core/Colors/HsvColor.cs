namespace LightSync.Core.Colors;

/// <summary>
/// Hue in degrees [0, 360), saturation and value as percentages [0, 100]. Several vendor APIs
/// accept only HSV, so the conversion lives in Core rather than in one adapter.
/// </summary>
public readonly record struct HsvColor(int Hue, int Saturation, int Value)
{
    public static HsvColor FromRgb(RgbColor color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue;
        if (delta == 0.0)
        {
            hue = 0.0;
        }
        else if (max == r)
        {
            hue = 60.0 * (((g - b) / delta) % 6.0);
        }
        else if (max == g)
        {
            hue = 60.0 * (((b - r) / delta) + 2.0);
        }
        else
        {
            hue = 60.0 * (((r - g) / delta) + 4.0);
        }

        if (hue < 0.0)
        {
            hue += 360.0;
        }

        var saturation = max == 0.0 ? 0.0 : delta / max;

        return new HsvColor(
            (int)Math.Round(hue) % 360,
            (int)Math.Round(saturation * 100.0),
            (int)Math.Round(max * 100.0));
    }
}
