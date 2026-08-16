namespace LightSync.Core.Capture;

/// <summary>
/// A connected display, discovered automatically. The user never types these values.
/// </summary>
public sealed record DisplayInfo(
    int Id,
    string Name,
    string Description,
    int X,
    int Y,
    int Width,
    int Height,
    double RefreshRate,
    double Scale)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public CaptureArea FullArea => new(X, Y, Width, Height);

    public override string ToString() =>
        $"[{Id}] {Name} {Width}x{Height}+{X}+{Y} @ {RefreshRate:0.##}Hz";
}
