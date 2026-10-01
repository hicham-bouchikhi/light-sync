using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Core.Audio;

/// <summary>Spectrum and Colors are reusable buffers; copy them before returning from the callback.</summary>
public readonly record struct AudioSyncStatus(long Frames, AudioFeatures Features,
    ReadOnlyMemory<double> Spectrum, ReadOnlyMemory<RgbColor> Colors);

/// <summary>One playback capture drives all devices. Caller owns capture and connected devices.</summary>
public sealed class AudioSyncSession
{
    private readonly IAudioCapture capture;
    private readonly IReadOnlyList<ILightDevice> devices;
    private readonly AudioColorMapper mapper;
    private readonly RgbColor[][] frames;

    public AudioSyncSession(IAudioCapture capture, IReadOnlyList<ILightDevice> devices, AudioConfig options)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count == 0)
        {
            throw new ArgumentException("Select at least one device.", nameof(devices));
        }

        this.capture = capture;
        this.devices = devices;
        mapper = new AudioColorMapper(options);
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

    public async Task RunAsync(Action<AudioSyncStatus>? report, CancellationToken cancellationToken)
    {
        var analyzer = new AudioAnalyzer(capture.SampleRate, capture.SamplesPerChannel);
        long count = 0;
        try
        {
            await capture.StartAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var samples = await capture.ReadAsync(cancellationToken);
                if (samples.IsEmpty)
                {
                    throw new IOException("The audio source ended.");
                }

                var features = analyzer.Analyze(samples.Span, capture.Channels);
                mapper.Advance(features, (double)capture.SamplesPerChannel / capture.SampleRate);
                for (var i = 0; i < devices.Count; i++)
                {
                    mapper.Map(features, frames[i]);
                    await devices[i].SendFrameAsync(frames[i], cancellationToken);
                }

                count++;
                if (count % 10 == 0)
                {
                    report?.Invoke(new AudioSyncStatus(count, features, analyzer.Spectrum, frames[0]));
                }
            }
        }
        finally
        {
            // All devices return to black, including those already updated when a later send fails.
            for (var i = 0; i < devices.Count; i++)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await devices[i].SetStaticColorAsync(RgbColor.Black, shutdown.Token);
                }
                catch (Exception)
                {
                    // Continue cleaning up other devices; preserve the original capture/send failure.
                }
            }
        }
    }

    public void UpdateOptions(AudioConfig options) => mapper.UpdateOptions(options);
}
