using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Devices.Wled;

/// <summary>
/// Placeholder for a WLED adapter over the DDP / UDP realtime protocol. Present so the
/// adapter surface is complete and <c>list-adapters</c> can advertise it as planned.
/// </summary>
public sealed class WledAdapter : ILightDevice
{
    private const string NotImplementedMessage =
        "The WLED adapter is not implemented yet. Set device.adapter to 'nanoleaf' or 'fake'.";

    public string Name => "WLED";

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
