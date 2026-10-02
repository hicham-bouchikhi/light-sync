using System.Buffers.Binary;
using System.Net.Sockets;
using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb;

/// <summary>One owned SDK connection. Callers serialize operations; the reader handles notifications.</summary>
internal sealed class OpenRgbClient : IAsyncDisposable
{
    private const int MaximumPayloadBytes = 4 * 1024 * 1024;
    private readonly TcpClient tcp = new() { NoDelay = true };
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenSource writeDeadline = new();
    private readonly object responseGate = new();
    private readonly OpenRgbSettings settings;
    private NetworkStream? stream;
    private Task? reader;
    private PendingResponse? pending;
    private Exception? failure;
    private int generation;

    internal OpenRgbClient(OpenRgbSettings settings) => this.settings = settings;

    internal uint Version { get; private set; }

    internal int Generation => Volatile.Read(ref generation);

    internal int ControllerGeneration { get; private set; }

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
        settings.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.TimeoutMilliseconds);
        try
        {
            await tcp.ConnectAsync(settings.Host, settings.Port, timeout.Token);
            stream = tcp.GetStream();
            reader = ReadLoopAsync();
            await SendAsync(0, 50, "LightSync\0"u8.ToArray(), timeout.Token);
            var versionData = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(versionData, OpenRgbProtocol.MaximumVersion);
            var response = await RequestAsync(0, 40, versionData, timeout.Token);
            if (response.Length != 4 || BinaryPrimitives.ReadUInt32LittleEndian(response) < 1)
            {
                throw new DeviceException("LightSync requires OpenRGB SDK protocol 1 or later (OpenRGB 0.5+).");
            }

            Version = Math.Min(OpenRgbProtocol.MaximumVersion, BinaryPrimitives.ReadUInt32LittleEndian(response));
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeviceUnreachableException("OpenRGB SDK connection timed out. Enable its SDK server and check the host and port.", ex);
        }
        catch (SocketException ex)
        {
            throw new DeviceUnreachableException("Could not connect to OpenRGB. Start OpenRGB and enable its SDK server.", ex);
        }
    }

    internal async Task<IReadOnlyList<OpenRgbController>> GetControllersAsync(CancellationToken cancellationToken)
    {
        var initialGeneration = Generation;
        var response = await RequestAsync(0, 0, ReadOnlyMemory<byte>.Empty, cancellationToken);
        if (response.Length != 4 || BinaryPrimitives.ReadUInt32LittleEndian(response) > 4096)
        {
            throw new DeviceException("OpenRGB returned an invalid controller count.");
        }

        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(response);
        var controllers = new OpenRgbController[count];
        var versionData = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(versionData, Version);
        for (var index = 0; index < count; index++)
        {
            var data = await RequestAsync((uint)index, 1, versionData, cancellationToken);
            controllers[index] = OpenRgbProtocol.ParseController(data, index, Version);
        }

        EnsureCurrent(initialGeneration);
        ControllerGeneration = initialGeneration;
        return controllers;
    }

    internal void EnsureCurrent(int expectedGeneration)
    {
        if (Generation != expectedGeneration)
        {
            throw new DeviceException("OpenRGB's device list changed. Disconnect and reconnect before sending more colours.");
        }
    }

    internal async ValueTask WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken,
        int? expectedGeneration = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedGeneration is { } current)
        {
            EnsureCurrent(current);
        }

        lock (responseGate)
        {
            if (failure is { } error)
            {
                throw new DeviceUnreachableException("OpenRGB SDK connection ended. Reconnect the device.", error);
            }
        }

        var connection = stream ?? throw new DeviceException("Connect to OpenRGB before sending colours.");
        writeDeadline.CancelAfter(settings.TimeoutMilliseconds);
        using var registration = cancellationToken.Register(static state =>
            ((CancellationTokenSource)state!).Cancel(), writeDeadline);
        try
        {
            await connection.WriteAsync(packet, writeDeadline.Token);
        }
        catch (OperationCanceledException ex)
        {
            Fail(ex); // A partially written TCP packet cannot safely be retried.
            cancellationToken.ThrowIfCancellationRequested();
            throw new DeviceUnreachableException("Sending colours to OpenRGB timed out. Reconnect the device.", ex);
        }
        catch (IOException ex)
        {
            Fail(ex);
            throw new DeviceUnreachableException("OpenRGB SDK connection ended. Reconnect the device.", ex);
        }
        catch (ObjectDisposedException ex)
        {
            Fail(ex);
            throw new DeviceUnreachableException("OpenRGB SDK connection ended. Reconnect the device.", ex);
        }
        finally
        {
            writeDeadline.CancelAfter(Timeout.Infinite);
        }
    }

    internal async Task SendAsync(uint controller, uint command, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken, int? expectedGeneration = null)
    {
        var packet = new byte[OpenRgbProtocol.HeaderSize + payload.Length];
        OpenRgbProtocol.WriteHeader(packet, controller, command, payload.Length);
        payload.CopyTo(packet.AsMemory(OpenRgbProtocol.HeaderSize));
        await WritePacketAsync(packet, cancellationToken, expectedGeneration);
    }

    private async Task<byte[]> RequestAsync(uint controller, uint command, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var response = new PendingResponse(controller, command);
        lock (responseGate)
        {
            if (failure is { } error)
            {
                throw new DeviceUnreachableException("OpenRGB SDK connection ended.", error);
            }

            pending = response;
        }

        try
        {
            await SendAsync(controller, command, payload, cancellationToken);
            return await response.Completion.Task.WaitAsync(
                TimeSpan.FromMilliseconds(settings.TimeoutMilliseconds), cancellationToken);
        }
        catch (TimeoutException ex)
        {
            Fail(ex);
            throw new DeviceUnreachableException("OpenRGB SDK did not answer in time. Reconnect the device.", ex);
        }
        catch (OperationCanceledException ex)
        {
            Fail(ex);
            throw;
        }
        finally
        {
            if (response.Completion.Task.IsFaulted)
            {
                _ = response.Completion.Task.Exception;
            }
            lock (responseGate)
            {
                pending = null;
            }
        }
    }

    private async Task ReadLoopAsync()
    {
        var header = new byte[OpenRgbProtocol.HeaderSize];
        try
        {
            var connection = stream!;
            while (!stopping.IsCancellationRequested)
            {
                await connection.ReadExactlyAsync(header, stopping.Token);
                var controller = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                var command = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
                var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
                if (!header.AsSpan(0, 4).SequenceEqual("ORGB"u8) || length > MaximumPayloadBytes)
                {
                    throw new DeviceException("OpenRGB returned an invalid packet header.");
                }

                if (command == 100 && length == 0)
                {
                    Interlocked.Increment(ref generation);
                    continue;
                }

                var data = new byte[(int)length];
                await connection.ReadExactlyAsync(data, stopping.Token);
                lock (responseGate)
                {
                    if (pending is not { } response || response.Controller != controller || response.Command != command)
                    {
                        throw new DeviceException("OpenRGB returned an unexpected SDK response.");
                    }

                    response.Completion.TrySetResult(data);
                }
            }
        }
        catch (Exception ex) when (stopping.IsCancellationRequested
            && ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Closing this owned connection stops its reader.
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Fail(Exception error)
    {
        lock (responseGate)
        {
            failure ??= error;
            pending?.Completion.TrySetException(error is DeviceException ? error
                : new DeviceUnreachableException("OpenRGB SDK connection ended. Reconnect the device.", error));
        }

        tcp.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        tcp.Dispose();
        stream?.Dispose();
        if (reader is { } reading)
        {
            await reading;
        }

        stopping.Dispose();
        writeDeadline.Dispose();
    }

    private sealed record PendingResponse(uint Controller, uint Command)
    {
        internal TaskCompletionSource<byte[]> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
