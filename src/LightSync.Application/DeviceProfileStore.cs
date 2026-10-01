using System.Text.Json;
using System.Text.Json.Serialization;
using LightSync.Core.Configuration;

namespace LightSync.Application;

public sealed record DeviceProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "New device";

    public bool SyncEnabled { get; init; } = true;

    public int BrightnessPercent { get; set; } = 100;

    public DeviceConfig Device { get; init; } = new();
}

public static class DeviceProfileStore
{
    public static string DefaultPath => Path.Combine(ConfigurationPaths.ConfigDirectory, "devices.json");

    public static async Task<IReadOnlyList<DeviceProfile>> LoadAsync(
        string path, DeviceConfig initialDevice, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [new DeviceProfile { Name = "Configured device", Device = initialDevice }];
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var profiles = JsonSerializer.Deserialize(json, DeviceProfileJsonContext.Default.ListDeviceProfile)
            ?? throw new ConfigurationException("The device profile file is empty.");
        if (profiles.Any(p => p is null || !Guid.TryParseExact(p.Id, "N", out _) || string.IsNullOrWhiteSpace(p.Name)
                || p.Device is null || p.BrightnessPercent is < 0 or > 100)
            || profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != profiles.Count)
        {
            throw new ConfigurationException("Device profiles must have unique IDs, names and device settings.");
        }

        return profiles.Select(p => p with { Device = p.Device.Normalized() }).ToList();
    }

    public static async Task SaveAsync(string path, IReadOnlyList<DeviceProfile> profiles, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(profiles.ToList(), DeviceProfileJsonContext.Default.ListDeviceProfile);
        await File.WriteAllTextAsync(path + ".tmp", json + Environment.NewLine, cancellationToken);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<DeviceProfile>))]
internal sealed partial class DeviceProfileJsonContext : JsonSerializerContext;
