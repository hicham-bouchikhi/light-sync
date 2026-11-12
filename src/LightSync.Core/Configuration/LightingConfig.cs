using LightSync.Core.Colors;

namespace LightSync.Core.Configuration;

/// <summary>
/// A self-running lighting effect that does not depend on audio playback or screen capture.
/// Black is a valid static colour and acts as an off switch.
/// </summary>
public sealed record LightingConfig
{
    public const string DefaultMode = "static";
    public const string DefaultDirection = "left-to-right";

    /// <summary>Static, pulse or rainbow.</summary>
    public string Mode { get; set; } = DefaultMode;

    /// <summary>Used by static and pulse; rainbow supplies its own hues.</summary>
    public RgbColor Color { get; set; } = new(128, 64, 255);

    /// <summary>Common output brightness in [0, 1].</summary>
    public double Brightness { get; set; } = 1;

    /// <summary>Pulse period and rainbow travel speed, in [0, 3]; zero freezes the effect.</summary>
    public double Speed { get; set; } = 1;

    /// <summary>Rainbow travel direction; left-to-right or right-to-left.</summary>
    public string Direction { get; set; } = DefaultDirection;

    public LightingConfig Normalized() => this with
    {
        Mode = string.IsNullOrWhiteSpace(Mode) ? DefaultMode : Mode,
        Direction = string.IsNullOrWhiteSpace(Direction) ? DefaultDirection : Direction,
    };

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];
        if (Mode is not ("static" or "pulse" or "rainbow"))
        {
            problems.Add("lighting.mode must be 'static', 'pulse' or 'rainbow'.");
        }

        if (Direction is not ("left-to-right" or "right-to-left"))
        {
            problems.Add("lighting.direction must be 'left-to-right' or 'right-to-left'.");
        }

        if (!double.IsFinite(Brightness) || Brightness is < 0 or > 1)
        {
            problems.Add("lighting.brightness must be in [0, 1].");
        }

        if (!double.IsFinite(Speed) || Speed is < 0 or > 3)
        {
            problems.Add("lighting.speed must be in [0, 3].");
        }

        return problems;
    }
}
