using System.Diagnostics;
using System.Globalization;
using LightSync.Application;
using LightSync.Core.Audio;
using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Core.Pipeline;
using LightSync.Core.Processing;

namespace LightSync.Cli.Commands;

internal sealed record RunOptions
{
    public string Source { get; init; } = "audio";
    public string? AudioSource { get; init; }
    public bool Demo { get; init; }
    public bool Preview { get; init; }
    public bool SelectArea { get; init; }
    public int Seconds { get; init; }
    public bool Watch { get; init; } = true;
    public string[] Settings { get; init; } = [];
}

internal static class RunCommand
{
    internal static async Task<int> RunAsync(CommandContext context, RunOptions options,
        CancellationToken cancellationToken, Func<string, IAudioCapture>? audioCaptureFactory = null)
    {
        if (options.Source is not ("audio" or "screen" or "ambient") || options.Seconds < 0)
        {
            throw new ConfigurationException(
                "--source must be audio, screen or ambient; --seconds must not be negative.");
        }
        if (options.Source != "screen" && (options.Demo || options.SelectArea))
        {
            throw new ConfigurationException("--demo and --select-area require --source screen.");
        }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Seconds > 0)
        {
            stopping.CancelAfter(TimeSpan.FromSeconds(options.Seconds));
        }
        await using var pidFile = options.Preview
            ? null : await PidFile.CreateAsync(context.PidPath, stopping.Token);
        try
        {
            return options.Source switch
            {
                "audio" => await RunAudioAsync(context, options,
                    audioCaptureFactory ?? (source => new PulseAudioCapture(source)), stopping.Token),
                "ambient" => await RunAmbientAsync(context, options, stopping.Token),
                _ => await RunScreenAsync(context, options,
                    audioCaptureFactory ?? (source => new PulseAudioCapture(source)), stopping.Token),
            };
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            if (options.Source != "screen")
            {
                ConsoleUI.EndLiveStatus();
            }
        }
    }

    private static async Task<AppConfig> LoadOptionsAsync(CommandContext context, RunOptions options,
        CancellationToken cancellationToken) => ConfigCommand.Apply(await context.LoadAsync(cancellationToken), options.Settings);

    private static async Task<int> RunAudioAsync(CommandContext context, RunOptions options,
        Func<string, IAudioCapture> captureFactory, CancellationToken cancellationToken)
    {
        var started = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var config = await LoadOptionsAsync(context, options, cancellationToken);
            var profiles = options.Preview ? Array.Empty<DeviceProfile>()
                : context.SelectProfiles(await context.LoadProfilesAsync(cancellationToken), sync: true, allowMissing: started);
            if (!options.Preview && profiles.Count == 0)
            {
                ConsoleUI.Warn("No enabled devices remain. Audio sync stopped.");
                return 0;
            }
            await using var connections = await ConnectedProfiles.CreateAsync(context, profiles, cancellationToken);
            await using var preview = options.Preview ? new TerminalDevice(config.Mapping.ZoneCount) : null;
            var devices = preview is null ? connections.Devices : [preview];
            foreach (var connected in connections.Devices.Select((device, index) => (device, index)))
            {
                if (connected.device is IBrightnessControl master)
                {
                    await master.SetBrightnessAsync(profiles[connected.index].BrightnessPercent, cancellationToken);
                }
            }
            var audio = config.Audio with { Source = options.AudioSource ?? config.Audio.Source };
            await using var capture = captureFactory(audio.Source);
            var session = new AudioSyncSession(capture, devices, audio);
            using var iteration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var restart = false;
            var watch = WatchAsync(options.Watch, async () =>
            {
                var updated = await LoadOptionsAsync(context, options, iteration.Token);
                var next = options.Preview ? Array.Empty<DeviceProfile>()
                    : context.SelectProfiles(await context.LoadProfilesAsync(iteration.Token), sync: true, allowMissing: true);
                if (updated.Audio.Source != config.Audio.Source || !ProfilesEqual(profiles, next))
                {
                    restart = true;
                    await iteration.CancelAsync();
                    return;
                }
                session.UpdateOptions(updated.Audio with { Source = audio.Source });
            }, iteration.Token);
            started = true;
            Console.WriteLine($"Audio sync from {audio.Source} to {string.Join(", ", devices.Select(d => d.Name))}. Ctrl+C to stop.");
            try
            {
                await session.RunAsync(status => ConsoleUI.WriteLiveStatus(string.Create(CultureInfo.InvariantCulture,
                    $"{ConsoleUI.ZoneStrip(status.Colors.Span)} frames {status.Frames} | level {status.Features.Level:F3} | bass {status.Features.Bass:F3} | mids {status.Features.Mid:F3} | treble {status.Features.Treble:F3} | {ConsoleUI.Spectrum(status.Spectrum.Span)}")), iteration.Token);
            }
            catch (OperationCanceledException) when (iteration.IsCancellationRequested)
            {
            }
            finally
            {
                await iteration.CancelAsync();
                await watch;
            }
            if (!restart)
            {
                return 0;
            }
            ConsoleUI.EndLiveStatus();
            Console.WriteLine("Device selection changed; restarting audio capture for the selected profiles.");
        }
        return 0;
    }

    internal static bool ProfilesEqual(IReadOnlyList<DeviceProfile> left, IReadOnlyList<DeviceProfile> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First == pair.Second);

    private static async Task<int> RunAmbientAsync(CommandContext context, RunOptions options,
        CancellationToken cancellationToken)
    {
        var started = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var config = await LoadOptionsAsync(context, options, cancellationToken);
            var profiles = options.Preview ? Array.Empty<DeviceProfile>()
                : context.SelectProfiles(await context.LoadProfilesAsync(cancellationToken), sync: true, allowMissing: started);
            if (!options.Preview && profiles.Count == 0)
            {
                ConsoleUI.Warn("No enabled devices remain. Lighting mode stopped.");
                return 0;
            }

            await using var connections = await ConnectedProfiles.CreateAsync(context, profiles, cancellationToken);
            await using var preview = options.Preview ? new TerminalDevice(config.Mapping.ZoneCount) : null;
            var devices = preview is null ? connections.Devices : [preview];
            var session = new AmbientSyncSession(devices, profiles, config.Lighting);
            using var iteration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var restart = false;
            var watch = WatchAsync(options.Watch, async () =>
            {
                var updated = await LoadOptionsAsync(context, options, iteration.Token);
                var next = options.Preview ? Array.Empty<DeviceProfile>()
                    : context.SelectProfiles(await context.LoadProfilesAsync(iteration.Token), sync: true, allowMissing: true);
                if (!ProfilesEqual(profiles, next))
                {
                    restart = true;
                    await iteration.CancelAsync();
                    return;
                }

                session.UpdateOptions(updated.Lighting);
            }, iteration.Token);
            started = true;
            Console.WriteLine($"Lighting mode {config.Lighting.Mode} to {string.Join(", ", devices.Select(d => d.Name))}. Ctrl+C to stop.");
            try
            {
                await session.RunAsync(status => ConsoleUI.WriteLiveStatus(string.Create(CultureInfo.InvariantCulture,
                    $"{ConsoleUI.ZoneStrip(status.Colors.Span)} frames {status.Frames} | {status.Options.Mode} | {status.Options.Color} | brightness {status.Options.Brightness:P0}")), iteration.Token);
            }
            catch (OperationCanceledException) when (iteration.IsCancellationRequested)
            {
            }
            finally
            {
                await iteration.CancelAsync();
                await watch;
            }

            if (!restart)
            {
                return 0;
            }

            ConsoleUI.EndLiveStatus();
            Console.WriteLine("Device selection changed; restarting the lighting mode for the selected profiles.");
        }

        return 0;
    }

    private static async Task<int> RunScreenAsync(CommandContext context, RunOptions options,
        Func<string, IAudioCapture> audioCaptureFactory, CancellationToken cancellationToken)
    {
        var config = await LoadOptionsAsync(context, options, cancellationToken);
        if (!options.Demo && !options.SelectArea && !config.Capture.IsConfigured)
        {
            throw new ConfigurationException("No capture area configured. Use 'select-area' or 'run --source screen --select-area'.");
        }
        var profiles = options.Preview ? Array.Empty<DeviceProfile>()
            : context.SelectProfiles(await context.LoadProfilesAsync(cancellationToken), sync: true);
        if (!options.Preview && profiles.Count == 0)
        {
            throw new ConfigurationException("Enable at least one device, choose --device, or use --preview.");
        }
        await using var connections = await ConnectedProfiles.CreateAsync(context, profiles, cancellationToken);
        await using var preview = options.Preview ? new TerminalDevice(config.Mapping.ZoneCount) : null;
        IReadOnlyList<ILightDevice> devices = preview is null ? connections.Devices : [preview];
        var brightness = new ScreenBrightness(config.Screen with { AudioSource = options.AudioSource ?? config.Screen.AudioSource });
        List<ScreenSyncOutput> outputs = [];
        SnapshotProcessor? snapshot = null;
        foreach (var device in devices)
        {
            var processor = ScreenSyncSession.BuildProcessor(config, device.Capabilities.MaximumZones, brightness);
            if (snapshot is null)
            {
                snapshot = new SnapshotProcessor(processor);
                processor = snapshot;
            }
            outputs.Add(new ScreenSyncOutput(processor, device));
        }
        var area = options.Demo || options.SelectArea ? new CaptureArea(0, 0, 640, 360) : config.Capture.Area;
        var scale = Math.Min(1, Math.Min(640.0 / area.Width, 360.0 / area.Height));
        var request = new CaptureRequest(config.Capture.DisplayId, area, config.Capture.Fps,
            Math.Max(1, (int)(area.Width * scale)), Math.Max(1, (int)(area.Height * scale)));
        await using IScreenCapture capture = options.Demo ? new SyntheticScreenCapture()
            : new PortalScreenCapturer(options.SelectArea ? null : config.Capture.RestoreToken,
                useSelectionBounds: options.SelectArea,
                onRecovering: recovering => ConsoleUI.Warn(recovering ? "Capture stalled; reconnecting…" : "Capture recovered."));
        await using var savingCapture = new SavingCapture(capture, context, options.SelectArea,
            persist: options.SelectArea || !options.Preview);
        var metrics = new PipelineMetrics();
        var session = new ScreenSyncSession(savingCapture, outputs, profiles, brightness, metrics,
            () => audioCaptureFactory(options.AudioSource ?? config.Screen.AudioSource));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watch = WatchAsync(options.Watch, async () =>
        {
            var updated = await LoadOptionsAsync(context, options, lifetime.Token);
            if (!options.Preview && !ProfilesEqual(profiles,
                context.SelectProfiles(await context.LoadProfilesAsync(lifetime.Token), sync: true, allowMissing: true)))
            {
                ConsoleUI.Warn("Device profiles changed. Stopping screen sync; start it again with the new selection.");
                await lifetime.CancelAsync();
                return;
            }
            brightness.UpdateOptions(updated.Screen);
            if (session.AudioMonitor is { } monitor)
            {
                await monitor.SetEnabledAsync(updated.Screen.AudioIntensityEnabled, lifetime.Token);
            }
        }, lifetime.Token);
        Console.WriteLine($"{(options.Demo ? "Demo" : "Screen")} sync to {string.Join(", ", devices.Select(d => d.Name))}. Ctrl+C to stop.");
        using var reporter = new Timer(_ =>
        {
            var intensity = brightness.Status;
            ConsoleUI.WriteLiveStatus(string.Create(CultureInfo.InvariantCulture,
                $"{snapshot!.Strip} {metrics} | brightness {intensity.Brightness:P0}")
                + (intensity.AudioError is { } error ? $" | audio unavailable: {error}" : string.Empty)
                + (metrics.LastDeviceError is { } deviceError ? $" | {deviceError}" : string.Empty));
        }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        try
        {
            await session.RunAsync(request, config.Screen.AudioIntensityEnabled, lifetime.Token);
        }
        finally
        {
            await lifetime.CancelAsync();
            await watch;
            ConsoleUI.EndLiveStatus();
            ConsoleUI.WriteMetrics(metrics);
        }
        return 0;
    }

    private static async Task WatchAsync(bool enabled, Func<Task> update, CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return;
        }
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await update();
                }
                catch (Exception ex) when (ex is ConfigurationException or System.Text.Json.JsonException or IOException)
                {
                    ConsoleUI.Warn("Could not reload settings: " + ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed class SavingCapture(IScreenCapture capture, CommandContext context, bool newSelection,
        bool persist) : IScreenCapture
    {
        public async Task<CapturedFrame> StartAsync(CaptureRequest request, CancellationToken cancellationToken)
        {
            var frame = await capture.StartAsync(request, cancellationToken);
            if (!persist || frame.IsEmpty || capture is not PortalScreenCapturer { Selection: { } selection })
            {
                return frame;
            }

            try
            {
                var config = await context.LoadAsync(cancellationToken);
                if (!newSelection
                    && string.Equals(selection.RestoreToken, config.Capture.RestoreToken, StringComparison.Ordinal))
                {
                    return frame;
                }

                var area = newSelection ? new CaptureArea(0, 0, selection.Width, selection.Height) : config.Capture.Area;
                await context.SaveAsync(config with
                {
                    Capture = CaptureConfig.FromArea(config.Capture.DisplayId, area, config.Capture.Fps,
                        selection.RestoreToken),
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is ConfigurationException or IOException or UnauthorizedAccessException
                or System.Text.Json.JsonException)
            {
                // A preview or read-only configuration must not abort capture after the picker.
                ConsoleUI.Warn("Could not save the capture selection: " + ex.Message);
            }

            return frame;
        }
        public Task<CapturedFrame> ReadFrameAsync(CancellationToken cancellationToken) => capture.ReadFrameAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => capture.StopAsync(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SnapshotProcessor(IColorProcessor processor) : IColorProcessor
    {
        private readonly Lock gate = new();
        private readonly RgbColor[] colors = new RgbColor[processor.ZoneCount];
        public int ZoneCount => processor.ZoneCount;
        public string Strip
        {
            get
            {
                lock (gate)
                {
                    return ConsoleUI.ZoneStrip(colors);
                }
            }
        }
        public void Process(in CapturedFrame frame, Span<RgbColor> destination)
        {
            processor.Process(frame, destination);
            lock (gate)
            {
                destination[..ZoneCount].CopyTo(colors);
            }
        }
        public void Reset() => processor.Reset();
    }
}

/// <summary>A terminal preview discards frames rather than retaining an unbounded history.</summary>
internal sealed class TerminalDevice(int zoneCount) : ILightDevice
{
    public string Name => "Preview only";
    public DeviceCapabilities Capabilities { get; } = new(zoneCount, true, true, false, false, true);
    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ConnectedProfiles : IAsyncDisposable
{
    private readonly List<ILightDevice> devices = [];
    public IReadOnlyList<ILightDevice> Devices => devices;

    public static async Task<ConnectedProfiles> CreateAsync(CommandContext context,
        IReadOnlyList<DeviceProfile> profiles, CancellationToken cancellationToken)
    {
        var result = new ConnectedProfiles();
        try
        {
            foreach (var profile in profiles)
            {
                var device = await context.ConnectAsync(profile, cancellationToken);
                result.devices.Add(device);
                var validation = DeviceCapabilityValidator.ValidateForStreaming(
                    device.Capabilities, Math.Max(1, device.Capabilities.MaximumZones));
                DeviceCapabilityValidator.ThrowIfInvalid(validation);
            }
            return result;
        }
        catch
        {
            await result.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var device in devices)
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await device.SetStaticColorAsync(RgbColor.Black, shutdown.Token);
            }
            catch (Exception ex)
            {
                ConsoleUI.Warn($"{device.Name}: blackout failed: {ex.Message}");
            }
            try
            {
                await device.DisposeAsync();
            }
            catch (Exception ex)
            {
                ConsoleUI.Warn($"{device.Name}: disconnect failed: {ex.Message}");
            }
        }
    }
}

/// <summary>
/// Records this process's pid so <c>light-sync stop</c> can find it, and removes the file on
/// exit so a stale pid never points at an unrelated process.
/// </summary>
internal sealed class PidFile : IAsyncDisposable
{
    private readonly string path;

    private PidFile(string path) => this.path = path;

    public static async Task<PidFile> CreateAsync(string path, CancellationToken cancellationToken)
    {
        if (ReadRunningPid(path) is { } runningPid)
        {
            throw new ConfigurationException($"light-sync is already running (pid {runningPid}); stop it before starting another session.");
        }
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            path,
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

        return new PidFile(path);
    }

    /// <summary>
    /// Reads the pid of a running instance, or null when none is running. A pid file left behind
    /// by a crashed process is treated as absent rather than reported as a live instance.
    /// </summary>
    public static int? ReadRunningPid(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (!int.TryParse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture, out var pid))
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : pid;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
