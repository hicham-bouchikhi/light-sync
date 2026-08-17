using LightSync.Core.Colors;
using LightSync.Core.Devices;
using LightSync.Devices.Nanoleaf;

namespace LightSync.Cli.Commands;

internal static class TestCommands
{
    public static async Task<int> TestDeviceAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = await context.LoadAsync(cancellationToken);

        Console.WriteLine($"Connecting to adapter '{config.Device.Adapter}'...");

        await using var device = context.Adapters.Create(config.Device.Adapter, config.Device.Settings);
        await device.ConnectAsync(cancellationToken);

        ConsoleUI.Success($"Connected to {device.Name}.");

        if (device is NanoleafAdapter { DeviceInfo: { } info })
        {
            Console.WriteLine($"  model            {info.Model}");
            Console.WriteLine($"  firmware         {info.FirmwareVersion}");
            Console.WriteLine($"  serial           {info.SerialNumber}");
        }

        var capabilities = device.Capabilities;
        Console.WriteLine($"  addressable LEDs {capabilities.MaximumZones}");
        Console.WriteLine($"  streaming        {Yes(capabilities.SupportsStreaming)}");
        Console.WriteLine($"  static colour    {Yes(capabilities.SupportsStaticColor)}");
        Console.WriteLine($"  per-zone colour  {Yes(capabilities.SupportsPerZoneColor)}");
        Console.WriteLine();

        var validation = DeviceCapabilityValidator.ValidateForStreaming(
            capabilities, config.Mapping.ZoneCount);

        if (validation.IsValid)
        {
            ConsoleUI.Success($"The device can render the configured {config.Mapping.ZoneCount} zones.");
            return 0;
        }

        ConsoleUI.Warn($"The configured {config.Mapping.ZoneCount} zones will not work as-is:");
        foreach (var problem in validation.Problems)
        {
            Console.WriteLine($"  - {problem}");
        }

        if (capabilities.MaximumZones > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Set mapping.zoneCount to {capabilities.MaximumZones} to match the device.");
        }

        return 1;
    }

    public static async Task<int> TestColorAsync(
        CommandContext context,
        string colorName,
        CancellationToken cancellationToken)
    {
        if (!ColorConstants.TryParseNamed(colorName, out var color))
        {
            ConsoleUI.Error(
                $"Unknown colour '{colorName}'. Known colours: " +
                string.Join(", ", ColorConstants.NamedColors) + ".");
            return 1;
        }

        var config = await context.LoadAsync(cancellationToken);

        await using var device = context.Adapters.Create(config.Device.Adapter, config.Device.Settings);
        await device.ConnectAsync(cancellationToken);

        DeviceCapabilityValidator.ThrowIfInvalid(
            DeviceCapabilityValidator.ValidateForStaticColor(device.Capabilities));

        await device.SetStaticColorAsync(color, cancellationToken);
        await device.DisconnectAsync(cancellationToken);

        ConsoleUI.Success($"Set {device.Name} to {colorName} ({color}).");
        return 0;
    }

    /// <summary>
    /// Streams a moving pattern straight to the device, so the streaming path can be exercised
    /// without screen capture being set up.
    /// </summary>
    public static async Task<int> TestStreamAsync(
        CommandContext context,
        int seconds,
        CancellationToken cancellationToken)
    {
        var config = await context.LoadAsync(cancellationToken);

        await using var device = context.Adapters.Create(config.Device.Adapter, config.Device.Settings);
        await device.ConnectAsync(cancellationToken);

        var zoneCount = device.Capabilities.MaximumZones;
        if (zoneCount <= 0)
        {
            ConsoleUI.Error("The device reported no addressable LEDs.");
            return 1;
        }

        DeviceCapabilityValidator.ThrowIfInvalid(
            DeviceCapabilityValidator.ValidateForStreaming(device.Capabilities, zoneCount));

        Console.WriteLine($"Streaming a moving rainbow to {device.Name} across {zoneCount} LEDs.");
        Console.WriteLine($"Running for {seconds}s, or press Ctrl+C to stop.");

        var colors = new RgbColor[zoneCount];
        var frames = 0;
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        try
        {
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                var phase = frames / 60.0 * 0.3;

                for (var zone = 0; zone < zoneCount; zone++)
                {
                    colors[zone] = HueToRgb((((double)zone / zoneCount) + phase) % 1.0);
                }

                await device.SendFrameAsync(colors, cancellationToken);
                frames++;

                ConsoleUI.WriteLiveStatus($"{ConsoleUI.ZoneStrip(colors)} {frames} frames");
                await Task.Delay(TimeSpan.FromMilliseconds(1000.0 / 60), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        ConsoleUI.EndLiveStatus();
        Console.WriteLine();
        ConsoleUI.Success($"Sent {frames} frames.");
        return 0;
    }

    private static string Yes(bool value) => value ? "yes" : "no";

    private static RgbColor HueToRgb(double hue)
    {
        var sector = hue * 6.0;
        var rising = (byte)((sector - Math.Floor(sector)) * 255);
        var falling = (byte)(255 - rising);

        return (int)sector switch
        {
            0 => new RgbColor(255, rising, 0),
            1 => new RgbColor(falling, 255, 0),
            2 => new RgbColor(0, 255, rising),
            3 => new RgbColor(0, falling, 255),
            4 => new RgbColor(rising, 0, 255),
            _ => new RgbColor(255, 0, falling),
        };
    }
}
