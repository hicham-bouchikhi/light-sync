using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb;

/// <summary>
/// Placeholder for an OpenRGB adapter over the OpenRGB SDK server protocol.
/// </summary>
public sealed class OpenRgbAdapter : ILightDevice
{
    private const string NotImplementedMessage =
        "The OpenRGB adapter is not implemented yet. Set device.adapter to 'nanoleaf' or 'fake'.";

    public string Name => "OpenRGB";

    public DeviceCapabilities Capabilities { get; } = new(
        MaximumZones: 0,
        SupportsStreaming: false,
        SupportsStaticColor: false,
        SupportsBrightness: false,
        SupportsEffects: false,
        SupportsPerZoneColor: false);

    public Task ConnectAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken) =>
        throw new NotSupportedException(NotImplementedMessage);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
