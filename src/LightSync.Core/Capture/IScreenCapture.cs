namespace LightSync.Core.Capture;

/// <summary>
/// A capture backend. <see cref="StartAsync"/> negotiates the source and returns the first
/// frame, which is what proves the negotiated format actually matches the request;
/// <see cref="ReadFrameAsync"/> then yields each subsequent frame.
/// </summary>
public interface IScreenCapture : IAsyncDisposable
{
    Task<CapturedFrame> StartAsync(
        CaptureRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the next frame, or an empty frame once the source has ended.
    /// </summary>
    Task<CapturedFrame> ReadFrameAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
