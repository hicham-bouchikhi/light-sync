using System.Text.Json.Serialization;

namespace LightSync.Core.Configuration;

public sealed record AppConfig
{
    [JsonPropertyName("capture")]
    public CaptureConfig Capture { get; init; } = new();

    [JsonPropertyName("mapping")]
    public MappingConfig Mapping { get; init; } = new();

    [JsonPropertyName("processing")]
    public ProcessingConfig Processing { get; init; } = new();

    [JsonPropertyName("device")]
    public DeviceConfig Device { get; init; } = new();

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];
        problems.AddRange(Capture.Validate());
        problems.AddRange(Mapping.Validate());
        problems.AddRange(Processing.Validate());
        problems.AddRange(Device.Validate());
        return problems;
    }
}
