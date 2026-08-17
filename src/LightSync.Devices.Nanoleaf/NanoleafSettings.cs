using System.Globalization;

namespace LightSync.Devices.Nanoleaf;

/// <summary>
/// Connection settings for a Nanoleaf device. Never holds the token itself — only the name of
/// the environment variable to read it from.
/// </summary>
public sealed record NanoleafSettings
{
    public const int DefaultPort = 16021;
    public const string DefaultTokenEnvironmentVariable = "NANOLEAF_TOKEN";

    /// <summary>Host or IP. When null the device is discovered over mDNS.</summary>
    public string? Host { get; init; }

    public int Port { get; init; } = DefaultPort;

    public string TokenEnvironmentVariable { get; init; } = DefaultTokenEnvironmentVariable;

    /// <summary>Model to require, so a config cannot silently drive the wrong hardware.</summary>
    public string? ExpectedModel { get; init; }

    /// <summary>
    /// Overrides where the token file is read from. Null means the default location. Exists so
    /// the path is configurable and so tests never reach into the developer's home directory.
    /// </summary>
    public string? SecretsFilePath { get; init; }

    /// <summary>
    /// Explicit panel ids in the order they should receive zone colours. Empty means use the
    /// device's own layout order.
    /// </summary>
    public int[] LedMapping { get; init; } = [];

    public Uri BaseUri => new(
        string.Create(CultureInfo.InvariantCulture, $"http://{Host}:{Port}/api/v1/"));

    public static NanoleafSettings FromDictionary(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new NanoleafSettings
        {
            Host = Lookup(settings, "host"),
            Port = TryLookupInt(settings, "port") ?? DefaultPort,
            TokenEnvironmentVariable =
                Lookup(settings, "tokenEnvironmentVariable") ?? DefaultTokenEnvironmentVariable,
            ExpectedModel = Lookup(settings, "expectedModel"),
            SecretsFilePath = Lookup(settings, "secretsFilePath"),
            LedMapping = ParseMapping(Lookup(settings, "ledMapping")),
        };
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (Port is < 1 or > 65535)
        {
            problems.Add($"device.settings.port must be between 1 and 65535, got {Port}.");
        }

        if (string.IsNullOrWhiteSpace(TokenEnvironmentVariable))
        {
            problems.Add("device.settings.tokenEnvironmentVariable must not be empty.");
        }

        return problems;
    }

    /// <summary>
    /// Deliberately omits the token environment variable's *value*. Overridden so that
    /// logging or interpolating settings can never leak a secret.
    /// </summary>
    public override string ToString() =>
        $"Nanoleaf {{ Host = {Host ?? "(discover)"}, Port = {Port}, " +
        $"TokenEnvironmentVariable = {TokenEnvironmentVariable}, " +
        $"ExpectedModel = {ExpectedModel ?? "(any)"}, LedMapping = {LedMapping.Length} entries }}";

    private static string? Lookup(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int? TryLookupInt(IReadOnlyDictionary<string, string> settings, string key) =>
        Lookup(settings, key) is { } text
        && int.TryParse(text, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int[] ParseMapping(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mapping = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], CultureInfo.InvariantCulture, out mapping[i]))
            {
                throw new ArgumentException(
                    $"device.settings.ledMapping must be a comma-separated list of panel ids; " +
                    $"'{parts[i]}' is not a number.",
                    nameof(value));
            }
        }

        return mapping;
    }
}
