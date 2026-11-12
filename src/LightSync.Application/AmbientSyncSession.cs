using System.Diagnostics;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Application;

/// <summary>Colors is a reusable buffer; copy it before returning from the callback.</summary>
public readonly record struct AmbientSyncStatus(long Frames, LightingConfig Options, ReadOnlyMemory<RgbColor> Colors);

/// <summary>
/// One time-based effect drives all devices, with no audio or screen capture. The caller owns
/// the connected devices and their profile selection.
/// </summary>
public sealed class AmbientSyncSession
{
    private static readonly TimeSpan DefaultFrameInterval = TimeSpan.FromMilliseconds(33);

    private readonly IReadOnlyList<ILightDevice> devices;
    private readonly IReadOnlyList<DeviceProfile> profiles;
    private readonly RgbColor[][] frames;
    private readonly AmbientColorMapper mapper;
    private readonly TimeSpan frameInterval;
    private readonly int reportEveryFrames;

    public AmbientSyncSession(IReadOnlyList<ILightDevice> devices, IReadOnlyList<DeviceProfile> profiles,
        LightingConfig options, TimeSpan? frameInterval = null, int reportEveryFrames = 10)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reportEveryFrames);
        if (devices.Count == 0)
        {
            throw new ArgumentException("Select at least one device.", nameof(devices));
        }

        if (profiles.Count != 0 && profiles.Count != devices.Count)
        {
            throw new InvalidOperationException("Each device output needs its saved profile.");
        }

        this.devices = devices;
        this.profiles = profiles;
        this.reportEveryFrames = reportEveryFrames;
        this.frameInterval = frameInterval ?? DefaultFrameInterval;
        mapper = new AmbientColorMapper(options);
        frames = new RgbColor[devices.Count][];
        for (var i = 0; i < devices.Count; i++)
        {
            var capabilities = devices[i].Capabilities;
            if (!capabilities.SupportsStreaming || capabilities.MaximumZones <= 0)
            {
                throw new DeviceException($"{devices[i].Name} does not support addressable streaming.");
            }

            frames[i] = new RgbColor[capabilities.MaximumZones];
        }
    }

    public void UpdateOptions(LightingConfig options) => mapper.UpdateOptions(options);

    public async Task RunAsync(Action<AmbientSyncStatus>? report, CancellationToken cancellationToken)
    {
        try
        {
            // The effect applies the common brightness, so the device master level is held at
            // 100% and restored from the profiles before the final blackout.
            for (var i = 0; i < devices.Count; i++)
            {
                if (devices[i] is IBrightnessControl master)
                {
                    await master.SetBrightnessAsync(100, cancellationToken);
                }
            }

            var clock = Stopwatch.StartNew();
            var previous = TimeSpan.Zero;
            long count = 0;
            using var timer = new PeriodicTimer(frameInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                // Wall-clock deltas keep the pulse and travel pacing steady even when a
                // device send takes longer than one interval.
                var now = clock.Elapsed;
                mapper.Advance((now - previous).TotalSeconds);
                previous = now;
                for (var i = 0; i < devices.Count; i++)
                {
                    mapper.Map(frames[i]);
                    await devices[i].SendFrameAsync(frames[i], cancellationToken);
                }

                count++;
                if (count % reportEveryFrames == 0)
                {
                    report?.Invoke(new AmbientSyncStatus(count, mapper.Options, frames[0]));
                }
            }
        }
        finally
        {
            for (var i = 0; i < profiles.Count; i++)
            {
                if (devices[i] is IBrightnessControl master)
                {
                    using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        await master.SetBrightnessAsync(profiles[i].BrightnessPercent, restore.Token);
                    }
                    catch (Exception)
                    {
                        // Restore the remaining devices; preserve the original failure.
                    }
                }
            }

            // A master command can power the lamp on, so blackout must be last.
            for (var i = 0; i < devices.Count; i++)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await devices[i].SetStaticColorAsync(RgbColor.Black, shutdown.Token);
                }
                catch (Exception)
                {
                    // Continue cleaning up other devices; preserve the original failure.
                }
            }
        }
    }
}
