using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Devices.Nanoleaf;

public sealed class NanoleafAdapter : ILightDevice
{
    public string Name => "Nanoleaf";

    public DeviceCapabilities Capabilities { get; private set; } = new(
        MaximumZones: 0,
        SupportsStreaming: true,
        SupportsStaticColor: true,
        SupportsBrightness: true,
        SupportsEffects: true,
        SupportsPerZoneColor: true);

    public Task ConnectAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task DisconnectAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
