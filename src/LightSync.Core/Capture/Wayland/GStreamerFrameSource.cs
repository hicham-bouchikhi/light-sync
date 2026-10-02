using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LightSync.Core.Capture.Wayland;

/// <summary>
/// Reads raw BGRx frames from a <c>gst-launch-1.0</c> child process.
/// </summary>
/// <remarks>
/// There is no managed PipeWire binding, and hand-rolling one would mean implementing SPA
/// format negotiation over P/Invoke. GStreamer's <c>pipewiresrc</c> already does that, and
/// putting <c>videocrop</c> and <c>videoscale</c> in the same pipeline means the 5120x1440
/// monitor is reduced to a few hundred pixels before any of it crosses into managed memory.
/// </remarks>
public sealed class GStreamerFrameSource : IAsyncDisposable, IFrameSource
{
    private readonly string sourceElement;
    private readonly CaptureArea? crop;
    private readonly int sourceWidth;
    private readonly int sourceHeight;
    private readonly byte[] buffer;
    private readonly StringBuilder diagnostics = new();
    private Process? process;
    private Task? stderrPump;
    private long sequence;
    private long startedAt;

    public GStreamerFrameSource(
        string sourceElement,
        int targetWidth,
        int targetHeight,
        int fps,
        CaptureArea? crop = null,
        int sourceWidth = 0,
        int sourceHeight = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceElement);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fps);

        this.sourceElement = sourceElement;
        this.crop = crop;
        this.sourceWidth = sourceWidth;
        this.sourceHeight = sourceHeight;

        Width = targetWidth;
        Height = targetHeight;
        Fps = fps;
        FrameSizeBytes = targetWidth * targetHeight * CapturedFrame.BytesPerPixel;
        buffer = new byte[FrameSizeBytes];
    }

    public int Width { get; }

    public int Height { get; }

    public int Fps { get; }

    public int FrameSizeBytes { get; }

    /// <summary>Whatever the child process wrote to stderr, for diagnostics.</summary>
    public string Diagnostics => diagnostics.ToString();

    /// <summary>
    /// The pipeline this source will run. Exposed so <c>diagnostics</c> can show it and so it
    /// can be asserted on without launching a process.
    /// </summary>
    public IReadOnlyList<string> BuildArguments()
    {
        // drop-only with max-rate is what actually throttles the stream. A framerate in the
        // caps filter only labels buffers, so on its own the pipeline would run flat out and
        // flood the pipe.
        List<string> arguments = ["-q"];

        // gst-launch-1.0 parses each argv entry as its own token, so an element and its
        // properties have to arrive as separate arguments rather than one spaced string.
        arguments.AddRange(
            sourceElement.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        arguments.Add("!");
        arguments.Add("videorate");
        arguments.Add("drop-only=true");
        arguments.Add(Argument("max-rate", Fps));

        // Cropping happens here rather than in managed code. When the portal already handed
        // us exactly the requested region there is nothing to crop.
        if (crop is { } region && sourceWidth > 0 && sourceHeight > 0
            && (region.Width != sourceWidth || region.Height != sourceHeight))
        {
            arguments.Add("!");
            arguments.Add("videocrop");
            arguments.Add(Argument("left", region.X));
            arguments.Add(Argument("top", region.Y));
            arguments.Add(Argument("right", Math.Max(0, sourceWidth - region.Right)));
            arguments.Add(Argument("bottom", Math.Max(0, sourceHeight - region.Bottom)));
        }

        arguments.Add("!");
        arguments.Add("videoscale");
        arguments.Add("add-borders=true");

        // Nearest-neighbour is both the cheapest and the right choice here: the frame is being
        // reduced to a per-zone average, so interpolation would only blur zone boundaries.
        arguments.Add("method=nearest-neighbour");
        arguments.Add("!");
        arguments.Add("videoconvert");
        arguments.Add("!");
        arguments.Add(string.Create(
            CultureInfo.InvariantCulture,
            // Keep the byte framing stable across source renegotiation. Square pixels make
            // videoscale letterbox a resized window rather than encode its new proportions
            // in pixel-aspect-ratio metadata that a raw byte pipe cannot carry.
            $"video/x-raw,format=BGRx,width={Width},height={Height},pixel-aspect-ratio=1/1,framerate={Fps}/1"));
        arguments.Add("!");
        arguments.Add("fdsink");
        arguments.Add("fd=1");
        // Capture timestamps/clock can reset during renegotiation. Deliver frames as soon as
        // they arrive; waiting for presentation time can hold up capture after a resize.
        arguments.Add("sync=false");
        arguments.Add("enable-last-sample=false");

        return arguments;
    }

    public Task<CapturedFrame> StartAsync(CancellationToken cancellationToken)
    {
        if (process is not null)
        {
            throw new InvalidOperationException("The frame source is already started.");
        }

        var startInfo = new ProcessStartInfo("gst-launch-1.0")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in BuildArguments())
        {
            startInfo.ArgumentList.Add(argument);
        }

        var launched = new Process { StartInfo = startInfo };

        try
        {
            launched.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            launched.Dispose();
            throw new CaptureException(
                "Could not run 'gst-launch-1.0'. Install GStreamer with the PipeWire plugin " +
                "(gst-plugin-pipewire).", ex);
        }

        process = launched;
        startedAt = Stopwatch.GetTimestamp();

        // Drained continuously so a chatty pipeline cannot fill the pipe and block. The reader
        // is handed over rather than the process, so the pump holds no disposable it does not
        // own, and DisposeAsync waits for it before tearing the process down.
        stderrPump = PumpStderrAsync(launched.StandardError);

        return ReadFrameAsync(cancellationToken);
    }

    /// <summary>
    /// Reads exactly one frame. Returns an empty frame when the pipeline has ended.
    /// </summary>
    public async Task<CapturedFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var current = process
            ?? throw new InvalidOperationException("StartAsync must be called before ReadFrameAsync.");

        var read = 0;
        while (read < FrameSizeBytes)
        {
            var count = await current.StandardOutput.BaseStream.ReadAsync(
                buffer.AsMemory(read, FrameSizeBytes - read), cancellationToken);

            if (count == 0)
            {
                // A short read means the pipeline stopped; a partial frame is never delivered.
                await ThrowIfPipelineFailedAsync(cancellationToken);
                return default;
            }

            read += count;
        }

        sequence++;
        return new CapturedFrame(buffer, Width, Height, sequence, Stopwatch.GetElapsedTime(startedAt));
    }

    private async Task ThrowIfPipelineFailedAsync(CancellationToken cancellationToken)
    {
        if (stderrPump is not null)
        {
            await stderrPump.WaitAsync(cancellationToken);
        }

        // Read the field after awaiting rather than capturing the process across the await,
        // so a concurrent DisposeAsync cannot leave a disposed handle in hand.
        if (process is not { HasExited: true } exited)
        {
            return;
        }

        if (exited.ExitCode != 0)
        {
            throw new CaptureException(
                $"The GStreamer pipeline exited with code {exited.ExitCode}." +
                (diagnostics.Length == 0 ? string.Empty : Environment.NewLine + diagnostics));
        }
    }

    private async Task PumpStderrAsync(StreamReader stderr)
    {
        try
        {
            while (await stderr.ReadLineAsync() is { } line)
            {
                // Bounded so a pipeline stuck in a warning loop cannot grow without limit.
                if (diagnostics.Length < 8192)
                {
                    diagnostics.AppendLine(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private static string Argument(string name, int value) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}={value}");

    public async ValueTask DisposeAsync()
    {
        if (process is null)
        {
            return;
        }

        var current = process;
        process = null;

        try
        {
            if (!current.HasExited)
            {
                current.Kill(entireProcessTree: true);
                await current.WaitForExitAsync();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
        }

        // The pump must finish reading stderr before the process, and with it the pipe, goes.
        if (stderrPump is not null)
        {
            await stderrPump.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            stderrPump = null;
        }

        current.Dispose();
    }
}

public sealed class CaptureException : Exception
{
    public CaptureException()
    {
    }

    public CaptureException(string message)
        : base(message)
    {
    }

    public CaptureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
