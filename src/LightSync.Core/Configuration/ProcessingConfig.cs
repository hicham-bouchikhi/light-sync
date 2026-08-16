using System.Text.Json.Serialization;
using LightSync.Core.Processing;

namespace LightSync.Core.Configuration;

public sealed record ProcessingConfig
{
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

    public ColorProcessorOptions ToOptions() => new()
    {
        Brightness = Brightness,
        Gamma = Gamma,
        Saturation = Saturation,
        Smoothing = Smoothing,
        BlackLevel = BlackLevel,
    };

    public IReadOnlyList<string> Validate() =>
        ToOptions().TryValidate(out var error) ? [] : ["processing: " + error];
}
