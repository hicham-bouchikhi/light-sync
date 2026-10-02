using System.Text.Json.Serialization;

namespace LightSync.Core.Configuration;

/// <summary>Desktop screen brightness and optional playback intensity, independent of audio colour sync.</summary>
public sealed record ScreenConfig
{
    public const string DefaultAudioSource = "@DEFAULT_MONITOR@";

    [JsonPropertyName("brightness")]
    public double Brightness { get; set; } = 0.75;

    [JsonPropertyName("audioIntensityEnabled")]
    public bool AudioIntensityEnabled { get; set; } = true;

    [JsonPropertyName("audioSource")]
    public string AudioSource { get; set; } = DefaultAudioSource;

    public ScreenConfig Normalized() => string.IsNullOrWhiteSpace(AudioSource)
        ? this with { AudioSource = DefaultAudioSource } : this;

    public IReadOnlyList<string> Validate() => !double.IsFinite(Brightness) || Brightness is < 0 or > 1
        ? ["screen.brightness must be between 0 and 1."] : [];
}
