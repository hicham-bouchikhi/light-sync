using LightSync.Core.Configuration;

namespace LightSync.Core.Colors;

/// <summary>
/// Maps the time-based lighting modes onto a reusable frame. One instance drives every device;
/// the caller advances it once per frame and maps each device's buffer without allocating.
/// </summary>
public sealed class AmbientColorMapper
{
    /// <summary>One dark-to-bright-to-dark breath at speed 1.</summary>
    public const double PulsePeriodSeconds = 4;

    /// <summary>One full hue turn at speed 1.</summary>
    public const double RainbowPeriodSeconds = 8;

    // Any whole number of both periods keeps the accumulated phase small enough for precise
    // trigonometry during a multi-hour session.
    private const double PhaseWrapSeconds = 24;

    private LightingConfig options = new();
    private double phase;

    public AmbientColorMapper(LightingConfig options)
    {
        UpdateOptions(options);
    }

    /// <summary>The options the next frame will use.</summary>
    public LightingConfig Options => Volatile.Read(ref options);

    public void UpdateOptions(LightingConfig options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Validate().Count > 0)
        {
            throw new ArgumentException("Invalid lighting configuration.", nameof(options));
        }

        Volatile.Write(ref this.options, options with { });
    }

    /// <summary>Advances the animation by wall-clock time. Speed zero leaves the phase frozen.</summary>
    public void Advance(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        var current = Volatile.Read(ref this.options);
        phase = (phase + (seconds * current.Speed)) % PhaseWrapSeconds;
    }

    /// <summary>Fills the caller's persistent frame with the current effect.</summary>
    public void Map(Span<RgbColor> frame)
    {
        var current = Volatile.Read(ref this.options);
        switch (current.Mode)
        {
            case "pulse":
                MapPulse(current, frame);
                break;
            case "rainbow":
                MapRainbow(current, frame);
                break;
            default:
                frame.Fill(Scale(current.Color, current.Brightness));
                break;
        }
    }

    private void MapPulse(LightingConfig current, Span<RgbColor> frame)
    {
        // Speed zero is a deliberate hold at full colour, not a frozen half-bright frame.
        var level = current.Speed == 0
            ? 1.0
            : 0.5 - (0.5 * Math.Cos(2 * Math.PI * phase / PulsePeriodSeconds));
        frame.Fill(Scale(current.Color, current.Brightness * level));
    }

    private void MapRainbow(LightingConfig current, Span<RgbColor> frame)
    {
        var direction = current.Direction == "right-to-left" ? -1.0 : 1.0;
        var travel = phase / RainbowPeriodSeconds;
        for (var i = 0; i < frame.Length; i++)
        {
            var position = (double)i / frame.Length;
            frame[i] = ColorMath.Rainbow((direction * travel) + position, current.Brightness);
        }
    }

    private static RgbColor Scale(RgbColor color, double factor) => new(
        ToByte(color.R / 255.0 * factor),
        ToByte(color.G / 255.0 * factor),
        ToByte(color.B / 255.0 * factor));

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255.0, MidpointRounding.AwayFromZero), 0.0, 255.0);
}
