using System.Diagnostics;
using LightSync.Core.Capture;
using LightSync.Core.Devices;
using LightSync.Core.Processing;
using Microsoft.Extensions.Logging;

namespace LightSync.Core.Pipeline;

/// <summary>
/// Captures once and processes each device's zones before handing them to independent output
/// stages through one-deep latest-frame slots. A slow device drops its old frames without
/// holding up the other devices or building latency.
/// </summary>
public sealed class LightSyncPipeline
{
    private readonly IScreenCapture capture;
    private readonly ScreenSyncOutput[] outputs;
    private readonly PipelineMetrics metrics;
    private readonly ILogger<LightSyncPipeline> logger;

    public LightSyncPipeline(IScreenCapture capture, IColorProcessor processor, ILightDevice device,
        PipelineMetrics metrics, ILogger<LightSyncPipeline> logger)
        : this(capture, [new ScreenSyncOutput(processor, device)], metrics, logger)
    {
    }

    public LightSyncPipeline(IScreenCapture capture, IReadOnlyList<ScreenSyncOutput> outputs,
        PipelineMetrics metrics, ILogger<LightSyncPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        if (outputs.Count == 0)
        {
            throw new ArgumentException("Select at least one screen output.", nameof(outputs));
        }

        this.capture = capture;
        this.outputs = outputs.ToArray();
        this.metrics = metrics;
        this.logger = logger;
    }

    public async Task RunAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.TryValidate(out var requestError))
        {
            throw new ArgumentException(requestError, nameof(request));
        }

        foreach (var output in outputs)
        {
            DeviceCapabilityValidator.ThrowIfInvalid(
                DeviceCapabilityValidator.ValidateForStreaming(output.Device.Capabilities, output.Processor.ZoneCount));
        }

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var slots = new LatestFrameSlot[outputs.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = new LatestFrameSlot(outputs[i].Processor.ZoneCount);
        }

        var loops = new Task[outputs.Length + 1];
        loops[0] = RunCaptureAndProcessAsync(request, slots, stopping.Token);
        for (var i = 0; i < outputs.Length; i++)
        {
            loops[i + 1] = RunOutputAsync(slots[i], outputs[i].Device, stopping.Token);
        }

        try
        {
            // If either stage fails, stop the other rather than leaving it running blind.
            var finished = await Task.WhenAny(loops);
            if (finished.IsFaulted)
            {
                await stopping.CancelAsync();
            }

            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Pipeline stopped.");
        }
        finally
        {
            await stopping.CancelAsync();

            // Both stages must be finished with the slot before it is disposed, so wait for
            // them even on the failure path. Their exceptions have already been surfaced
            // above, or are about to be by the throwing await.
            await Task.WhenAll(loops).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            foreach (var slot in slots)
            {
                slot.Dispose();
            }
        }
    }

    private async Task RunCaptureAndProcessAsync(
        CaptureRequest request,
        LatestFrameSlot[] slots,
        CancellationToken cancellationToken)
    {
        try
        {
            // Processing is cheap once the capture backend has downscaled, so it rides with
            // capture rather than paying for a third stage's handoff.
            var frame = await capture.StartAsync(request, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (frame.IsEmpty)
                {
                    logger.LogInformation("Capture source ended.");
                    break;
                }

                metrics.RecordCaptured();

                for (var i = 0; i < outputs.Length; i++)
                {
                    var slot = slots[i];
                    var zoneFrame = slot.Rent();
                    zoneFrame.CapturedAt = Stopwatch.GetTimestamp();
                    outputs[i].Processor.Process(frame, zoneFrame.Colors);
                    if (slot.Publish(zoneFrame))
                    {
                        metrics.RecordDropped();
                    }
                }

                metrics.RecordProcessed();

                frame = await capture.ReadFrameAsync(cancellationToken);
            }
        }
        finally
        {
            foreach (var slot in slots)
            {
                slot.Complete();
            }
        }
    }

    private async Task RunOutputAsync(LatestFrameSlot slot, ILightDevice device, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await slot.TakeAsync(cancellationToken);
            if (frame is null)
            {
                return;
            }

            try
            {
                await device.SendFrameAsync(frame.Colors, cancellationToken);
                metrics.RecordSent(Stopwatch.GetElapsedTime(frame.CapturedAt));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A device hiccup must not tear down the run; the next frame gets a fresh try.
                metrics.RecordDeviceError(outputs.Length == 1 ? ex.Message : $"{device.Name}: {ex.Message}");
                logger.LogWarning(ex, "Dropping a frame after a device error.");
            }
            finally
            {
                slot.Return(frame);
            }
        }
    }
}
