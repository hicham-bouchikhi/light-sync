using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Devices.OpenRgb;

namespace LightSync.Application;

/// <summary>Opens the local OpenRGB application when its SDK server is needed.</summary>
public static class OpenRgbServerLauncher
{
    private static readonly SemaphoreSlim StartupGate = new(1, 1);

    public static string LogDirectory => Path.Combine(ConfigurationPaths.ConfigDirectory, "logs");

    public static async Task EnsureAvailableAsync(OpenRgbSettings settings, Action<string>? reportStatus,
        CancellationToken cancellationToken)
    {
        using var host = new LocalOpenRgbHost();
        await EnsureAvailableAsync(settings, host, TimeSpan.FromSeconds(30), reportStatus, cancellationToken);
    }

    internal static async Task EnsureAvailableAsync(OpenRgbSettings settings, IOpenRgbServerHost host,
        TimeSpan startupTimeout, Action<string>? reportStatus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        await StartupGate.WaitAsync(cancellationToken);
        try
        {
            if (await host.CanConnectAsync(settings, cancellationToken))
            {
                return;
            }

            if (!IsLocalHost(settings.Host))
            {
                throw new DeviceUnreachableException($"Could not reach OpenRGB at {settings.Host}:{settings.Port}. "
                    + "Enable the SDK server on that computer, then try again.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(startupTimeout);
            var alreadyRunning = host.IsRunning;
            try
            {
                if (alreadyRunning)
                {
                    reportStatus?.Invoke("OpenRGB is already running. Waiting for its SDK server… "
                        + "Enable SDK Server in OpenRGB if needed.");
                }
                else
                {
                    reportStatus?.Invoke("Opening OpenRGB with its SDK server enabled. Waiting for hardware detection…");
                    host.Start(settings);
                }

                while (!await host.CanConnectAsync(settings, timeout.Token))
                {
                    if (host.StartedProcessExitCode is { } exitCode)
                    {
                        if (exitCode is 126 or 127)
                        {
                            throw new DeviceUnreachableException("Could not open OpenRGB. Install OpenRGB and make it "
                                + "available on PATH, then try again." + LogHint(host));
                        }
                        throw new DeviceUnreachableException($"OpenRGB exited with code {exitCode} before its SDK server was ready. "
                            + "Check OpenRGB's hardware access and server settings, then try again." + LogHint(host));
                    }

                    await Task.Delay(200, timeout.Token);
                }

                if (!alreadyRunning)
                {
                    await host.WaitForDetectionAsync(settings, timeout.Token);
                }
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DeviceUnreachableException($"OpenRGB's SDK server at {settings.Host}:{settings.Port} is not ready. "
                    + "Enable SDK Server in OpenRGB and check its port and hardware access, then try again." + LogHint(host), ex);
            }
        }
        finally
        {
            StartupGate.Release();
        }
    }

    private static bool IsLocalHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static string LogHint(IOpenRgbServerHost host) => host.LogPath is { } path ? " OpenRGB log: " + path : string.Empty;

    internal static ProcessStartInfo CreateStartInfo(OpenRgbSettings settings)
    {
        var executable = OperatingSystem.IsWindows() ? "OpenRGB.exe" : "openrgb";
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(AppContext.BaseDirectory, "OpenRGB.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenRGB", "OpenRGB.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "OpenRGB", "OpenRGB.exe")]
            : OperatingSystem.IsMacOS()
            ? ["/Applications/OpenRGB.app/Contents/MacOS/OpenRGB",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Applications", "OpenRGB.app", "Contents", "MacOS", "OpenRGB")]
            : [];
        var start = new ProcessStartInfo(candidates.FirstOrDefault(File.Exists) ?? executable)
        {
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--gui");
        start.ArgumentList.Add("--server");
        start.ArgumentList.Add("--server-host");
        start.ArgumentList.Add(string.Equals(settings.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            ? OpenRgbSettings.DefaultHost : settings.Host);
        start.ArgumentList.Add("--server-port");
        start.ArgumentList.Add(settings.Port.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--noautoconnect");
        return start;
    }

    internal static ProcessStartInfo CreateLoggedStartInfo(OpenRgbSettings settings, string logPath) =>
        CreateLoggedStartInfo(CreateStartInfo(settings), logPath);

    internal static ProcessStartInfo CreateLoggedStartInfo(ProcessStartInfo command, string logPath)
    {
        if (OperatingSystem.IsWindows())
        {
            command.CreateNoWindow = true;
            return command;
        }

        // A fixed wrapper lets OpenRGB own regular file descriptors rather than
        // pipes that require LightSync to keep draining them after it closes.
        // All variable data is passed as argv or environment values, never script text.
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("exec \"$@\" >>\"$LIGHTSYNC_OPENRGB_LOG\" 2>&1");
        start.ArgumentList.Add("light-sync-openrgb");
        start.ArgumentList.Add(command.FileName);
        foreach (var argument in command.ArgumentList)
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["LIGHTSYNC_OPENRGB_LOG"] = logPath;
        return start;
    }

    private sealed class LocalOpenRgbHost : IOpenRgbServerHost, IDisposable
    {
        private Process? started;

        public string? LogPath { get; private set; }

        public bool IsRunning
        {
            get
            {
                var running = false;
                foreach (var process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try
                        {
                            running |= process.ProcessName.StartsWith("OpenRGB", StringComparison.OrdinalIgnoreCase);
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                        {
                            // Other processes may exit or deny access during enumeration.
                        }
                    }
                }

                return running;
            }
        }

        public int? StartedProcessExitCode => started is { HasExited: true } ? started.ExitCode : null;

        public Task<bool> CanConnectAsync(OpenRgbSettings settings, CancellationToken cancellationToken) =>
            OpenRgbDiscovery.IsServerAvailableAsync(settings, cancellationToken);

        public void Start(OpenRgbSettings settings)
        {
            try
            {
                ProcessStartInfo start;
                if (OperatingSystem.IsWindows())
                {
                    start = CreateLoggedStartInfo(settings, string.Empty);
                }
                else
                {
                    Directory.CreateDirectory(LogDirectory);
                    LogPath = Path.Combine(LogDirectory, $"openrgb-{Guid.NewGuid():N}.log");
                    // Validate access before starting the child; the wrapper appends
                    // to this file and leaves the user's OpenRGB logging settings alone.
                    using (File.Create(LogPath))
                    {
                    }
                    start = CreateLoggedStartInfo(settings, LogPath);
                }
                started = Process.Start(start)
                    ?? throw new DeviceUnreachableException("Could not open OpenRGB. Install OpenRGB and make it available on PATH.");
            }
            catch (Win32Exception ex)
            {
                throw new DeviceUnreachableException("Could not open OpenRGB. Install OpenRGB and make it available on PATH "
                    + "(or in the standard Applications / Program Files folder), then try again.", ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DeviceUnreachableException("Could not create the OpenRGB log in " + LogDirectory
                    + ". Check directory permissions and free disk space, then try again.", ex);
            }
        }

        public Task WaitForDetectionAsync(OpenRgbSettings settings, CancellationToken cancellationToken) =>
            OpenRgbDiscovery.WaitForDetectionAsync(settings, cancellationToken);

        // Dispose the handle only: OpenRGB remains available to other lighting clients.
        public void Dispose() => started?.Dispose();
    }
}

internal interface IOpenRgbServerHost
{
    string? LogPath => null;

    bool IsRunning { get; }

    int? StartedProcessExitCode { get; }

    Task<bool> CanConnectAsync(OpenRgbSettings settings, CancellationToken cancellationToken);

    void Start(OpenRgbSettings settings);

    Task WaitForDetectionAsync(OpenRgbSettings settings, CancellationToken cancellationToken);
}
