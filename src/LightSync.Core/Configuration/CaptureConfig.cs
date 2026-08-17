using System.Text.Json.Serialization;
using LightSync.Core.Capture;

namespace LightSync.Core.Configuration;

/// <summary>
/// The saved capture rectangle. Only the rectangle and target frame rate are stored —
/// screen dimensions are always discovered, never configured.
/// </summary>
public sealed record CaptureConfig
{
    [JsonPropertyName("displayId")]
    public int DisplayId { get; init; }

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("fps")]
    public int Fps { get; init; } = 30;

    /// <summary>
    /// Restore token handed back by the desktop portal, so later runs skip the picker.
    /// Absent until the first successful selection.
    /// </summary>
    [JsonPropertyName("restoreToken")]
    public string? RestoreToken { get; init; }

    [JsonIgnore]
    public CaptureArea Area => new(X, Y, Width, Height);

    public static CaptureConfig FromArea(int displayId, CaptureArea area, int fps, string? restoreToken) =>
        new()
        {
            DisplayId = displayId,
            X = area.X,
            Y = area.Y,
            Width = area.Width,
            Height = area.Height,
            Fps = fps,
            RestoreToken = restoreToken,
        };

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (DisplayId < 0)
        {
            problems.Add($"capture.displayId must not be negative, got {DisplayId}.");
        }

        // A wholly zero rectangle means "no area chosen yet", which is the state before setup
        // has run. That is not a broken configuration, so only a partially filled rectangle is
        // reported here; commands that need an area check IsConfigured and say so plainly.
        if (!IsUnset && !Area.TryValidate(out var areaError))
        {
            problems.Add("capture: " + areaError);
        }

        if (Fps is < 1 or > 240)
        {
            problems.Add($"capture.fps must be between 1 and 240, got {Fps}.");
        }

        return problems;
    }

    [JsonIgnore]
    public bool IsConfigured => Width > 0 && Height > 0;

    [JsonIgnore]
    private bool IsUnset => Width == 0 && Height == 0 && X == 0 && Y == 0;
}
