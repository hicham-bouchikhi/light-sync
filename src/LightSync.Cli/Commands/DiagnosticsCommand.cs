using System.Diagnostics;
using LightSync.Core.Capture;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Cli.Commands;

/// <summary>
/// Probes everything the pipeline depends on and reports it in one place, so a failure to run
/// can be diagnosed without guesswork.
/// </summary>
internal static class DiagnosticsCommand
{
    public static async Task<int> RunAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var checks = new List<Check>();

        checks.Add(CheckEnvironmentVariable("Wayland session", "WAYLAND_DISPLAY"));
        checks.Add(CheckEnvironmentVariable("D-Bus session bus", "DBUS_SESSION_BUS_ADDRESS"));
        checks.Add(CheckExecutable("hyprctl", "hyprctl"));
        checks.Add(CheckExecutable("GStreamer", "gst-launch-1.0"));
        checks.Add(CheckExecutable("avahi-browse (device discovery)", "avahi-browse"));
        checks.Add(await CheckGstElementAsync("GStreamer pipewiresrc plugin", "pipewiresrc", cancellationToken));
        checks.Add(await CheckGstElementAsync("GStreamer videoscale plugin", "videoscale", cancellationToken));
        checks.Add(await CheckPortalAsync(cancellationToken));
        checks.Add(await CheckDisplaysAsync(context, cancellationToken));

        var config = await AddConfigurationChecksAsync(context, checks, cancellationToken);

        if (config is not null)
        {
            checks.Add(await CheckDeviceAsync(context, config, cancellationToken));
        }

        Console.WriteLine();
        var width = checks.Max(c => c.Name.Length);
        foreach (var check in checks)
        {
            var marker = check.Ok ? "ok  " : "FAIL";
            Console.WriteLine($"  [{marker}] {check.Name.PadRight(width)}  {check.Detail}");
        }

        Console.WriteLine();
        var failures = checks.Count(c => !c.Ok);

        if (failures == 0)
        {
            ConsoleUI.Success("Everything checks out.");
            return 0;
        }

        ConsoleUI.Warn($"{failures} check(s) failed.");
        return 1;
    }

    private static async Task<AppConfig?> AddConfigurationChecksAsync(
        CommandContext context,
        List<Check> checks,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(context.ConfigPath))
        {
            checks.Add(new Check("Configuration", false, $"not found at {context.ConfigPath}; run 'light-sync setup'"));
            return null;
        }

        AppConfig config;
        try
        {
            config = await ConfigurationLoader.LoadValidatedAsync(context.ConfigPath, cancellationToken);
        }
        catch (ConfigurationException ex)
        {
            checks.Add(new Check("Configuration", false, ex.Message.ReplaceLineEndings(" ")));
            return null;
        }

        checks.Add(new Check("Configuration", true, context.ConfigPath));

        checks.Add(config.Capture.IsConfigured
            ? new Check("Capture area", true, $"{config.Capture.Area} at {config.Capture.Fps} fps")
            : new Check("Capture area", false, "not chosen yet; run 'light-sync setup'"));

        checks.Add(new Check(
            "Zone mapping",
            true,
            $"{config.Mapping.ZoneCount} zones, {config.Mapping.Layout}, {config.Mapping.Direction}" +
            (config.Mapping.Reverse ? ", reversed" : string.Empty)));

        checks.Add(config.Capture.RestoreToken is null
            ? new Check("Portal restore token", false, "absent; the picker will appear on the next run")
            : new Check("Portal restore token", true, "present; the picker will be skipped"));

        return config;
    }

    private static async Task<Check> CheckDeviceAsync(
        CommandContext context,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        const string name = "Lighting device";

        try
        {
            await using var device = context.Adapters.Create(config.Device.Adapter, config.Device.Settings);
            await device.ConnectAsync(cancellationToken);

            var zones = device.Capabilities.MaximumZones;
            var validation = DeviceCapabilityValidator.ValidateForStreaming(
                device.Capabilities, config.Mapping.ZoneCount);

            return validation.IsValid
                ? new Check(name, true, $"{device.Name}, {zones} addressable LEDs")
                : new Check(name, false, $"{device.Name}: {string.Join("; ", validation.Problems)}");
        }
        catch (DeviceException ex)
        {
            return new Check(name, false, ex.Message.ReplaceLineEndings(" "));
        }
    }

    private static async Task<Check> CheckDisplaysAsync(
        CommandContext context,
        CancellationToken cancellationToken)
    {
        const string name = "Display detection";

        try
        {
            var displays = await context.Displays.GetDisplaysAsync(cancellationToken);
            return new Check(
                name,
                true,
                $"{displays.Count} found: " + string.Join(", ", displays.Select(d => $"{d.Name} {d.Width}x{d.Height}")));
        }
        catch (Exception ex) when (ex is Core.Capture.Wayland.DisplayEnumerationException)
        {
            return new Check(name, false, ex.Message);
        }
    }

    /// <summary>Confirms the portal is on the bus, without opening a session or a dialog.</summary>
    private static async Task<Check> CheckPortalAsync(CancellationToken cancellationToken)
    {
        const string name = "Desktop portal (ScreenCast)";

        var result = await RunAsync(
            "busctl",
            ["--user", "introspect", "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop"],
            cancellationToken);

        if (result is null)
        {
            return new Check(name, false, "could not run busctl to check");
        }

        return result.Value.Output.Contains("org.freedesktop.portal.ScreenCast", StringComparison.Ordinal)
            ? new Check(name, true, "ScreenCast interface available")
            : new Check(name, false, "ScreenCast interface not offered by the portal");
    }

    private static async Task<Check> CheckGstElementAsync(
        string name,
        string element,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync("gst-inspect-1.0", [element], cancellationToken);

        return result is { ExitCode: 0 }
            ? new Check(name, true, "present")
            : new Check(name, false, $"'{element}' not found; install the matching gst plugin package");
    }

    private static Check CheckExecutable(string name, string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return new Check(name, true, candidate);
            }
        }

        return new Check(name, false, $"'{executable}' not found on PATH");
    }

    private static Check CheckEnvironmentVariable(string name, string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);

        return string.IsNullOrWhiteSpace(value)
            ? new Check(name, false, $"{variable} is not set")
            : new Check(name, true, $"{variable} is set");
    }

    private static async Task<(int ExitCode, string Output)?> RunAsync(
        string executable,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, stdout);
    }

    private readonly record struct Check(string Name, bool Ok, string Detail);
}
