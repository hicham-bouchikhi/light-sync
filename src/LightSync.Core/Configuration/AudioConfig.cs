using LightSync.Core.Colors;

namespace LightSync.Core.Configuration;

public sealed record AudioConfig
{
    // Setters let source-generated deserialization leave omitted values at their defaults.
    // Init-only members are supplied as constructor arguments, which would overwrite them
    // with zero; zero is also a valid explicit brightness/smoothing setting.

    public string Source { get; set; } = "@DEFAULT_MONITOR@";

    /// <summary>Rainbow, spectrum, volume, or bass. A bass pulse follows low-frequency energy.</summary>
    public string Mode { get; set; } = "spectrum";

    public double Gain { get; set; } = 3;

    public double Brightness { get; set; } = 1;

    public double Motion { get; set; } = 1;

    public double Smoothing { get; set; } = 0.65;

    public double NoiseGate { get; set; } = 0.005;

    public RgbColor Color { get; set; } = new(128, 64, 255);

    public AudioConfig Normalized() => this with
    {
        Source = string.IsNullOrWhiteSpace(Source) ? "@DEFAULT_MONITOR@" : Source,
        Mode = string.IsNullOrWhiteSpace(Mode) ? "spectrum" : Mode,
    };

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];
        if (Mode is not ("rainbow" or "spectrum" or "volume" or "bass"))
        {
            problems.Add("audio.mode must be 'rainbow', 'spectrum', 'volume', or 'bass'.");
        }

        if (!double.IsFinite(Motion) || Motion is < 0 or > 3)
        {
            problems.Add("audio.motion must be in [0, 3].");
        }

        if (!double.IsFinite(Gain) || Gain is <= 0 or > 100)
        {
            problems.Add("audio.gain must be greater than 0 and at most 100.");
        }

        if (!double.IsFinite(Brightness) || Brightness is < 0 or > 1
            || !double.IsFinite(Smoothing) || Smoothing is < 0 or >= 1
            || !double.IsFinite(NoiseGate) || NoiseGate is < 0 or > 1)
        {
            problems.Add("audio.brightness and noiseGate must be in [0, 1]; smoothing in [0, 1).");
        }

        return problems;
    }
}
