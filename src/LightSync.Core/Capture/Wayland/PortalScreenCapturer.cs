using System.Globalization;

namespace LightSync.Core.Capture.Wayland;

/// <summary>
/// Screen capture on Wayland: the desktop portal chooses the source, PipeWire carries it, and
/// GStreamer crops and downscales it before it reaches managed code.
/// </summary>
public sealed class PortalScreenCapturer(
    string? restoreToken = null,
    Action<PortalSelection>? onSelected = null,
    bool useSelectionBounds = false) : IScreenCapture
{
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(10);

    private PortalScreenCastSession? session;
    private GStreamerFrameSource? source;

    /// <summary>The selection the portal returned, available after <see cref="StartAsync"/>.</summary>
    public PortalSelection? Selection { get; private set; }

    public async Task<CapturedFrame> StartAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session is not null)
        {
            throw new InvalidOperationException("The capturer is already started.");
        }

        if (!request.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(request));
        }

        var portal = new PortalScreenCastSession();
        session = portal;

        var selection = await portal.OpenAsync(restoreToken, cancellationToken);
        Selection = selection;
        onSelected?.Invoke(selection);

        // A region selection already arrives cropped, so cropping again would be wrong as well
        // as wasteful. Only a whole-screen selection needs the configured rectangle applied,
        // and even then GStreamer does it rather than managed code.
        CaptureArea? crop = selection.IsPreCropped || useSelectionBounds ? null : request.Area;

        // Never ask for more pixels than the stream has; the request was sized from the saved
        // rectangle, which may not match what the user has just picked.
        var targetWidth = Math.Min(request.TargetWidth, selection.Width);
        var targetHeight = Math.Min(request.TargetHeight, selection.Height);
        if (useSelectionBounds)
        {
            // The picker may return a portrait monitor or a wide region. Fit the whole
            // selected source into the preview budget without stretching its aspect ratio.
            var scale = Math.Min((double)targetWidth / selection.Width, (double)targetHeight / selection.Height);
            targetWidth = Math.Max(1, (int)(selection.Width * scale));
            targetHeight = Math.Max(1, (int)(selection.Height * scale));
        }

        source = new GStreamerFrameSource(
            sourceElement: string.Create(
                CultureInfo.InvariantCulture,
                $"pipewiresrc fd={portal.PipeWireRemote!.DangerousGetHandle()} path={selection.NodeId} do-timestamp=true keepalive-time=1000"),
            targetWidth: Math.Max(1, targetWidth),
            targetHeight: Math.Max(1, targetHeight),
            fps: request.Fps,
            crop: crop,
            sourceWidth: selection.Width,
            sourceHeight: selection.Height);

        try
        {
            // A successful portal selection should make its PipeWire node readable almost
            // immediately. Without a timeout, a broken portal/PipeWire hand-off leaves setup
            // waiting forever with no indication that the area was already accepted.
            return await source.StartAsync(cancellationToken).WaitAsync(FirstFrameTimeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new CaptureException(
                "The desktop accepted the selected area, but no capture frame arrived within " +
                $"{FirstFrameTimeout.TotalSeconds:0} seconds. Check that PipeWire and the " +
                "desktop portal backend are running, then try select-area again.", ex);
        }
    }

    public Task<CapturedFrame> ReadFrameAsync(CancellationToken cancellationToken) =>
        source is null
            ? throw new InvalidOperationException("StartAsync must be called before ReadFrameAsync.")
            : source.ReadFrameAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Order matters: stop consuming before closing the session, since closing it destroys
        // the PipeWire node underneath the reader.
        if (source is not null)
        {
            await source.DisposeAsync();
            source = null;
        }

        if (session is not null)
        {
            await session.DisposeAsync();
            session = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
