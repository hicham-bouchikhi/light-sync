using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightSync.Devices.Nanoleaf;

/// <summary>
/// Source-generated serialisation for the Nanoleaf local API, required for native AOT.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(NanoleafAuthResponse))]
[JsonSerializable(typeof(NanoleafDeviceInfo))]
[JsonSerializable(typeof(NanoleafPanelLayout))]
[JsonSerializable(typeof(NanoleafPanelLayoutWrapper))]
[JsonSerializable(typeof(NanoleafStreamControlResponse))]
[JsonSerializable(typeof(NanoleafStateRequest))]
[JsonSerializable(typeof(NanoleafBoolValue))]
[JsonSerializable(typeof(NanoleafEffectsWriteRequest))]
internal sealed partial class NanoleafJsonContext : JsonSerializerContext;

/// <summary>A partial state update; only the set members are sent.</summary>
internal sealed class NanoleafStateRequest
{
    [JsonPropertyName("on")]
    public NanoleafBoolValue? On { get; set; }

    [JsonPropertyName("brightness")]
    public NanoleafWriteValue? Brightness { get; set; }

    [JsonPropertyName("hue")]
    public NanoleafWriteValue? Hue { get; set; }

    [JsonPropertyName("sat")]
    public NanoleafWriteValue? Saturation { get; set; }
}

internal sealed class NanoleafWriteValue(int value)
{
    [JsonPropertyName("value")]
    public int Value { get; set; } = value;
}

internal sealed class NanoleafEffectsWriteRequest(NanoleafEffectsWrite write)
{
    [JsonPropertyName("write")]
    public NanoleafEffectsWrite Write { get; set; } = write;
}

internal sealed class NanoleafEffectsWrite
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = "display";

    [JsonPropertyName("animType")]
    public string AnimationType { get; set; } = "extControl";

    /// <summary>Omitted for protocol v1; set to "v2" for the newer packet format.</summary>
    [JsonPropertyName("extControlVersion")]
    public string? ExtControlVersion { get; set; }
}
