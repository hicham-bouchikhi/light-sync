using System.Text.Json.Serialization;

namespace LightSync.Devices.Nanoleaf;

public sealed class NanoleafAuthResponse
{
    [JsonPropertyName("auth_token")]
    public string? AuthToken { get; set; }
}

public sealed class NanoleafDeviceInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("serialNo")]
    public string? SerialNumber { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("firmwareVersion")]
    public string? FirmwareVersion { get; set; }

    [JsonPropertyName("hardwareVersion")]
    public string? HardwareVersion { get; set; }

    [JsonPropertyName("state")]
    public NanoleafState? State { get; set; }

    [JsonPropertyName("panelLayout")]
    public NanoleafPanelLayoutWrapper? PanelLayout { get; set; }

    [JsonPropertyName("effects")]
    public NanoleafEffects? Effects { get; set; }
}

public sealed class NanoleafState
{
    [JsonPropertyName("on")]
    public NanoleafBoolValue? On { get; set; }

    [JsonPropertyName("brightness")]
    public NanoleafIntValue? Brightness { get; set; }

    [JsonPropertyName("hue")]
    public NanoleafIntValue? Hue { get; set; }

    [JsonPropertyName("sat")]
    public NanoleafIntValue? Saturation { get; set; }

    [JsonPropertyName("ct")]
    public NanoleafIntValue? ColorTemperature { get; set; }

    [JsonPropertyName("colorMode")]
    public string? ColorMode { get; set; }
}

public sealed class NanoleafEffects
{
    [JsonPropertyName("select")]
    public string? Selected { get; set; }

    [JsonPropertyName("effectsList")]
    public string[]? EffectsList { get; set; }
}

public sealed class NanoleafBoolValue
{
    [JsonPropertyName("value")]
    public bool Value { get; set; }
}

public sealed class NanoleafIntValue
{
    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("min")]
    public int? Min { get; set; }

    [JsonPropertyName("max")]
    public int? Max { get; set; }
}

public sealed class NanoleafPanelLayoutWrapper
{
    [JsonPropertyName("layout")]
    public NanoleafPanelLayout? Layout { get; set; }
}

public sealed class NanoleafPanelLayout
{
    [JsonPropertyName("numPanels")]
    public int NumPanels { get; set; }

    [JsonPropertyName("positionData")]
    public NanoleafPanelPosition[]? PositionData { get; set; }
}

public sealed class NanoleafPanelPosition
{
    [JsonPropertyName("panelId")]
    public int PanelId { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("o")]
    public int Orientation { get; set; }

    [JsonPropertyName("shapeType")]
    public int ShapeType { get; set; }
}

/// <summary>Response to enabling extControl, when the device supports streaming.</summary>
public sealed class NanoleafStreamControlResponse
{
    [JsonPropertyName("streamControlIpAddr")]
    public string? IpAddress { get; set; }

    [JsonPropertyName("streamControlPort")]
    public int? Port { get; set; }

    [JsonPropertyName("streamControlProtocol")]
    public string? Protocol { get; set; }
}
