using System.Net;
using System.Net.Sockets;
using LightSync.Core.Colors;
using LightSync.Core.Devices;

namespace LightSync.Devices.Nanoleaf;

/// <summary>
/// Drives a Nanoleaf device over the local API: HTTP for setup and static colour, and a
/// persistent UDP socket for per-frame streaming.
/// </summary>
public sealed class NanoleafAdapter : ILightDevice, IZoneAddressProvider, IBrightnessControl
{
    /// <summary>
    /// Fallback LED count for models whose <c>panelLayout</c> endpoint is unavailable. The
    /// Matter Essentials line answers that endpoint with HTTP 500, and a NL72K4 floor lamp was
    /// measured to have exactly this many addressable LEDs.
    /// </summary>
    public const int DefaultEssentialsLedCount = 24;

    private readonly NanoleafSettings settings;
    private readonly Func<NanoleafSettings, string, NanoleafApi> apiFactory;
    private NanoleafApi? api;
    private Socket? stream;
    private IPEndPoint? streamEndpoint;
    private int[] panelIds = [];
    private byte[] frameBuffer = [];
    private int streamingBrightness;

    public NanoleafAdapter(
        NanoleafSettings? settings = null,
        Func<NanoleafSettings, string, NanoleafApi>? apiFactory = null)
    {
        this.settings = settings ?? new NanoleafSettings();
        streamingBrightness = this.settings.BrightnessPercent;
        this.apiFactory = apiFactory ?? ((s, token) => new NanoleafApi(s, token));
        Capabilities = UnknownCapabilities;
    }

    private static DeviceCapabilities UnknownCapabilities { get; } = new(
        MaximumZones: 0,
        SupportsStreaming: true,
        SupportsStaticColor: true,
        SupportsBrightness: true,
        SupportsEffects: true,
        SupportsPerZoneColor: true);

    public string Name { get; private set; } = "Nanoleaf";

    public DeviceCapabilities Capabilities { get; private set; }

    public bool IsStreaming => stream is not null;

    public int BrightnessPercent { get; private set; } = 100;

    public IReadOnlyList<int> ZoneAddresses => panelIds;

    public string ZoneAddressSource { get; private set; } = "Not connected";

    /// <summary>Device details, available after <see cref="ConnectAsync"/>.</summary>
    public NanoleafDeviceInfo? DeviceInfo { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var resolved = await ResolveSettingsAsync(cancellationToken);
        var problems = resolved.Validate();
        if (problems.Count > 0)
        {
            throw new DeviceException(string.Join(Environment.NewLine, problems));
        }
        var token = NanoleafAuthentication.ResolveToken(resolved, resolved.SecretsFilePath);

        api = apiFactory(resolved, token);

        var info = await api.GetDeviceInfoAsync(cancellationToken);
        DeviceInfo = info;
        BrightnessPercent = info.State?.On?.Value == false ? 0 : Math.Clamp(info.State?.Brightness?.Value ?? 100, 0, 100);
        Name = string.IsNullOrWhiteSpace(info.Name) ? "Nanoleaf" : info.Name;

        VerifyModel(resolved, info);

        panelIds = await ResolvePanelIdsAsync(resolved, cancellationToken);
        frameBuffer = new byte[NanoleafStreamProtocol.FrameSizeBytes(panelIds.Length)];

        Capabilities = new DeviceCapabilities(
            MaximumZones: panelIds.Length,
            SupportsStreaming: true,
            SupportsStaticColor: true,
            SupportsBrightness: true,
            SupportsEffects: true,
            SupportsPerZoneColor: panelIds.Length > 1);
    }

    /// <summary>
    /// Switches the device into external-control mode and opens the streaming socket. Called
    /// lazily on the first frame so static-colour use never disturbs the current effect.
    /// </summary>
    public async Task StartStreamingAsync(CancellationToken cancellationToken)
    {
        var client = api ?? throw new DeviceException("ConnectAsync must be called first.");

        if (stream is not null)
        {
            return;
        }

        // Black-out uses a brightness of 1 with power off on Essentials. Restore the chosen
        // master level before streaming so a new session cannot inherit that 1% brightness.
        await client.SetBrightnessAsync(streamingBrightness, cancellationToken);
        BrightnessPercent = streamingBrightness;
        if (streamingBrightness == 0)
        {
            return;
        }

        var control = await client.TryEnableExternalControlAsync(
            NanoleafStreamProtocol.Version, cancellationToken)
            ?? throw new DeviceException(
                "The device refused external-control streaming, so it cannot follow audio or the screen. " +
                "Use a static colour instead.");

        var host = string.IsNullOrWhiteSpace(control.IpAddress) ? client.Host : control.IpAddress;
        var port = control.Port is > 0 ? control.Port.Value : NanoleafStreamProtocol.StreamPort;

        if (!IPAddress.TryParse(host, out var address))
        {
            var resolved = await Dns.GetHostAddressesAsync(host, cancellationToken);
            address = Array.Find(resolved, a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new DeviceUnreachableException($"Could not resolve '{host}' to an IPv4 address.");
        }

        streamEndpoint = new IPEndPoint(address, port);

        // Deliberately an unconnected socket. On a connected UDP socket the kernel reports a
        // peer's ICMP port-unreachable as ECONNREFUSED on the *next* send, so a single frame
        // sent while the device had not yet opened the port would poison the socket and kill
        // the run. Sending to an explicit endpoint keeps the stream fire-and-forget.
        stream = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    }

    public async Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
    {
        if (panelIds.Length == 0)
        {
            throw new DeviceException("The device reported no addressable LEDs.");
        }

        // The device discards any frame mentioning an unknown panel id, so sending more zones
        // than it has would silently freeze the lamp rather than partially update it.
        if (colors.Length != panelIds.Length)
        {
            throw new DeviceException(
                $"Frame carries {colors.Length} colours but the device has {panelIds.Length} LEDs; " +
                "send exactly one colour per LED.");
        }

        if (streamingBrightness == 0 && BrightnessPercent == 0)
        {
            return;
        }

        if (stream is null)
        {
            await StartStreamingAsync(cancellationToken);
        }

        if (streamingBrightness == 0)
        {
            return;
        }

        var written = NanoleafStreamProtocol.WriteFrame(
            frameBuffer, panelIds, colors.Span[..panelIds.Length]);

        try
        {
            await stream!.SendToAsync(
                frameBuffer.AsMemory(0, written), SocketFlags.None, streamEndpoint!, cancellationToken);
        }
        catch (SocketException ex)
        {
            throw new DeviceUnreachableException(
                $"Failed to send a streaming frame to {streamEndpoint} ({ex.SocketErrorCode}).", ex);
        }
    }

    public async Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken)
    {
        var client = api ?? throw new DeviceException("ConnectAsync must be called first.");

        // Streaming holds the device in external control, which overrides state changes.
        await StopStreamingAsync(cancellationToken);

        var hsv = HsvColor.FromRgb(color);
        await client.SetHsvAsync(hsv.Hue, hsv.Saturation, hsv.Value, cancellationToken);
        BrightnessPercent = hsv.Value;
    }

    public async Task SetBrightnessAsync(int percent, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        var client = api ?? throw new DeviceException("ConnectAsync must be called first.");
        await StopStreamingAsync(cancellationToken);
        await client.SetBrightnessAsync(percent, cancellationToken);
        streamingBrightness = percent;
        BrightnessPercent = percent;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await StopStreamingAsync(cancellationToken);

        api?.Dispose();
        api = null;
    }

    private Task StopStreamingAsync(CancellationToken cancellationToken)
    {
        stream?.Dispose();
        stream = null;
        streamEndpoint = null;
        return Task.CompletedTask;
    }

    /// <summary>Fills in the host by discovery when configuration did not name one.</summary>
    private async Task<NanoleafSettings> ResolveSettingsAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.Host))
        {
            return settings;
        }

        var found = await NanoleafDiscovery.DiscoverAsync(TimeSpan.FromSeconds(6), cancellationToken);
        var candidates = settings.ExpectedModel is { } model
            ? found.Where(d => string.Equals(d.Model, model, StringComparison.OrdinalIgnoreCase)).ToList()
            : [.. found];

        return candidates.Count switch
        {
            0 => throw new DeviceUnreachableException(
                "No Nanoleaf device was found on the network. Set device.settings.host explicitly."),
            1 => settings with { Host = candidates[0].Host, Port = candidates[0].Port },
            _ => throw new DeviceException(
                $"{candidates.Count} Nanoleaf devices were found. Set device.settings.host to choose one: " +
                string.Join(", ", candidates.Select(d => d.Host)) + "."),
        };
    }

    private static void VerifyModel(NanoleafSettings settings, NanoleafDeviceInfo info)
    {
        if (settings.ExpectedModel is not { } expected)
        {
            return;
        }

        if (!string.Equals(info.Model, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeviceException(
                $"Expected a Nanoleaf model {expected} but the device at {settings.Host} reports " +
                $"'{info.Model}'. Correct device.settings.expectedModel or point host at the right device.");
        }
    }

    /// <summary>
    /// Determines the panel ids to address. An explicit mapping wins; otherwise the device's own
    /// layout is used, falling back to sequential ids for models without a layout endpoint.
    /// </summary>
    private async Task<int[]> ResolvePanelIdsAsync(
        NanoleafSettings resolved,
        CancellationToken cancellationToken)
    {
        if (resolved.LedMapping.Length > 0)
        {
            ZoneAddressSource = "Explicit LED mapping; verify the physical order with the chase test.";
            return resolved.LedMapping;
        }

        NanoleafPanelLayout? layout = null;
        try
        {
            layout = await api!.GetPanelLayoutAsync(cancellationToken);
        }
        catch (DeviceException)
        {
            // Matter Essentials models answer panelLayout with HTTP 500. Their LEDs are simply
            // addressed as a sequential run, so the absence of a layout is not an error.
        }

        if (layout?.PositionData is { Length: > 0 } positions)
        {
            ZoneAddressSource = "Device layout, ordered by Y then X.";
            return [.. positions.OrderBy(p => p.Y).ThenBy(p => p.X).Select(p => p.PanelId)];
        }

        var count = layout?.NumPanels > 0 ? layout.NumPanels : DefaultEssentialsLedCount;
        ZoneAddressSource = "Sequential fallback; physical LED count/order must be verified with the chase test.";
        return [.. Enumerable.Range(0, count)];
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None);
    }
}
