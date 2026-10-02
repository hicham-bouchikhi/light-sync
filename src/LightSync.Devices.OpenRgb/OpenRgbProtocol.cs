using System.Buffers.Binary;
using System.Text;
using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb;

/// <summary>Little-endian SDK protocol 1–4. Higher-version servers negotiate down to 4.</summary>
internal static class OpenRgbProtocol
{
    internal const uint MaximumVersion = 4;
    internal const int HeaderSize = 16;

    internal static void WriteHeader(Span<byte> buffer, uint controller, uint command, int payloadSize)
    {
        "ORGB"u8.CopyTo(buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], controller);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..], command);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..], (uint)payloadSize);
    }

    internal static void WriteColors(Span<byte> packet, ReadOnlySpan<RgbColor> colors)
    {
        var payload = packet[HeaderSize..];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)payload.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[4..], (ushort)colors.Length);
        for (var i = 0; i < colors.Length; i++)
        {
            var offset = 6 + (i * 4);
            payload[offset] = colors[i].R;
            payload[offset + 1] = colors[i].G;
            payload[offset + 2] = colors[i].B;
            payload[offset + 3] = 0;
        }
    }

    internal static OpenRgbController ParseController(ReadOnlySpan<byte> data, int index, uint version)
    {
        var reader = new PacketReader(data);
        if (reader.ReadUInt32() != data.Length)
        {
            throw new DeviceException("OpenRGB controller data has an invalid size.");
        }

        reader.Skip(4); // Device type.
        var name = reader.ReadString();
        var vendor = reader.ReadString(); // Protocol 1 and later.
        reader.ReadString(); // Description.
        reader.ReadString(); // Firmware version.
        var serial = reader.ReadString();
        var location = reader.ReadString();
        var modeCount = reader.ReadUInt16();
        reader.Skip(4); // Active mode.
        var supportsDirect = false;
        for (var i = 0; i < modeCount; i++)
        {
            var modeName = reader.ReadString();
            reader.Skip(4); // Vendor mode value.
            var flags = reader.ReadUInt32();
            reader.Skip(8); // Speed bounds.
            if (version >= 3)
            {
                reader.Skip(8); // Brightness bounds.
            }

            reader.Skip(12); // Colour bounds and current speed.
            if (version >= 3)
            {
                reader.Skip(4); // Current brightness.
            }

            reader.Skip(4); // Direction.
            var colorMode = reader.ReadUInt32();
            reader.Skip(reader.ReadUInt16() * 4);
            supportsDirect |= string.Equals(modeName, "Direct", StringComparison.Ordinal)
                && (flags & (1U << 5)) != 0 && colorMode == 1;
        }

        var zoneCount = reader.ReadUInt16();
        var zones = new OpenRgbZone[zoneCount];
        var firstLed = 0;
        for (var i = 0; i < zoneCount; i++)
        {
            var zoneName = reader.ReadString();
            reader.Skip(12); // Type and LED count bounds.
            var count = reader.ReadUInt32();
            if (count > ushort.MaxValue || firstLed + count > ushort.MaxValue)
            {
                throw new DeviceException("OpenRGB zone LED count exceeds the protocol limit.");
            }

            zones[i] = new OpenRgbZone(zoneName, firstLed, (int)count);
            firstLed += (int)count;
            reader.Skip(reader.ReadUInt16()); // Matrix map.
            if (version >= 4)
            {
                var segmentCount = reader.ReadUInt16();
                for (var segment = 0; segment < segmentCount; segment++)
                {
                    reader.ReadString();
                    reader.Skip(12); // Segment type, start and length.
                }
            }
        }

        var ledCount = reader.ReadUInt16();
        for (var i = 0; i < ledCount; i++)
        {
            reader.ReadString();
            reader.Skip(4); // Vendor LED value is not a frame index.
        }

        var colorCount = reader.ReadUInt16();
        reader.Skip(colorCount * 4);
        if (!reader.IsEmpty || colorCount != ledCount || firstLed != ledCount)
        {
            throw new DeviceException("OpenRGB controller has an inconsistent LED layout.");
        }

        return new OpenRgbController(index, name, vendor, serial, location, ledCount, supportsDirect, zones);
    }

    private ref struct PacketReader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> remaining = data;

        public readonly bool IsEmpty => remaining.IsEmpty;

        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public void Skip(int count) => Take(count);

        public string ReadString()
        {
            var bytes = Take(ReadUInt16());
            if (bytes.IsEmpty || bytes[^1] != 0)
            {
                throw new DeviceException("OpenRGB returned an invalid string.");
            }

            return Encoding.UTF8.GetString(bytes[..^1]);
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count > remaining.Length)
            {
                throw new DeviceException("OpenRGB controller data is truncated.");
            }

            var value = remaining[..count];
            remaining = remaining[count..];
            return value;
        }
    }
}
