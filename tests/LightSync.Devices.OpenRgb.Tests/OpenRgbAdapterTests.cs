using System.Globalization;
using System.Net;
using System.Net.Sockets;
using LightSync.Application;
using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb.Tests;

public sealed class OpenRgbAdapterTests
{
    private static readonly int[] ExpectedAddresses = [0, 1];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DiscoveryReportsEmptyServerWithoutSendingControlCommands()
    {
        await using var server = new SdkServerFixture { Controllers = [] };
        var devices = await DeviceDiscoveryService.DiscoverAsync("openrgb", server.Settings.ToDictionary(), Token);
        Assert.Empty(devices);
        Assert.Equal(0, server.ControlWriteCount);
    }

    [Fact]
    public async Task DiscoveryConnectionFailureIdentifiesEndpointAndServerSetup()
    {
        // Reserve an unused port without listening, so no local service is involved.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var settings = new OpenRgbSettings { Port = ((IPEndPoint)socket.LocalEndPoint!).Port };
        var error = await Assert.ThrowsAsync<DeviceUnreachableException>(() =>
            DeviceDiscoveryService.DiscoverAsync("openrgb", settings.ToDictionary(), Token));
        Assert.Contains(settings.Host + ":" + settings.Port.ToString(CultureInfo.InvariantCulture),
            error.Message, StringComparison.Ordinal);
        Assert.Contains("SDK server", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoveryDoesNotSendControlCommands()
    {
        await using var server = new SdkServerFixture();
        var devices = await OpenRgbDiscovery.DiscoverAsync(server.Settings, Token);
        var controller = Assert.Single(devices);
        Assert.Equal("Test GPU", controller.Name);
        Assert.Equal("gpu-serial", controller.Serial);
        Assert.Equal(2, controller.LedCount);
        Assert.True(controller.SupportsDirectColor);
        Assert.Equal(new OpenRgbZone("Fan ring", 0, 2), Assert.Single(controller.Zones));
        Assert.Equal(0, server.ControlWriteCount);
    }

    [Fact]
    public async Task ReportsDiscoveredLedCountAndFrameIndices()
    {
        await using var server = new SdkServerFixture();
        await using var adapter = new OpenRgbAdapter(server.Settings);
        Assert.Equal(0, adapter.Capabilities.MaximumZones);
        await adapter.ConnectAsync(Token);
        Assert.Equal(2, adapter.Capabilities.MaximumZones);
        Assert.True(adapter.Capabilities.SupportsStreaming);
        Assert.False(adapter.Capabilities.SupportsBrightness);
        Assert.Equal(ExpectedAddresses, adapter.ZoneAddresses);
        var mode = await server.ReadWriteAsync();
        Assert.Equal(1100U, mode.Command);
        Assert.Empty(mode.Payload);
    }

    [Fact]
    public async Task SendsExactRgbBytesAndBlacksOutUnusedLeds()
    {
        await using var server = new SdkServerFixture();
        await using var adapter = new OpenRgbAdapter(server.Settings);
        await adapter.ConnectAsync(Token);
        await server.ReadWriteAsync();
        await adapter.SendFrameAsync(new[] { new RgbColor(1, 2, 3), new RgbColor(255, 128, 64) }, Token);
        var frame = await server.ReadWriteAsync();
        Assert.Equal(1050U, frame.Command);
        Assert.Equal(new byte[] { 14, 0, 0, 0, 2, 0, 1, 2, 3, 0, 255, 128, 64, 0 }, frame.Payload);
        await adapter.SendFrameAsync(new[] { new RgbColor(10, 20, 30) }, Token);
        frame = await server.ReadWriteAsync();
        Assert.Equal(new byte[] { 14, 0, 0, 0, 2, 0, 10, 20, 30, 0, 0, 0, 0, 0 }, frame.Payload);
        await adapter.SetStaticColorAsync(RgbColor.Black, Token);
        frame = await server.ReadWriteAsync();
        Assert.Equal(new byte[] { 14, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, frame.Payload);
    }

    [Fact]
    public async Task SelectsControllerByMetadataRatherThanSavedIndex()
    {
        await using var server = new SdkServerFixture
        {
            Controllers = [SdkServerFixture.ControllerData("RAM", "ram-serial", "DIMM:0"),
                SdkServerFixture.ControllerData("GPU", "gpu-serial", "PCI:1")],
        };
        await using var adapter = new OpenRgbAdapter(server.Settings with { Serial = "gpu-serial" });
        await adapter.ConnectAsync(Token);
        Assert.Equal(1U, (await server.ReadWriteAsync()).Controller);
        await adapter.DisconnectAsync(Token);
        Array.Reverse(server.Controllers);
        await adapter.ConnectAsync(Token);
        Assert.Equal(0U, (await server.ReadWriteAsync()).Controller);
        await adapter.SetStaticColorAsync(new RgbColor(7, 8, 9), Token);
        Assert.Equal(0U, (await server.ReadWriteAsync()).Controller);
    }

    [Fact]
    public async Task SeparateProfilesCanControlDifferentComponents()
    {
        await using var server = new SdkServerFixture
        {
            Controllers = [SdkServerFixture.ControllerData("RAM", "ram", "DIMM:0"),
                SdkServerFixture.ControllerData("GPU", "gpu", "PCI:1")],
        };
        await using var ram = new OpenRgbAdapter(server.Settings with { Serial = "ram" });
        await using var gpu = new OpenRgbAdapter(server.Settings with { Serial = "gpu" });
        await ram.ConnectAsync(Token);
        await server.ReadWriteAsync();
        await gpu.ConnectAsync(Token);
        await server.ReadWriteAsync();
        await ram.DisconnectAsync(Token);
        await gpu.SetStaticColorAsync(new RgbColor(1, 2, 3), Token);
        Assert.Equal(1U, (await server.ReadWriteAsync()).Controller);
    }

    [Fact]
    public async Task RejectsAmbiguousAndMissingControllers()
    {
        await using var server = new SdkServerFixture
        {
            Controllers = [SdkServerFixture.ControllerData("RAM", "first", "DIMM:0"),
                SdkServerFixture.ControllerData("RAM", "second", "DIMM:1")],
        };
        await using var ambiguous = new OpenRgbAdapter(server.Settings with { ControllerName = "RAM" });
        var error = await Assert.ThrowsAsync<DeviceException>(() => ambiguous.ConnectAsync(Token));
        Assert.Contains("Several", error.Message, StringComparison.Ordinal);
        await using var missing = new OpenRgbAdapter(server.Settings with { Serial = "missing" });
        error = await Assert.ThrowsAsync<DeviceException>(() => missing.ConnectAsync(Token));
        Assert.Contains("No OpenRGB controller", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAnUnsupportedController()
    {
        await using var server = new SdkServerFixture
        {
            Controllers = [SdkServerFixture.ControllerData("RAM", "ram", "DIMM:0", direct: false)],
        };
        await using var adapter = new OpenRgbAdapter(server.Settings);
        await Assert.ThrowsAsync<DeviceException>(() => adapter.ConnectAsync(Token));
        Assert.False(adapter.Capabilities.SupportsStreaming);
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(2U)]
    [InlineData(3U)]
    [InlineData(4U)]
    [InlineData(6U)]
    public async Task NegotiatesSupportedControllerFormats(uint serverVersion)
    {
        await using var server = new SdkServerFixture
        {
            Version = serverVersion,
            Controllers = [SdkServerFixture.ControllerData("GPU", "gpu", "PCI:1", version: Math.Min(serverVersion, 4))],
        };
        await using var adapter = new OpenRgbAdapter(server.Settings);
        await adapter.ConnectAsync(Token);
        Assert.Equal(2, adapter.Capabilities.MaximumZones);
    }

    [Fact]
    public async Task RejectsADeviceListChangeDuringEnumeration()
    {
        await using var server = new SdkServerFixture { ChangeListDuringEnumeration = true };
        await using var adapter = new OpenRgbAdapter(server.Settings);
        var error = await Assert.ThrowsAsync<DeviceException>(() => adapter.ConnectAsync(Token));
        Assert.Contains("device list changed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopsWritesWhenAnEstablishedDeviceListChanges()
    {
        await using var server = new SdkServerFixture { ChangeListAfterColor = true };
        await using var client = new OpenRgbClient(server.Settings);
        await client.ConnectAsync(Token);
        await client.GetControllersAsync(Token);
        var originalGeneration = client.ControllerGeneration;
        var packet = new byte[30];
        OpenRgbProtocol.WriteHeader(packet, 0, 1050, 14);
        OpenRgbProtocol.WriteColors(packet, new RgbColor[2]);
        await client.WritePacketAsync(packet, Token, originalGeneration);
        await server.ReadWriteAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(1000);
        while (client.Generation == originalGeneration)
        {
            await Task.Delay(5, deadline.Token);
        }

        await Assert.ThrowsAsync<DeviceException>(async () =>
            await client.WritePacketAsync(packet, Token, originalGeneration));
        Assert.Equal(1, server.ControlWriteCount);
    }

    [Fact]
    public async Task TimesOutWhenSdkStopsResponding()
    {
        await using var server = new SdkServerFixture { StallCommand = 0 };
        await using var adapter = new OpenRgbAdapter(server.Settings with { TimeoutMilliseconds = 100 });
        await Assert.ThrowsAsync<DeviceUnreachableException>(() => adapter.ConnectAsync(Token));
    }

    [Fact]
    public async Task CancelsPendingSdkRequests()
    {
        await using var server = new SdkServerFixture { StallCommand = 0 };
        await using var adapter = new OpenRgbAdapter(server.Settings with { TimeoutMilliseconds = 5000 });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ConnectAsync(cancellation.Token));
    }

    [Fact]
    public async Task ReportsAnSdkDisconnectWithoutHanging()
    {
        await using var server = new SdkServerFixture { DisconnectCommand = 0 };
        await using var adapter = new OpenRgbAdapter(server.Settings);
        await Assert.ThrowsAsync<DeviceUnreachableException>(() => adapter.ConnectAsync(Token));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RejectsInvalidOrOversizedPackets(bool badMagic, bool oversized)
    {
        await using var server = new SdkServerFixture { BadMagic = badMagic, OversizedHeader = oversized };
        await using var adapter = new OpenRgbAdapter(server.Settings);
        await Assert.ThrowsAnyAsync<DeviceException>(() => adapter.ConnectAsync(Token));
    }

    [Fact]
    public async Task RejectsInvalidFramesBeforeWriting()
    {
        await using var server = new SdkServerFixture();
        await using var adapter = new OpenRgbAdapter(server.Settings);
        await Assert.ThrowsAsync<DeviceException>(() => adapter.SetStaticColorAsync(RgbColor.Black, Token));
        await adapter.ConnectAsync(Token);
        await Assert.ThrowsAsync<DeviceException>(() => adapter.SendFrameAsync(ReadOnlyMemory<RgbColor>.Empty, Token));
        await Assert.ThrowsAsync<DeviceException>(() => adapter.SendFrameAsync(new RgbColor[3], Token));
    }

    [Fact]
    public async Task SharedDiscoveryProducesReconnectableProfiles()
    {
        await using var server = new SdkServerFixture();
        var devices = await DeviceDiscoveryService.DiscoverAsync("openrgb", server.Settings.ToDictionary(), Token);
        var profile = Assert.Single(devices);
        Assert.Equal("gpu-serial", profile.Device.Settings["serial"]);
        var factory = new DeviceAdapterFactory();
        Assert.True(Assert.Single(factory.AvailableAdapters, a => a.Id == "openrgb").IsImplemented);
        await using var adapter = factory.Create(profile.Device.Adapter, profile.Device.Settings);
        await adapter.ConnectAsync(Token);
        Assert.Equal("Test GPU", adapter.Name);
    }
}
