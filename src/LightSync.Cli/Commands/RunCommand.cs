using System.Diagnostics;
using System.Globalization;
using LightSync.Core.Capture;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace LightSync.Cli.Commands;

internal static class RunCommand
{
    public static async Task<int> RunAsync(
        CommandContext context,
        Func<AppConfig, IScreenCapture> captureFactory,
        CancellationToken cancellationToken)
    {
        var config = await context.LoadAsync(cancellationToken);

        if (!config.Capture.IsConfigured)
        {
            ConsoleUI.Error("No capture area configured yet. Run 'light-sync setup' first.");
            return 1;
        }

        await using var device = context.Adapters.Create(config.Device.Adapter, config.Device.Settings);
        await device.ConnectAsync(cancellationToken);

        var validation = DeviceCapabilityValidator.ValidateForStreaming(
            device.Capabilities, config.Mapping.ZoneCount);

        if (!validation.IsValid)
        {
            ConsoleUI.Error($"{device.Name} cannot render the configured {config.Mapping.ZoneCount} zones:");
            foreach (var problem in validation.Problems)
            {
                Console.WriteLine($"  - {problem}");
            }

            return 1;
        }

        var processor = CommandContext.BuildProcessor(config);
        var request = CommandContext.BuildRequest(config);
        var metrics = new PipelineMetrics();

        await using var capture = captureFactory(config);

        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddSimpleConsole(options => options.SingleLine = true));

        var pipeline = new LightSyncPipeline(
            capture, processor, device, metrics, loggerFactory.CreateLogger<LightSyncPipeline>());

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Syncing {config.Capture.Area} at {config.Capture.Fps} fps to {device.Name} " +
            $"({processor.ZoneCount} zones)."));
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        await using var pidFile = await PidFile.CreateAsync(cancellationToken);
        using var reporter = StartReporter(metrics, cancellationToken);

        try
        {
            await pipeline.RunAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            // Leave the device in a defined state rather than holding the last frame forever.
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await device.SetStaticColorAsync(LightSync.Core.Colors.RgbColor.Black, shutdown.Token);
            }
            catch (Exception ex) when (ex is DeviceException or OperationCanceledException)
            {
                // Best effort; a device that has already gone away must not fail the shutdown.
            }
        }

        ConsoleUI.EndLiveStatus();
        Console.WriteLine();
        ConsoleUI.WriteMetrics(metrics);
        return 0;
    }

    /// <summary>Repaints the status line on a timer rather than once per frame.</summary>
    private static Timer StartReporter(PipelineMetrics metrics, CancellationToken cancellationToken) =>
        new(
            _ =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    ConsoleUI.WriteLiveStatus($"  {metrics}");
                }
            },
            state: null,
            dueTime: TimeSpan.FromMilliseconds(500),
            period: TimeSpan.FromMilliseconds(500));
}

/// <summary>
/// Records this process's pid so <c>light-sync stop</c> can find it, and removes the file on
/// exit so a stale pid never points at an unrelated process.
/// </summary>
internal sealed class PidFile : IAsyncDisposable
{
    private readonly string path;

    private PidFile(string path) => this.path = path;

    public static async Task<PidFile> CreateAsync(CancellationToken cancellationToken)
    {
        var path = ConfigurationPaths.PidFile;
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
    public static int? ReadRunningPid()
    {
        var path = ConfigurationPaths.PidFile;

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
