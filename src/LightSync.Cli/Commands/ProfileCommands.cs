using LightSync.Application;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Cli.Commands;

internal static class ProfileCommands
{
    internal static async Task<int> ShowAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var profile = await context.LoadSelectedProfileAsync(cancellationToken);
        Console.WriteLine($"{profile.Id}  {profile.Name}");
        Console.WriteLine($"adapter: {profile.Device.Adapter}");
        Console.WriteLine($"sync enabled: {profile.SyncEnabled}");
        Console.WriteLine($"master brightness: {profile.BrightnessPercent}%");
        foreach (var setting in profile.Device.Settings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{setting.Key}: {setting.Value}");
        }
        return 0;
    }

    internal static async Task<int> ListAsync(CommandContext context, CancellationToken cancellationToken)
    {
        foreach (var profile in await context.LoadProfilesAsync(cancellationToken))
        {
            Console.WriteLine($"{profile.Id}  {profile.Name}  {profile.Device.Adapter}  "
                + $"sync={(profile.SyncEnabled ? "on" : "off")}  master={profile.BrightnessPercent}%");
        }
        return 0;
    }

    internal static async Task<int> SaveAsync(CommandContext context, bool add, string? name, string? adapter,
        string[] settings, string[] unsetSettings, bool? enabled, int? brightness,
        CancellationToken cancellationToken)
    {
        var profiles = await context.LoadProfilesAsync(cancellationToken);
        var profile = add ? new DeviceProfile() : context.SelectProfiles(profiles, sync: false)[0];
        var values = new Dictionary<string, string>(profile.Device.Settings, StringComparer.Ordinal);
        foreach (var assignment in settings)
        {
            var split = assignment.IndexOf('=', StringComparison.Ordinal);
            if (split < 1)
            {
                throw new ConfigurationException("Device settings must use key=value.");
            }
            var key = assignment[..split];
            if (key.Contains("token", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("tokenEnvironmentVariable", StringComparison.Ordinal))
            {
                throw new ConfigurationException("Store tokens through 'pair --save' or an environment variable.");
            }
            values[key] = assignment[(split + 1)..];
        }
        foreach (var key in unsetSettings)
        {
            values.Remove(key);
        }
        var updated = profile with
        {
            Name = name ?? profile.Name,
            SyncEnabled = enabled ?? profile.SyncEnabled,
            BrightnessPercent = brightness ?? profile.BrightnessPercent,
            Device = profile.Device with { Adapter = adapter ?? profile.Device.Adapter, Settings = values },
        };
        if (string.IsNullOrWhiteSpace(updated.Name) || updated.BrightnessPercent is < 0 or > 100)
        {
            throw new ConfigurationException("A profile needs a name and brightness between 0 and 100.");
        }
        if (!context.Adapters.AvailableAdapters.Any(a => a.Id == updated.Device.Adapter))
        {
            throw new ConfigurationException("Unknown adapter. See 'list-adapters'.");
        }
        if (add)
        {
            profiles.Add(updated);
        }
        else
        {
            profiles[profiles.FindIndex(p => p.Id == updated.Id)] = updated;
        }
        await context.SaveProfilesAsync(profiles, cancellationToken);
        ConsoleUI.Success($"Saved {updated.Id} ({updated.Name}).");
        return 0;
    }

    internal static async Task<int> RemoveAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var profiles = await context.LoadProfilesAsync(cancellationToken);
        var profile = context.SelectProfiles(profiles, sync: false)[0];
        profiles.RemoveAll(p => p.Id == profile.Id);
        await context.SaveProfilesAsync(profiles, cancellationToken);
        ConsoleUI.Success($"Removed {profile.Name}. Its token file is retained.");
        return 0;
    }

    internal static async Task<int> BrightnessAsync(CommandContext context, int percent, CancellationToken cancellationToken)
    {
        context.RequireIdle();
        if (percent is < 0 or > 100)
        {
            throw new ConfigurationException("Brightness must be between 0 and 100.");
        }
        var profiles = await context.LoadProfilesAsync(cancellationToken);
        var profile = context.SelectProfiles(profiles, sync: false)[0];
        await using var device = await context.ConnectAsync(profile, cancellationToken);
        if (device is not IBrightnessControl brightness)
        {
            throw new DeviceException("This adapter does not implement master brightness control.");
        }
        await brightness.SetBrightnessAsync(percent, cancellationToken);
        profiles[profiles.FindIndex(p => p.Id == profile.Id)] = profile with { BrightnessPercent = percent };
        await context.SaveProfilesAsync(profiles, cancellationToken);
        ConsoleUI.Success($"Set {profile.Name} master brightness to {percent}%.");
        return 0;
    }

    internal static async Task<int> BlackoutAsync(CommandContext context, CancellationToken cancellationToken)
    {
        context.RequireIdle();
        var selected = context.SelectProfiles(await context.LoadProfilesAsync(cancellationToken), sync: true);
        foreach (var profile in selected)
        {
            await using var device = await context.ConnectAsync(profile, cancellationToken);
            await device.SetStaticColorAsync(LightSync.Core.Colors.RgbColor.Black, cancellationToken);
        }
        return 0;
    }
}
