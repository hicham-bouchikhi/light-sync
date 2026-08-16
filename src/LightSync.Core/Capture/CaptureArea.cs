namespace LightSync.Core.Capture;

/// <summary>
/// A rectangle in native screen pixels, as reported by the desktop portal.
/// </summary>
public readonly record struct CaptureArea(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public long PixelCount => (long)Width * Height;

    /// <summary>
    /// Validates the rectangle on its own terms, ignoring any display it might sit on.
    /// </summary>
    public bool TryValidate(out string? error)
    {
        if (Width <= 0 || Height <= 0)
        {
            error = $"Capture area must have positive width and height, got {Width}x{Height}.";
            return false;
        }

        if (X < 0 || Y < 0)
        {
            error = $"Capture area origin must not be negative, got ({X}, {Y}).";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Validates the rectangle and confirms it lies entirely inside the display bounds.
    /// </summary>
    public bool TryValidateWithin(DisplayInfo display, out string? error)
    {
        if (!TryValidate(out error))
        {
            return false;
        }

        if (X < display.X || Y < display.Y || Right > display.Right || Bottom > display.Bottom)
        {
            error =
                $"Capture area {this} does not fit inside display '{display.Name}' " +
                $"({display.X},{display.Y} {display.Width}x{display.Height}).";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Converts this area into coordinates relative to the given display's origin.
    /// </summary>
    public CaptureArea ToDisplayRelative(DisplayInfo display) =>
        this with { X = X - display.X, Y = Y - display.Y };

    public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
}
