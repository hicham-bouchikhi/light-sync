using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace LightSync.Devices.OpenRgb.Tests;

/// <summary>An actual TCP peer with independent packet encoding, including split responses.</summary>
internal sealed class SdkServerFixture : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new(TimeSpan.FromSeconds(15));
    private readonly List<Task> connections = [];
    private readonly Channel<SdkPacket> writes = Channel.CreateUnbounded<SdkPacket>();
    private readonly Task accepting;
    private int controlWriteCount;

    internal SdkServerFixture()
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = AcceptAsync();
    }

    internal int Port { get; }

    internal uint Version { get; init; } = 4;

    internal byte[][] Controllers { get; set; } = [ControllerData("Test GPU", "gpu-serial", "PCI:1")];

    internal uint? StallCommand { get; init; }

    internal uint? DisconnectCommand { get; init; }

    internal bool BadMagic { get; init; }

    internal bool OversizedHeader { get; init; }

    internal bool ChangeListDuringEnumeration { get; init; }

    internal bool ChangeListAfterColor { get; init; }

    internal int ControlWriteCount => Volatile.Read(ref controlWriteCount);

    internal OpenRgbSettings Settings => new() { Port = Port, TimeoutMilliseconds = 1000 };

    internal async Task<SdkPacket> ReadWriteAsync() =>
        await writes.Reader.ReadAsync(stopping.Token);

    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var connection = await listener.AcceptTcpClientAsync(stopping.Token);
                connections.Add(HandleAsync(connection));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }

    private async Task HandleAsync(TcpClient connection)
    {
        using (connection)
        {
            var stream = connection.GetStream();
            var header = new byte[16];
            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    await stream.ReadExactlyAsync(header, stopping.Token);
                    Assert.Equal("ORGB"u8.ToArray(), header.AsSpan(0, 4).ToArray());
                    var controller = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                    var command = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
                    var payload = new byte[(int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12))];
                    await stream.ReadExactlyAsync(payload, stopping.Token);
                    if (command == DisconnectCommand)
                    {
                        return;
                    }
                    if (command == StallCommand)
                    {
                        continue;
                    }

                    byte[]? response = null;
                    switch (command)
                    {
                        case 50:
                            Assert.Equal("LightSync\0"u8.ToArray(), payload);
                            break;
                        case 40:
                            Assert.Equal(new byte[] { 4, 0, 0, 0 }, payload);
                            response = UInt32Data(Version);
                            break;
                        case 0:
                            response = UInt32Data((uint)Controllers.Length);
                            break;
                        case 1:
                            Assert.Equal(UInt32Data(Math.Min(Version, 4)), payload);
                            if (ChangeListDuringEnumeration)
                            {
                                await SendResponseAsync(stream, 0, 100, [], stopping.Token);
                            }
                            response = Controllers[(int)controller];
                            break;
                        case 1050:
                        case 1100:
                            Interlocked.Increment(ref controlWriteCount);
                            await writes.Writer.WriteAsync(new SdkPacket(controller, command, payload), stopping.Token);
                            if (command == 1050 && ChangeListAfterColor)
                            {
                                await SendResponseAsync(stream, 0, 100, [], stopping.Token);
                            }
                            break;
                        default:
                            throw new InvalidOperationException($"Unexpected SDK command {command}.");
                    }

                    if (response is not null)
                    {
                        await SendResponseAsync(stream, controller, command, response, stopping.Token);
                    }
                }
            }
            catch (IOException)
            {
                // Client disconnected, including timeout and cancellation tests.
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
            }
        }
    }

    private async Task SendResponseAsync(NetworkStream stream, uint controller, uint command, byte[] payload,
        CancellationToken cancellationToken)
    {
        var packet = new byte[16 + payload.Length];
        (BadMagic ? "BAD!"u8 : "ORGB"u8).CopyTo(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), controller);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), command);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), OversizedHeader ? uint.MaxValue : (uint)payload.Length);
        payload.CopyTo(packet, 16);
        // Every header and payload can span multiple TCP reads.
        for (var offset = 0; offset < packet.Length; offset += 7)
        {
            await stream.WriteAsync(packet.AsMemory(offset, Math.Min(7, packet.Length - offset)), cancellationToken);
        }
    }

    internal static byte[] UInt32Data(uint value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return data;
    }

    internal static byte[] ControllerData(string name, string serial, string location, int leds = 2,
        uint version = 4, bool direct = true)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0U); // Total size, patched below.
        writer.Write(2U); // GPU.
        WriteString(writer, name);
        WriteString(writer, "Test vendor");
        WriteString(writer, "SDK fixture");
        WriteString(writer, "1.0");
        WriteString(writer, serial);
        WriteString(writer, location);
        writer.Write((ushort)1); // Mode count.
        writer.Write(0U); // Active mode.
        WriteString(writer, direct ? "Direct" : "Static");
        writer.Write(0U); // Vendor mode value.
        writer.Write(32U); // Per-LED colour flag.
        writer.Write(0U); // Speed bounds.
        writer.Write(0U);
        if (version >= 3)
        {
            writer.Write(0U); // Brightness bounds.
            writer.Write(100U);
        }
        writer.Write(0U); // Colour count bounds.
        writer.Write(0U);
        writer.Write(0U); // Speed.
        if (version >= 3)
        {
            writer.Write(100U); // Brightness.
        }
        writer.Write(0U); // Direction.
        writer.Write(1U); // Per-LED colours.
        writer.Write((ushort)0); // Mode colours.
        writer.Write((ushort)1); // Zone count.
        WriteString(writer, "Fan ring");
        writer.Write(1U); // Linear zone.
        writer.Write((uint)leds); // Minimum, maximum and current size.
        writer.Write((uint)leds);
        writer.Write((uint)leds);
        writer.Write((ushort)0); // No matrix.
        if (version >= 4)
        {
            writer.Write((ushort)0); // No segments.
        }
        writer.Write((ushort)leds);
        for (var i = 0; i < leds; i++)
        {
            WriteString(writer, $"LED {i}");
            writer.Write((uint)(1000 + i)); // Vendor values deliberately differ from frame indices.
        }
        writer.Write((ushort)leds);
        for (var i = 0; i < leds; i++)
        {
            writer.Write(0U);
        }

        var data = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)data.Length);
        return data;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + '\0');
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        await accepting;
        listener.Dispose();
        await Task.WhenAll(connections);
        stopping.Dispose();
    }
}

internal sealed record SdkPacket(uint Controller, uint Command, byte[] Payload);
