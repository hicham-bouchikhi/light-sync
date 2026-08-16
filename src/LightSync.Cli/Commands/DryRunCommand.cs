using System.Diagnostics;
using System.Globalization;
using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Pipeline;

namespace LightSync.Cli.Commands;

/// <summary>
/// Captures and processes the selected rectangle, printing zone colours and frame rate.
/// Contacts no device.
/// </summary>
internal static class DryRunCommand
{
    public static async Task<int> RunAsync(
        CommandContext context,
        IScreenCapture capture,
        CancellationToken cancellationToken)
    {
        var config = await context.LoadAsync(cancellationToken);

        if (!config.Capture.IsConfigured)
        {
            ConsoleUI.Error("No capture area configured yet. Run 'light-sync setup' first.");
            return 1;
        }

        var processor = CommandContext.BuildProcessor(config);
        var request = CommandContext.BuildRequest(config);
        var metrics = new PipelineMetrics();

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Dry run: {config.Capture.Area} at {config.Capture.Fps} fps, " +
            $"{processor.ZoneCount} zones ({config.Mapping.Layout}, {config.Mapping.Direction})."));
        Console.WriteLine("No device will be contacted. Press Ctrl+C to stop.");
        Console.WriteLine();

        var zones = new RgbColor[processor.ZoneCount];
        var lastRedraw = 0L;

        try
        {
            var frame = await capture.StartAsync(request, cancellationToken);

            while (!cancellationToken.IsCancellationRequested && !frame.IsEmpty)
            {
                var capturedAt = Stopwatch.GetTimestamp();
                processor.Process(frame, zones);
                metrics.RecordCaptured();
                metrics.RecordProcessed();
                metrics.RecordSent(Stopwatch.GetElapsedTime(capturedAt));

                // Redraw at about 20 Hz; the terminal, not the pipeline, is the bottleneck.
                if (Stopwatch.GetElapsedTime(lastRedraw) > TimeSpan.FromMilliseconds(50))
                {
                    lastRedraw = Stopwatch.GetTimestamp();
                    ConsoleUI.WriteLiveStatus($"{ConsoleUI.ZoneStrip(zones)} {metrics}");
                }

                frame = await capture.ReadFrameAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        ConsoleUI.EndLiveStatus();
        Console.WriteLine();
        ConsoleUI.WriteMetrics(metrics);
        return 0;
    }
}
