using System.Diagnostics;
using LightSync.Core.Capture;
using LightSync.Core.Devices;
using LightSync.Core.Processing;
using Microsoft.Extensions.Logging;

namespace LightSync.Core.Pipeline;

/// <summary>
/// Runs capture-and-process and device output as two concurrent stages joined by a one-deep
/// latest-frame slot. A slow device therefore shows up as dropped frames rather than as
/// growing latency or unbounded memory.
/// </summary>
public sealed class LightSyncPipeline(
    IScreenCapture capture,
    IColorProcessor processor,
    ILightDevice device,
    PipelineMetrics metrics,
    ILogger<LightSyncPipeline> logger)
{
    public async Task RunAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.TryValidate(out var requestError))
        {
            throw new ArgumentException(requestError, nameof(request));
        }

        DeviceCapabilityValidator.ThrowIfInvalid(
            DeviceCapabilityValidator.ValidateForStreaming(device.Capabilities, processor.ZoneCount));

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var slot = new LatestFrameSlot(processor.ZoneCount);

        var captureLoop = RunCaptureAndProcessAsync(request, slot, stopping.Token);
        var outputLoop = RunOutputAsync(slot, stopping.Token);

        try
        {
            // If either stage fails, stop the other rather than leaving it running blind.
            var finished = await Task.WhenAny(captureLoop, outputLoop);
            if (finished.IsFaulted)
            {
                await stopping.CancelAsync();
            }

            await Task.WhenAll(captureLoop, outputLoop);
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
            await Task.WhenAll(captureLoop, outputLoop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            slot.Dispose();
        }
    }

    private async Task RunCaptureAndProcessAsync(
        CaptureRequest request,
        LatestFrameSlot slot,
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

                var zoneFrame = slot.Rent();
                zoneFrame.CapturedAt = Stopwatch.GetTimestamp();
                processor.Process(frame, zoneFrame.Colors);
                metrics.RecordProcessed();

                if (slot.Publish(zoneFrame))
                {
                    metrics.RecordDropped();
                }

                frame = await capture.ReadFrameAsync(cancellationToken);
            }
        }
        finally
        {
            slot.Complete();
        }
    }

    private async Task RunOutputAsync(LatestFrameSlot slot, CancellationToken cancellationToken)
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
                metrics.RecordDeviceError();
                logger.LogWarning(ex, "Dropping a frame after a device error.");
            }
            finally
            {
                slot.Return(frame);
            }
        }
    }
}
