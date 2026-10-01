using System.Text.Json.Serialization;

namespace LightSync.Core.Configuration;

public sealed record AppConfig
{
    [JsonPropertyName("audio")]
    public AudioConfig Audio { get; init; } = new();

    [JsonPropertyName("capture")]
    public CaptureConfig Capture { get; init; } = new();

    [JsonPropertyName("mapping")]
    public MappingConfig Mapping { get; init; } = new();

    [JsonPropertyName("processing")]
    public ProcessingConfig Processing { get; init; } = new();

    [JsonPropertyName("device")]
    public DeviceConfig Device { get; init; } = new();

    /// <summary>
    /// Fills in anything the configuration file left out.
    /// </summary>
    /// <remarks>
    /// Property initializers do not run during deserialization, so an omitted section arrives as
    /// null and an omitted string as null — a hand-written partial config would otherwise throw
    /// a NullReferenceException. Defaults are applied here rather than relying on the
    /// serializer, so they hold however the object was constructed.
    /// </remarks>
    public AppConfig Normalized() => new()
    {
        Audio = (Audio ?? new AudioConfig()).Normalized(),
        Capture = (Capture ?? new CaptureConfig()).Normalized(),
        Mapping = (Mapping ?? new MappingConfig()).Normalized(),
        Processing = (Processing ?? new ProcessingConfig()).Normalized(),
        Device = (Device ?? new DeviceConfig()).Normalized(),
    };

    public IReadOnlyList<string> Validate()
    {
        var config = Normalized();

        List<string> problems = [];
        problems.AddRange(config.Audio.Validate());
        problems.AddRange(config.Capture.Validate());
        problems.AddRange(config.Mapping.Validate());
        problems.AddRange(config.Processing.Validate());
        problems.AddRange(config.Device.Validate());
        return problems;
    }
}
