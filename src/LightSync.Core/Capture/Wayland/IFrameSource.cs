namespace LightSync.Core.Capture.Wayland;

internal interface IFrameSource : IAsyncDisposable
{
    Task<CapturedFrame> StartAsync(CancellationToken cancellationToken);

    Task<CapturedFrame> ReadFrameAsync(CancellationToken cancellationToken);
}
