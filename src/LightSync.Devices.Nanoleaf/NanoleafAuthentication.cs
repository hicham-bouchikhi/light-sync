using System.Text.Json;
using System.Text.Json.Serialization;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Devices.Nanoleaf;

/// <summary>
/// Resolves the device auth token. The environment variable wins, so a token never has to be
/// written to disk at all; a gitignored local file is the fallback for convenience.
/// </summary>
public static class NanoleafAuthentication
{
    public static string ResolveToken(NanoleafSettings settings, string? secretsFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var fromEnvironment = Environment.GetEnvironmentVariable(settings.TokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        var path = secretsFilePath ?? ConfigurationPaths.SecretsFile;
        var fromFile = TryReadFromFile(path);
        if (!string.IsNullOrWhiteSpace(fromFile))
        {
            return fromFile.Trim();
        }

        throw new DeviceAuthenticationException(
            $"No Nanoleaf token found. Set the {settings.TokenEnvironmentVariable} environment " +
            $"variable, or run 'light-sync pair' to obtain one." + Environment.NewLine +
            $"A token may also be placed in {path}.");
    }

    public static bool TryResolveToken(
        NanoleafSettings settings,
        out string? token,
        string? secretsFilePath = null)
    {
        try
        {
            token = ResolveToken(settings, secretsFilePath);
            return true;
        }
        catch (DeviceAuthenticationException)
        {
            token = null;
            return false;
        }
    }

    /// <summary>
    /// Writes the token to the secrets file with owner-only permissions. Kept separate from the
    /// main configuration so the config file stays safe to share.
    /// </summary>
    public static async Task SaveTokenAsync(
        string token,
        string? secretsFilePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var path = secretsFilePath ?? ConfigurationPaths.SecretsFile;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var existing = ReadSecrets(path) ?? new NanoleafSecrets();
        existing.NanoleafToken = token;

        var json = JsonSerializer.Serialize(existing, NanoleafSecretsJsonContext.Default.NanoleafSecrets);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string? TryReadFromFile(string path) => ReadSecrets(path)?.NanoleafToken;

    private static NanoleafSecrets? ReadSecrets(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(path), NanoleafSecretsJsonContext.Default.NanoleafSecrets);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A malformed or unreadable secrets file is treated as absent; the caller then
            // reports the missing-token case, which never echoes file contents.
            return null;
        }
    }
}

internal sealed class NanoleafSecrets
{
    [JsonPropertyName("nanoleafToken")]
    public string? NanoleafToken { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(NanoleafSecrets))]
internal sealed partial class NanoleafSecretsJsonContext : JsonSerializerContext;
