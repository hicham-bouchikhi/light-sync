using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;

namespace LightSync.Core.Audio;

/// <summary>Captures playback through PulseAudio or PipeWire's PulseAudio compatibility server.</summary>
public sealed class PulseAudioCapture : IAudioCapture
{
    private readonly string source;
    private readonly byte[] bytes = new byte[1024 * 2 * sizeof(float)];
    private readonly float[] samples = new float[1024 * 2];
    private readonly CancellationTokenSource readTimeout = new();
    private Process? process;
    private Task<string>? errors;

    public PulseAudioCapture(string source = "@DEFAULT_MONITOR@")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        this.source = source;
    }

    public int SampleRate => 48000;

    public int Channels => 2;

    public int SamplesPerChannel => 1024;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (process is not null)
        {
            throw new InvalidOperationException("Audio capture is already started.");
        }

        var start = new ProcessStartInfo("parec")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--raw");
        start.ArgumentList.Add("--format=float32le");
        start.ArgumentList.Add("--rate=48000");
        start.ArgumentList.Add("--channels=2");
        start.ArgumentList.Add("--latency-msec=30");
        start.ArgumentList.Add("--client-name=LightSync");
        start.ArgumentList.Add("--stream-name=Playback lighting");
        start.ArgumentList.Add("--device=" + source);
        try
        {
            process = Process.Start(start) ?? throw new IOException("Could not start parec.");
            errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        }
        catch (Win32Exception ex)
        {
            throw new IOException("Audio capture requires parec (pulseaudio-utils or your distribution's PulseAudio tools).", ex);
        }

        return Task.CompletedTask;
    }

    public async Task<ReadOnlyMemory<float>> ReadAsync(CancellationToken cancellationToken)
    {
        var active = process ?? throw new InvalidOperationException("StartAsync must be called first.");
        // Reuse the timeout source/timer and the token's registration node across blocks.
        using var registration = cancellationToken.UnsafeRegister(
            static state => ((CancellationTokenSource)state!).Cancel(), readTimeout);
        readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await active.StandardOutput.BaseStream.ReadExactlyAsync(bytes, readTimeout.Token);
        }
        catch (EndOfStreamException ex)
        {
            var detail = errors is null ? string.Empty : await errors;
            throw new IOException($"Audio source '{source}' ended. {detail.Trim()}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("No audio samples arrived within 5 seconds. Check PipeWire/PulseAudio and the selected monitor source.", ex);
        }
        finally
        {
            readTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
            samples[i] = float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
        }

        return samples;
    }

    public async ValueTask DisposeAsync()
    {
        if (process is { } active)
        {
            if (!active.HasExited)
            {
                active.Kill();
            }

            await active.WaitForExitAsync();
            active.Dispose();
            process = null;
            if (errors is not null)
            {
                await errors;
            }
        }

        readTimeout.Dispose();
    }
}
