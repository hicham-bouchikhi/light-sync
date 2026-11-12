using System.Globalization;

namespace LightSync.Core.Colors;

public static class ColorConstants
{
    public static RgbColor Red => new(255, 0, 0);

    public static RgbColor Green => new(0, 255, 0);

    public static RgbColor Blue => new(0, 0, 255);

    public static RgbColor White => new(255, 255, 255);

    /// <summary>
    /// Parses a named colour, <c>#RRGGBB</c> (with or without the hash) or <c>R,G,B</c> channels.
    /// Returns false rather than throwing so frontends can report the problem their own way.
    /// </summary>
    public static bool TryParse(string? value, out RgbColor color)
    {
        if (value is null)
        {
            color = default;
            return false;
        }

        if (TryParseNamed(value, out color))
        {
            return true;
        }

        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        var channels = value.Split(',');
        if (channels.Length == 3 && byte.TryParse(channels[0], out var red)
            && byte.TryParse(channels[1], out var green) && byte.TryParse(channels[2], out var blue))
        {
            color = new(red, green, blue);
            return true;
        }

        color = default;
        return false;
    }

    public static bool TryParseNamed(string name, out RgbColor color)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "red":
                color = Red;
                return true;
            case "green":
                color = Green;
                return true;
            case "blue":
                color = Blue;
                return true;
            case "white":
                color = White;
                return true;
            case "black":
                color = RgbColor.Black;
                return true;
            default:
                color = default;
                return false;
        }
    }

    public static IReadOnlyList<string> NamedColors { get; } =
        ["red", "green", "blue", "white", "black"];
}
