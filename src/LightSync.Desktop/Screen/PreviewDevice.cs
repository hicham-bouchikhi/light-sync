using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Desktop.Screen;

/// <summary>A preview sink that discards output instead of recording an unbounded frame history.</summary>
internal sealed class PreviewDevice(int zoneCount) : ILightDevice
{
    public string Name => "Preview only";

    public DeviceCapabilities Capabilities { get; } = new(zoneCount, true, true, false, false, true);

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
