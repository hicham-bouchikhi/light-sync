using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb;

/// <summary>
/// One selected controller, driven through OpenRGB's local or remote SDK server.
/// </summary>
public sealed class OpenRgbAdapter : ILightDevice, IZoneAddressProvider
{
    private readonly OpenRgbSettings settings;
    private readonly SemaphoreSlim gate = new(1, 1);
    private OpenRgbClient? client;
    private byte[] packet = [];
    private RgbColor[] frameColors = [];
    private int[] addresses = [];
    private int generation;
    private bool disposed;

    public OpenRgbAdapter(OpenRgbSettings? settings = null) => this.settings = settings ?? new();

    public string Name { get; private set; } = "OpenRGB";

    public DeviceCapabilities Capabilities { get; private set; } = UnconnectedCapabilities;

    public OpenRgbController? Controller { get; private set; }

    public IReadOnlyList<int> ZoneAddresses => addresses;

    public string ZoneAddressSource => "OpenRGB controller LED indices, in SDK order.";

    private static DeviceCapabilities UnconnectedCapabilities => new(0, false, false, false, false, false);

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (client is not null)
            {
                return;
            }

            client = new OpenRgbClient(settings);
            var connection = client;
            try
            {
                await connection.ConnectAsync(cancellationToken);
                var controllers = await connection.GetControllersAsync(cancellationToken);
                var matches = controllers.Where(settings.Matches).ToArray();
                if (matches.Length != 1)
                {
                    throw new DeviceException(matches.Length == 0
                        ? "No OpenRGB controller matches this profile. Discover devices and check controllerName, serial and location."
                        : "Several OpenRGB controllers match this profile. Set controllerName, serial or location to select exactly one.");
                }

                var controller = matches[0];
                if (!controller.SupportsDirectColor || controller.LedCount == 0)
                {
                    throw new DeviceException($"{controller.Name} does not expose a per-LED Direct mode with addressable LEDs in OpenRGB.");
                }

                generation = connection.ControllerGeneration;
                await connection.SendAsync((uint)controller.Index, 1100, ReadOnlyMemory<byte>.Empty,
                    cancellationToken, generation);
                frameColors = new RgbColor[controller.LedCount];
                packet = new byte[OpenRgbProtocol.HeaderSize + 6 + (controller.LedCount * 4)];
                addresses = new int[controller.LedCount];
                for (var i = 0; i < addresses.Length; i++)
                {
                    addresses[i] = i;
                }

                OpenRgbProtocol.WriteHeader(packet, (uint)controller.Index, 1050, packet.Length - OpenRgbProtocol.HeaderSize);
                Controller = controller;
                Name = controller.Name;
                Capabilities = new(controller.LedCount, true, true, false, false, controller.LedCount > 1);
            }
            catch
            {
                await client.DisposeAsync();
                client = null;
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var connection = client ?? throw new DeviceException("Connect to OpenRGB before sending colours.");
            if (colors.IsEmpty || colors.Length > frameColors.Length)
            {
                throw new DeviceException($"OpenRGB frame must contain between 1 and {frameColors.Length} colours.");
            }

            colors.Span.CopyTo(frameColors);
            frameColors.AsSpan(colors.Length).Clear();
            OpenRgbProtocol.WriteColors(packet, frameColors);
            await connection.WritePacketAsync(packet, cancellationToken, generation);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var connection = client ?? throw new DeviceException("Connect to OpenRGB before sending colours.");
            frameColors.AsSpan().Fill(color);
            OpenRgbProtocol.WriteColors(packet, frameColors);
            await connection.WritePacketAsync(packet, cancellationToken, generation);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (client is not null)
            {
                await client.DisposeAsync();
                client = null;
            }

            Controller = null;
            Name = "OpenRGB";
            Capabilities = UnconnectedCapabilities;
            addresses = [];
            frameColors = [];
            packet = [];
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await gate.WaitAsync();
        try
        {
            if (client is not null)
            {
                await client.DisposeAsync();
                client = null;
            }

            Controller = null;
            Capabilities = UnconnectedCapabilities;
            addresses = [];
            frameColors = [];
            packet = [];
            disposed = true;
        }
        finally
        {
            gate.Release();
        }
        gate.Dispose();
    }
}
