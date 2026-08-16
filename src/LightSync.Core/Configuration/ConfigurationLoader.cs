using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightSync.Core.Configuration;

/// <summary>
/// Source-generated JSON context. Reflection-based serialisation is unavailable under
/// native AOT, so every serialised type must be listed here.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfig))]
public partial class ConfigJsonContext : JsonSerializerContext;

public sealed class ConfigurationException : Exception
{
    public ConfigurationException()
    {
    }

    public ConfigurationException(string message)
        : base(message)
    {
    }

    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class ConfigurationLoader
{
    /// <summary>
    /// Loads configuration from <paramref name="path"/>, or returns defaults when the file
    /// does not exist yet.
    /// </summary>
    public static async Task<AppConfig> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (IOException ex)
        {
            throw new ConfigurationException($"Could not read configuration from '{path}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ConfigurationException($"Not permitted to read configuration at '{path}'.", ex);
        }

        return Parse(json, path);
    }

    public static AppConfig Parse(string json, string sourceDescription)
    {
        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException(
                $"'{sourceDescription}' is not valid JSON: {ex.Message}", ex);
        }

        if (config is null)
        {
            throw new ConfigurationException($"'{sourceDescription}' contained no configuration.");
        }

        return config;
    }

    public static async Task SaveAsync(string path, AppConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);

        // Write to a sibling temp file and move it into place so an interrupted save can
        // never leave a half-written config behind.
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json + Environment.NewLine, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public static string Serialize(AppConfig config) =>
        JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);

    /// <summary>
    /// Loads configuration and throws if it is not usable, listing every problem at once.
    /// </summary>
    public static async Task<AppConfig> LoadValidatedAsync(string path, CancellationToken cancellationToken)
    {
        var config = await LoadAsync(path, cancellationToken);
        var problems = config.Validate();

        if (problems.Count > 0)
        {
            throw new ConfigurationException(
                $"Configuration at '{path}' is not valid:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
        }

        return config;
    }
}
