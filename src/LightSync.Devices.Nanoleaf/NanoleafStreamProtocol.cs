using System.Buffers.Binary;
using LightSync.Core.Colors;

namespace LightSync.Devices.Nanoleaf;

/// <summary>
/// Encodes external-control ("extControl") streaming frames.
/// </summary>
/// <remarks>
/// Layout verified against a real NL72K4 floor lamp on firmware 4.0.11. Version 2 is
/// big-endian throughout:
/// <code>
///   offset  size  field
///   0       2     panel count
///   then per panel:
///   +0      2     panel id
///   +2      1     red
///   +3      1     green
///   +4      1     blue
///   +5      1     white       (always 0; the strip has no white channel)
///   +6      2     transition time
/// </code>
/// The device validates the whole frame: if it contains a single out-of-range panel id it is
/// discarded silently and the previous frame stays on screen. Sending more panels than the
/// device has is therefore not a partial success but a total loss of the update.
/// </remarks>
public static class NanoleafStreamProtocol
{
    /// <summary>Fixed UDP port for stream control.</summary>
    public const int StreamPort = 60222;

    public const int HeaderSizeBytes = 2;

    public const int PanelRecordSizeBytes = 8;

    /// <summary>The version string the device expects when enabling external control.</summary>
    public const string Version = "v2";

    public static int FrameSizeBytes(int panelCount) =>
        HeaderSizeBytes + (panelCount * PanelRecordSizeBytes);

    /// <summary>
    /// Writes a frame into <paramref name="destination"/> and returns the number of bytes
    /// written. Allocation-free so it can be called every frame.
    /// </summary>
    public static int WriteFrame(
        Span<byte> destination,
        ReadOnlySpan<int> panelIds,
        ReadOnlySpan<RgbColor> colors,
        ushort transitionTime = 1)
    {
        if (panelIds.Length != colors.Length)
        {
            throw new ArgumentException(
                $"Got {panelIds.Length} panel ids but {colors.Length} colours; they must match.",
                nameof(colors));
        }

        var required = FrameSizeBytes(panelIds.Length);
        if (destination.Length < required)
        {
            throw new ArgumentException(
                $"Frame needs {required} bytes but the buffer holds {destination.Length}.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)panelIds.Length);

        var offset = HeaderSizeBytes;
        for (var i = 0; i < panelIds.Length; i++)
        {
            var panel = panelIds[i];
            if (panel is < 0 or > ushort.MaxValue)
            {
                throw new ArgumentException(
                    $"Panel id {panel} does not fit in the protocol's 16-bit field.", nameof(panelIds));
            }

            var color = colors[i];

            BinaryPrimitives.WriteUInt16BigEndian(destination[offset..], (ushort)panel);
            destination[offset + 2] = color.R;
            destination[offset + 3] = color.G;
            destination[offset + 4] = color.B;
            destination[offset + 5] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(destination[(offset + 6)..], transitionTime);

            offset += PanelRecordSizeBytes;
        }

        return offset;
    }
}
