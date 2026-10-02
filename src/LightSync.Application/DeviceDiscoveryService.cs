using System.Globalization;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Devices.Nanoleaf;
using LightSync.Devices.OpenRgb;

namespace LightSync.Application;

/// <summary>Discovery returns profile settings without taking control of the discovered lights.</summary>
public sealed record DiscoveredLightDevice(string Name, DeviceConfig Device, bool SupportsStreaming);

public static class DeviceDiscoveryService
{
    public static async Task<IReadOnlyList<DiscoveredLightDevice>> DiscoverAsync(string adapterId,
        IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        List<DiscoveredLightDevice> results = [];
        switch (adapterId?.Trim().ToLowerInvariant())
        {
            case "nanoleaf":
                var lamps = await NanoleafDiscovery.DiscoverAsync(TimeSpan.FromSeconds(6), cancellationToken);
                foreach (var lamp in lamps)
                {
                    results.Add(new DiscoveredLightDevice(lamp.Name ?? lamp.Host, new DeviceConfig
                    {
                        Adapter = "nanoleaf",
                        Settings = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["host"] = lamp.Host,
                            ["port"] = lamp.Port.ToString(CultureInfo.InvariantCulture),
                        },
                    }, true));
                }
                break;
            case "openrgb":
                var endpoint = OpenRgbSettings.FromDictionary(settings);
                var controllers = await OpenRgbDiscovery.DiscoverAsync(endpoint, cancellationToken);
                foreach (var controller in controllers)
                {
                    var selector = endpoint with
                    {
                        ControllerName = controller.Name,
                        Serial = controller.Serial,
                        Location = controller.Location,
                    };
                    results.Add(new DiscoveredLightDevice(controller.Name, new DeviceConfig
                    {
                        Adapter = "openrgb", Settings = selector.ToDictionary(),
                    }, controller.SupportsDirectColor && controller.LedCount > 0));
                }
                break;
            default:
                throw new DeviceException($"Discovery is available for 'nanoleaf' and 'openrgb', not '{adapterId}'.");
        }

        return results;
    }
}
