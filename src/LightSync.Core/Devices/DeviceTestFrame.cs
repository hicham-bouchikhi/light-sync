using LightSync.Core.Colors;

namespace LightSync.Core.Devices;

/// <summary>Exact RGB frame used for manual tests. No smoothing, gamma, HSV conversion or remapping.</summary>
public sealed class DeviceTestFrame
{
    private readonly RgbColor[] colors;

    public DeviceTestFrame(int zoneCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoneCount);
        colors = new RgbColor[zoneCount];
    }

    public ReadOnlyMemory<RgbColor> Colors => colors;

    public void SetAll(RgbColor color) => colors.AsSpan().Fill(color);

    public void SetZone(int index, RgbColor color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, colors.Length);
        colors[index] = color;
    }

    public void Isolate(int index, RgbColor color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, colors.Length);
        colors.AsSpan().Clear();
        colors[index] = color;
    }
}
