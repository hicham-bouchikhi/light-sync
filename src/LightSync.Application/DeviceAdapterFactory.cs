using LightSync.Core.Devices;
using LightSync.Devices.Hue;
using LightSync.Devices.Nanoleaf;
using LightSync.Devices.OpenRgb;
using LightSync.Devices.Wled;

namespace LightSync.Application;

/// <summary>
/// The single place vendor code enters the application. A switch rather than reflection,
/// because the CLI is published as native AOT and must not depend on runtime type discovery.
/// </summary>
public sealed class DeviceAdapterFactory : IDeviceAdapterFactory
{
    public IReadOnlyList<DeviceAdapterDescriptor> AvailableAdapters { get; } =
    [
        new("nanoleaf", "Nanoleaf", "Nanoleaf local API with extControl streaming.", IsImplemented: true),
        new("fake", "Fake device", "In-memory device for testing without hardware.", IsImplemented: true),
        new("wled", "WLED", "Planned: WLED realtime UDP.", IsImplemented: false),
        new("hue", "Philips Hue", "Planned: Hue Entertainment API.", IsImplemented: false),
        new("openrgb", "OpenRGB", "Planned: OpenRGB SDK server.", IsImplemented: false),
    ];

    public ILightDevice Create(string adapterId, IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return adapterId?.Trim().ToLowerInvariant() switch
        {
            "nanoleaf" => new NanoleafAdapter(NanoleafSettings.FromDictionary(settings)),
            "fake" => new FakeDevice(),
            "wled" => new WledAdapter(),
            "hue" => new PhilipsHueAdapter(),
            "openrgb" => new OpenRgbAdapter(),
            _ => throw new DeviceException(
                $"Unknown device adapter '{adapterId}'. Known adapters: " +
                string.Join(", ", AvailableAdapters.Select(a => a.Id)) + "."),
        };
    }
}
