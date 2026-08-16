using System.Globalization;

namespace LightSync.Core.Colors;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Black => default;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");
}
