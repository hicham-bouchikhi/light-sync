using System.Diagnostics;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Devices.Nanoleaf;

namespace LightSync.Cli.Commands;

internal static class NanoleafCommands
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PairingWindow = TimeSpan.FromSeconds(60);

    public static async Task<int> DiscoverAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Searching for Nanoleaf devices...");

        var devices = await NanoleafDiscovery.DiscoverAsync(DiscoveryTimeout, cancellationToken);

        if (devices.Count == 0)
        {
            ConsoleUI.Warn("No Nanoleaf devices found. Check they are on the same network as this machine.");
            return 1;
        }

        Console.WriteLine($"{devices.Count} device(s) found:");
        foreach (var device in devices)
        {
            Console.WriteLine($"  {device}");
        }

        return 0;
    }

    /// <summary>
    /// Obtains an auth token. The device only issues one while in pairing mode, so this polls
    /// until the user has held the power button.
    /// </summary>
    public static async Task<int> PairAsync(
        CommandContext context,
        string? host,
        bool save,
        CancellationToken cancellationToken)
    {
        var target = host ?? await ResolveSingleHostAsync(cancellationToken);
        if (target is null)
        {
            return 1;
        }

        Console.WriteLine($"Pairing with the Nanoleaf device at {target}.");
        Console.WriteLine();
        Console.WriteLine("  Hold the device's power button for about five seconds,");
        Console.WriteLine("  until its LED starts flashing, then wait.");
        Console.WriteLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var deadline = Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(deadline) < PairingWindow)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var token = await NanoleafApi.PairAsync(
                    target, NanoleafSettings.DefaultPort, http, cancellationToken);

                ConsoleUI.EndLiveStatus();
                ConsoleUI.Success("Paired successfully.");
                Console.WriteLine();

                if (save)
                {
                    await NanoleafAuthentication.SaveTokenAsync(token, null, cancellationToken);
                    Console.WriteLine($"Token saved to {ConfigurationPaths.SecretsFile} (owner-readable only).");
                }
                else
                {
                    // Printed once, on request, so the user can put it in their shell profile.
                    // Nothing else in the application ever writes the token to output.
                    Console.WriteLine("Add this to your shell profile:");
                    Console.WriteLine();
                    Console.WriteLine($"  export NANOLEAF_TOKEN={token}");
                }

                await RecordHostAsync(context, target, cancellationToken);
                return 0;
            }
            catch (DeviceAuthenticationException)
            {
                var remaining = PairingWindow - Stopwatch.GetElapsedTime(deadline);
                ConsoleUI.WriteLiveStatus(
                    $"  Waiting for the device to enter pairing mode... {remaining.TotalSeconds:0}s left");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        ConsoleUI.EndLiveStatus();
        ConsoleUI.Error(
            "Timed out waiting for pairing mode. Hold the power button until the LED flashes, " +
            "then run 'light-sync pair' again.");
        return 1;
    }

    private static async Task<string?> ResolveSingleHostAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("No host given; searching for Nanoleaf devices...");
        var devices = await NanoleafDiscovery.DiscoverAsync(DiscoveryTimeout, cancellationToken);

        switch (devices.Count)
        {
            case 0:
                ConsoleUI.Error("No Nanoleaf devices found. Pass --host to name one explicitly.");
                return null;

            case 1:
                Console.WriteLine($"Found {devices[0]}.");
                return devices[0].Host;

            default:
                ConsoleUI.Warn($"{devices.Count} Nanoleaf devices found; pass --host to choose one:");
                foreach (var device in devices)
                {
                    Console.WriteLine($"  {device}");
                }

                return null;
        }
    }

    /// <summary>Remembers the paired host so later commands need no --host.</summary>
    private static async Task RecordHostAsync(
        CommandContext context,
        string host,
        CancellationToken cancellationToken)
    {
        var config = await ConfigurationLoader.LoadAsync(context.ConfigPath, cancellationToken);

        var settings = new Dictionary<string, string>(config.Device.Settings, StringComparer.Ordinal)
        {
            ["host"] = host,
            ["port"] = NanoleafSettings.DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["tokenEnvironmentVariable"] = NanoleafSettings.DefaultTokenEnvironmentVariable,
        };

        var updated = config with
        {
            Device = new DeviceConfig { Adapter = "nanoleaf", Settings = settings },
        };

        await context.SaveAsync(updated, cancellationToken);
        Console.WriteLine($"Configuration updated: device.adapter = nanoleaf, host = {host}.");
    }
}
