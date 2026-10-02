namespace LightSync.Devices.OpenRgb;

/// <summary>Controller metadata from the SDK. Index is valid only for the current device list.</summary>
public sealed record OpenRgbController(int Index, string Name, string Vendor, string Serial,
    string Location, int LedCount, bool SupportsDirectColor, IReadOnlyList<OpenRgbZone> Zones);

/// <summary>An OpenRGB hardware group containing a contiguous range of frame indices.</summary>
public sealed record OpenRgbZone(string Name, int FirstLedIndex, int LedCount);
