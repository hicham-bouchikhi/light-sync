using System.Text.Json.Serialization;
using LightSync.Core.Processing;

namespace LightSync.Core.Configuration;

public sealed record ProcessingConfig
{
    public const string DefaultAveraging = "luminance-weighted";

    /// <summary>Fills in values the configuration file omitted.</summary>
    public ProcessingConfig Normalized() => string.IsNullOrWhiteSpace(Averaging)
        ? this with { Averaging = DefaultAveraging }
        : this;

    [JsonPropertyName("brightness")]
    public double Brightness { get; init; } = 1.0;

    [JsonPropertyName("gamma")]
    public double Gamma { get; init; } = 1.0;

    [JsonPropertyName("saturation")]
    public double Saturation { get; init; } = 1.0;

    [JsonPropertyName("smoothing")]
    public double Smoothing { get; init; } = 0.2;

    [JsonPropertyName("blackLevel")]
    public double BlackLevel { get; init; } = 0.01;

    /// <summary>"mean", "luminance-weighted" or "colour-weighted".</summary>
    [JsonPropertyName("averaging")]
    public string Averaging { get; init; } = DefaultAveraging;

    public ColorProcessorOptions ToOptions() => new()
    {
        Brightness = Brightness,
        Gamma = Gamma,
        Saturation = Saturation,
        Smoothing = Smoothing,
        BlackLevel = BlackLevel,
        Averaging = ParsedAveraging,
    };

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (!TryParseAveraging(Averaging, out _))
        {
            problems.Add(
                $"processing.averaging must be 'mean', 'luminance-weighted' or 'colour-weighted', got '{Averaging}'.");
        }

        if (!ToOptions().TryValidate(out var error))
        {
            problems.Add("processing: " + error);
        }

        return problems;
    }

    public ZoneAveraging ParsedAveraging => TryParseAveraging(Averaging, out var mode)
        ? mode
        : ZoneAveraging.LuminanceWeighted;

    public static bool TryParseAveraging(string value, out ZoneAveraging averaging)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "mean":
            case "average":
                averaging = ZoneAveraging.Mean;
                return true;
            case "luminance-weighted":
            case "weighted":
                averaging = ZoneAveraging.LuminanceWeighted;
                return true;
            case "colour-weighted":
            case "color-weighted":
                averaging = ZoneAveraging.ColorWeighted;
                return true;
            default:
                averaging = default;
                return false;
        }
    }
}
