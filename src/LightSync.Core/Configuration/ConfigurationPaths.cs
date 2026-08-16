namespace LightSync.Core.Configuration;

public static class ConfigurationPaths
{
    public const string ApplicationName = "light-sync";

    public static string ConfigDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var root = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdg;

            return Path.Combine(root, ApplicationName);
        }
    }

    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    /// <summary>
    /// Optional file holding device secrets. Named <c>.local.json</c> so the repository's
    /// gitignore already excludes it if a user keeps it alongside a checkout.
    /// </summary>
    public static string SecretsFile => Path.Combine(ConfigDirectory, "secrets.local.json");

    public static string RuntimeDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            return string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Path.GetTempPath(), ApplicationName)
                : Path.Combine(xdg, ApplicationName);
        }
    }

    /// <summary>File holding the pid of a running instance, used by <c>stop</c>.</summary>
    public static string PidFile => Path.Combine(RuntimeDirectory, "light-sync.pid");
}
