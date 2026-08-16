namespace LightSync.Core.Colors;

public static class ColorConstants
{
    public static RgbColor Red => new(255, 0, 0);

    public static RgbColor Green => new(0, 255, 0);

    public static RgbColor Blue => new(0, 0, 255);

    public static RgbColor White => new(255, 255, 255);

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
