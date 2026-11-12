using LightSync.Application;
using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Devices.OpenRgb;

namespace LightSync.Cli.Commands;

/// <summary>
/// Everything the commands share. Constructed once in Program so no service-locator or
/// reflection-based container is needed, which keeps the AOT build clean.
/// </summary>
internal sealed class CommandContext
{
    public CommandContext(string configPath, string? profilesPath = null, string[]? deviceSelectors = null,
        IDeviceAdapterFactory? adapters = null)
    {
        ConfigPath = Path.GetFullPath(configPath);
        Displays = new HyprlandDisplayEnumerator();
        Adapters = adapters ?? new DeviceAdapterFactory();
        ProfilesPath = Path.GetFullPath(profilesPath ?? Path.Combine(Path.GetDirectoryName(ConfigPath)!, "devices.json"));
        DeviceSelectors = deviceSelectors ?? [];
    }

    public string ConfigPath { get; }

    public string ProfilesPath { get; }

    public string PidPath => Path.GetFullPath(ConfigPath) == Path.GetFullPath(ConfigurationPaths.ConfigFile)
        ? ConfigurationPaths.PidFile : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ConfigPath))!, "light-sync.pid");

    public IReadOnlyList<string> DeviceSelectors { get; }

    public IDisplayEnumerator Displays { get; }

    public IDeviceAdapterFactory Adapters { get; }

    public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
        ConfigurationLoader.LoadValidatedAsync(ConfigPath, cancellationToken);

    public Task SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
        ConfigurationLoader.SaveAsync(ConfigPath, config, cancellationToken);

    public async Task<List<DeviceProfile>> LoadProfilesAsync(CancellationToken cancellationToken)
    {
        var config = await LoadAsync(cancellationToken);
        var profiles = (await DeviceProfileStore.LoadAsync(ProfilesPath, config.Device, cancellationToken)).ToList();
        if (!File.Exists(ProfilesPath))
        {
            // Persist the legacy import so IDs printed by `devices list` stay usable.
            await SaveProfilesAsync(profiles, cancellationToken);
        }
        return profiles;
    }

    public Task SaveProfilesAsync(IReadOnlyList<DeviceProfile> profiles, CancellationToken cancellationToken) =>
        DeviceProfileStore.SaveAsync(ProfilesPath, profiles, cancellationToken);

    public void RequireIdle()
    {
        if (PidFile.ReadRunningPid(PidPath) is { } pid)
        {
            throw new ConfigurationException($"A CLI session is running (pid {pid}). Use 'stop' before pairing, device inspection or LED tests.");
        }
    }

    public IReadOnlyList<DeviceProfile> SelectProfiles(IReadOnlyList<DeviceProfile> profiles, bool sync, bool allowMissing = false)
    {
        if (DeviceSelectors.Count == 0)
        {
            if (sync)
            {
                return profiles.Where(p => p.SyncEnabled).ToArray();
            }
            if (profiles.Count != 1)
            {
                throw new ConfigurationException("Choose one profile with --device <ID or name>; see 'devices list'.");
            }
            return profiles;
        }

        List<DeviceProfile> selected = [];
        foreach (var selector in DeviceSelectors)
        {
            var matches = profiles.Where(p => p.Id == selector || p.Name == selector).ToArray();
            if (matches.Length == 0 && allowMissing)
            {
                continue;
            }
            if (matches.Length != 1)
            {
                throw new ConfigurationException($"Device '{selector}' is unknown or ambiguous. Use an ID from 'devices list'.");
            }
            if (!selected.Any(p => p.Id == matches[0].Id))
            {
                selected.Add(matches[0]);
            }
        }
        if (!sync && selected.Count != 1)
        {
            throw new ConfigurationException("This command requires exactly one --device.");
        }
        return selected;
    }

    public async Task<DeviceProfile> LoadSelectedProfileAsync(CancellationToken cancellationToken) =>
        SelectProfiles(await LoadProfilesAsync(cancellationToken), sync: false)[0];

    public async Task<ILightDevice> ConnectAsync(DeviceProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Device.Adapter == "openrgb")
        {
            await OpenRgbServerLauncher.EnsureAvailableAsync(OpenRgbSettings.FromDictionary(profile.Device.Settings),
                Console.WriteLine, cancellationToken);
        }
        var device = Adapters.Create(profile.Device.Adapter, profile.Device.Settings);
        try
        {
            await device.ConnectAsync(cancellationToken);
            return device;
        }
        catch
        {
            await device.DisposeAsync();
            throw;
        }
    }

}
