using System.Globalization;
using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb;

/// <summary>SDK server endpoint and a controller selector, independent of runtime indices.</summary>
public sealed record OpenRgbSettings
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 6742;

    public string Host { get; init; } = DefaultHost;

    public int Port { get; init; } = DefaultPort;

    public int TimeoutMilliseconds { get; init; } = 3000;

    public string? ControllerName { get; init; }

    public string? Serial { get; init; }

    public string? Location { get; init; }

    public static OpenRgbSettings FromDictionary(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var result = new OpenRgbSettings
        {
            Host = Lookup(settings, "host") ?? DefaultHost,
            Port = ReadInt(settings, "port", DefaultPort),
            TimeoutMilliseconds = ReadInt(settings, "timeoutMilliseconds", 3000),
            ControllerName = Lookup(settings, "controllerName"),
            Serial = Lookup(settings, "serial"),
            Location = Lookup(settings, "location"),
        };
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535
            || TimeoutMilliseconds is < 100 or > 60000)
        {
            throw new DeviceException("OpenRGB requires a host, port in [1, 65535], and timeoutMilliseconds in [100, 60000].");
        }
    }

    internal bool Matches(OpenRgbController controller) =>
        (ControllerName is null || string.Equals(ControllerName, controller.Name, StringComparison.Ordinal))
        && (Serial is null || string.Equals(Serial, controller.Serial, StringComparison.Ordinal))
        && (Location is null || string.Equals(Location, controller.Location, StringComparison.Ordinal));

    public Dictionary<string, string> ToDictionary() => new(StringComparer.Ordinal)
    {
        ["host"] = Host,
        ["port"] = Port.ToString(CultureInfo.InvariantCulture),
        ["timeoutMilliseconds"] = TimeoutMilliseconds.ToString(CultureInfo.InvariantCulture),
        ["controllerName"] = ControllerName ?? string.Empty,
        ["serial"] = Serial ?? string.Empty,
        ["location"] = Location ?? string.Empty,
    };

    private static string? Lookup(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int ReadInt(IReadOnlyDictionary<string, string> settings, string key, int defaultValue)
    {
        var text = Lookup(settings, key);
        if (text is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new DeviceException($"OpenRGB device.settings.{key} must be an integer.");
        }

        return value;
    }
}
