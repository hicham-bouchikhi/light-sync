using LightSync.Core.Colors;

namespace LightSync.Core.Devices;

public interface ILightDevice : IAsyncDisposable
{
    string Name { get; }

    DeviceCapabilities Capabilities { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task SendFrameAsync(
        ReadOnlyMemory<RgbColor> colors,
        CancellationToken cancellationToken);

    Task SetStaticColorAsync(
        RgbColor color,
        CancellationToken cancellationToken);
}
