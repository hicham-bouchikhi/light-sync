using LightSync.Core.Colors;

namespace LightSync.Core.Devices;

/// <summary>
/// An in-memory device that records what it was asked to display. Lets the whole pipeline
/// be exercised, and asserted on, without any hardware.
/// </summary>
public sealed class FakeDevice : ILightDevice
{
    private readonly List<RgbColor[]> frames = [];
    private readonly Lock gate = new();

    public FakeDevice(int maximumZones = 24)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumZones);

        Capabilities = new DeviceCapabilities(
            MaximumZones: maximumZones,
            SupportsStreaming: true,
            SupportsStaticColor: true,
            SupportsBrightness: true,
            SupportsEffects: false,
            SupportsPerZoneColor: true);
    }

    public string Name => "Fake device";

    public DeviceCapabilities Capabilities { get; }

    public bool IsConnected { get; private set; }

    public int FrameCount
    {
        get
        {
            lock (gate)
            {
                return frames.Count;
            }
        }
    }

    public RgbColor? LastStaticColor { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConnected)
        {
            throw new DeviceException("SendFrameAsync called before ConnectAsync.");
        }

        if (colors.Length > Capabilities.MaximumZones)
        {
            throw new DeviceException(
                $"Frame has {colors.Length} zones but the device supports at most " +
                $"{Capabilities.MaximumZones}.");
        }

        lock (gate)
        {
            frames.Add(colors.ToArray());
        }

        return Task.CompletedTask;
    }

    public Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConnected)
        {
            throw new DeviceException("SetStaticColorAsync called before ConnectAsync.");
        }

        LastStaticColor = color;
        return Task.CompletedTask;
    }

    /// <summary>Copies out every frame received so far.</summary>
    public IReadOnlyList<RgbColor[]> SnapshotFrames()
    {
        lock (gate)
        {
            return [.. frames];
        }
    }

    public RgbColor[]? GetLastFrame()
    {
        lock (gate)
        {
            return frames.Count == 0 ? null : frames[^1];
        }
    }

    public void ClearFrames()
    {
        lock (gate)
        {
            frames.Clear();
        }
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
