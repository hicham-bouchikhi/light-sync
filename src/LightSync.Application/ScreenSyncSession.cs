using LightSync.Core.Audio;
using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Core.Mapping;
using LightSync.Core.Pipeline;
using LightSync.Core.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightSync.Application;

/// <summary>Shared screen processing and lifecycle for desktop and CLI. Caller owns the devices and capture.</summary>
public sealed class ScreenSyncSession(IScreenCapture capture, IReadOnlyList<ScreenSyncOutput> outputs,
    IReadOnlyList<DeviceProfile> profiles, ScreenBrightness brightness, PipelineMetrics metrics,
    Func<IAudioCapture> audioCaptureFactory)
{
    public ScreenAudioMonitor? AudioMonitor { get; private set; }

    public static IColorProcessor BuildProcessor(AppConfig config, int zoneCount, ScreenBrightness brightness) =>
        new ScreenBrightnessProcessor(new ColorProcessor(new ZoneMapper(zoneCount, config.Mapping.ParsedLayout,
            config.Mapping.ParsedDirection, config.Mapping.Reverse, config.Mapping.CustomOrder),
            config.Processing.ToOptions() with { Brightness = 1 }), brightness);

    public async Task RunAsync(CaptureRequest request, bool audioIntensityEnabled, CancellationToken cancellationToken)
    {
        if (profiles.Count != 0 && profiles.Count != outputs.Count)
        {
            throw new InvalidOperationException("Each device output needs its saved profile.");
        }
        await using var monitor = new ScreenAudioMonitor(brightness, audioCaptureFactory);
        AudioMonitor = monitor;
        try
        {
            foreach (var output in outputs)
            {
                if (output.Device is IBrightnessControl master)
                {
                    await master.SetBrightnessAsync(100, cancellationToken);
                }
            }
            await monitor.SetEnabledAsync(audioIntensityEnabled, cancellationToken);
            var pipeline = new LightSyncPipeline(capture, outputs, metrics, NullLogger<LightSyncPipeline>.Instance);
            // Capture and processing must not resume on Avalonia's UI context.
            await Task.Run(() => pipeline.RunAsync(request, cancellationToken), cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new CaptureException(metrics.CapturedFrames == 0
                    ? "The screen source ended without producing any frames. Choose a screen / region again."
                    : "The screen-sharing source ended. Choose a screen / region again to resume sync.");
            }
        }
        finally
        {
            AudioMonitor = null;
            await monitor.SetEnabledAsync(false, CancellationToken.None);
            for (var i = 0; i < profiles.Count; i++)
            {
                if (outputs[i].Device is IBrightnessControl master)
                {
                    using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        await master.SetBrightnessAsync(profiles[i].BrightnessPercent, restore.Token);
                    }
                    catch (Exception ex)
                    {
                        metrics.RecordDeviceError($"{outputs[i].Device.Name}: restoring brightness failed: {ex.Message}");
                    }
                }
            }
            // A master command can power the lamp on, so blackout must be last.
            foreach (var output in outputs)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await output.Device.SetStaticColorAsync(RgbColor.Black, shutdown.Token);
                }
                catch (Exception ex)
                {
                    metrics.RecordDeviceError($"{output.Device.Name}: blackout failed: {ex.Message}");
                }
            }
        }
    }
}
