using System.Globalization;
using LightSync.Application;
using LightSync.Core.Configuration;
using LightSync.Devices.OpenRgb;

namespace LightSync.Cli.Commands;

internal static class DiscoverCommand
{
    internal static async Task<int> RunAsync(CommandContext context, string adapterId, string? host, int? port, bool save,
        CancellationToken cancellationToken)
    {
        // Nanoleaf's established output includes model and firmware information.
        if (!save && string.Equals(adapterId, "nanoleaf", StringComparison.OrdinalIgnoreCase))
        {
            return await NanoleafCommands.DiscoverAsync(cancellationToken);
        }

        var config = await context.LoadAsync(cancellationToken);
        var selected = context.DeviceSelectors.Count == 0 ? config.Device
            : (await context.LoadSelectedProfileAsync(cancellationToken)).Device;
        var settings = string.Equals(selected.Adapter, adapterId, StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>(selected.Settings, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        if (host is not null)
        {
            settings["host"] = host;
        }
        if (port is { } serverPort)
        {
            settings["port"] = serverPort.ToString(CultureInfo.InvariantCulture);
        }

        if (string.Equals(adapterId, "openrgb", StringComparison.OrdinalIgnoreCase))
        {
            await OpenRgbServerLauncher.EnsureAvailableAsync(OpenRgbSettings.FromDictionary(settings), Console.WriteLine, cancellationToken);
        }
        var devices = await DeviceDiscoveryService.DiscoverAsync(adapterId, settings, cancellationToken);
        Console.WriteLine($"Found {devices.Count} device(s).");
        var profiles = save ? await context.LoadProfilesAsync(cancellationToken) : [];
        foreach (var device in devices)
        {
            Console.WriteLine($"{device.Name} · per-LED Direct mode: {device.SupportsStreaming}");
            Console.WriteLine(ConfigurationLoader.Serialize(new AppConfig { Device = device.Device }));
            if (save && !profiles.Any(p => DeviceDiscoveryService.MatchesProfile(p, device)))
            {
                profiles.Add(new DeviceProfile { Name = device.Name, Device = device.Device, SyncEnabled = device.SupportsStreaming });
            }
        }

        if (save)
        {
            await context.SaveProfilesAsync(profiles, cancellationToken);
            ConsoleUI.Success($"Profiles saved to {context.ProfilesPath}.");
        }

        return 0;
    }
}
