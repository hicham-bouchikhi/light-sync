using System.Globalization;
using LightSync.Application;
using LightSync.Core.Configuration;

namespace LightSync.Cli.Commands;

internal static class DiscoverCommand
{
    internal static async Task<int> RunAsync(CommandContext context, string adapterId, string? host, int? port,
        CancellationToken cancellationToken)
    {
        // Nanoleaf's established output includes model and firmware information.
        if (string.Equals(adapterId, "nanoleaf", StringComparison.OrdinalIgnoreCase))
        {
            return await NanoleafCommands.DiscoverAsync(cancellationToken);
        }

        var config = await context.LoadAsync(cancellationToken);
        var settings = string.Equals(config.Device.Adapter, adapterId, StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>(config.Device.Settings, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        if (host is not null)
        {
            settings["host"] = host;
        }
        if (port is { } serverPort)
        {
            settings["port"] = serverPort.ToString(CultureInfo.InvariantCulture);
        }

        var devices = await DeviceDiscoveryService.DiscoverAsync(adapterId, settings, cancellationToken);
        Console.WriteLine($"Found {devices.Count} OpenRGB controller(s). Copy a device section into your configuration:");
        foreach (var device in devices)
        {
            Console.WriteLine($"{device.Name} · per-LED Direct mode: {device.SupportsStreaming}");
            Console.WriteLine(ConfigurationLoader.Serialize(new AppConfig { Device = device.Device }));
        }

        return 0;
    }
}
