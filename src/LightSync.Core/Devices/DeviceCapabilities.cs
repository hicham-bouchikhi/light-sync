namespace LightSync.Core.Devices;

public sealed record DeviceCapabilities(
    int MaximumZones,
    bool SupportsStreaming,
    bool SupportsStaticColor,
    bool SupportsBrightness,
    bool SupportsEffects,
    bool SupportsPerZoneColor);
