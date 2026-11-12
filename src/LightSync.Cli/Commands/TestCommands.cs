using System.Diagnostics;
using LightSync.Core.Colors;
using LightSync.Core.Devices;
using LightSync.Devices.Nanoleaf;

namespace LightSync.Cli.Commands;

internal static class TestCommands
{
    public static async Task<int> TestDeviceAsync(CommandContext context, CancellationToken cancellationToken)
    {
        context.RequireIdle();
        Console.WriteLine("Connecting to the selected device profile...");

        var profile = await context.LoadSelectedProfileAsync(cancellationToken);
        await using var device = await context.ConnectAsync(profile, cancellationToken);

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
        Console.WriteLine($"  master brightness {Yes(device is IBrightnessControl)}");
        if (device is IZoneAddressProvider addresses)
        {
            Console.WriteLine($"  LED addresses    {string.Join(",", addresses.ZoneAddresses)}");
            Console.WriteLine($"  address source   {addresses.ZoneAddressSource}");
        }
        Console.WriteLine();

        var validation = DeviceCapabilityValidator.ValidateForStreaming(
            capabilities, capabilities.MaximumZones);

        if (validation.IsValid)
        {
            ConsoleUI.Success($"Sync will use all {capabilities.MaximumZones} addressable LEDs on this device.");
            return 0;
        }

        ConsoleUI.Warn("The device cannot stream:");
        foreach (var problem in validation.Problems)
        {
            Console.WriteLine($"  - {problem}");
        }

        return 1;
    }

    public static async Task<int> TestColorAsync(CommandContext context, string colorName,
        int? zone, bool isolate, string background, int seconds, string[] leds, CancellationToken cancellationToken)
    {
        context.RequireIdle();
        var color = ConfigCommand.ParseColor(colorName);
        var baseColor = ConfigCommand.ParseColor(background);
        if (seconds < 0 || zone is <= 0 || (isolate && zone is null))
        {
            throw new LightSync.Core.Configuration.ConfigurationException(
                "--zone must be a 1-based LED number; --isolate requires --zone; --seconds must not be negative.");
        }
        var profile = await context.LoadSelectedProfileAsync(cancellationToken);
        await using var device = await context.ConnectAsync(profile, cancellationToken);
        var frame = BuildTestFrame(device.Capabilities.MaximumZones, color, zone, isolate, baseColor);
        ApplyLedChanges(frame, leds);
        DeviceCapabilityValidator.ThrowIfInvalid(DeviceCapabilityValidator.ValidateForStreaming(device.Capabilities, frame.Colors.Length));
        if (device is IBrightnessControl master)
        {
            await master.SetBrightnessAsync(profile.BrightnessPercent, cancellationToken);
        }
        await using var pidFile = await PidFile.CreateAsync(context.PidPath, cancellationToken);
        Console.WriteLine($"Exact RGB {color} on {device.Name}" + (zone is { } led ? $", LED {led}." : ", every LED.") + " Ctrl+C to stop.");
        try
        {
            await HoldFrameAsync(device, frame, seconds == 0 ? null : TimeSpan.FromSeconds(seconds), cancellationToken);
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await device.SetStaticColorAsync(RgbColor.Black, shutdown.Token);
        }
        return 0;
    }

    internal static DeviceTestFrame BuildTestFrame(int count, RgbColor color, int? zone, bool isolate, RgbColor background)
    {
        var frame = new DeviceTestFrame(count);
        if (zone is null)
        {
            frame.SetAll(color);
        }
        else if (isolate)
        {
            frame.Isolate(zone.Value - 1, color);
        }
        else
        {
            frame.SetAll(background);
            frame.SetZone(zone.Value - 1, color);
        }
        return frame;
    }

    internal static void ApplyLedChanges(DeviceTestFrame frame, IEnumerable<string> assignments)
    {
        foreach (var assignment in assignments)
        {
            var separator = assignment.IndexOf('=', StringComparison.Ordinal);
            if (separator < 1 || !int.TryParse(assignment[..separator], out var led) || led < 1)
            {
                throw new LightSync.Core.Configuration.ConfigurationException("--led must use NUMBER=COLOUR, with a 1-based LED number.");
            }
            frame.SetZone(led - 1, ConfigCommand.ParseColor(assignment[(separator + 1)..]));
        }
    }

    public static async Task<int> ChaseAsync(CommandContext context, string colorName, int milliseconds,
        CancellationToken cancellationToken)
    {
        context.RequireIdle();
        var color = ConfigCommand.ParseColor(colorName);
        if (color == RgbColor.Black || milliseconds is < 1 or > 60000)
        {
            throw new LightSync.Core.Configuration.ConfigurationException("Choose a visible colour and a hold time from 1 to 60000 ms.");
        }
        var profile = await context.LoadSelectedProfileAsync(cancellationToken);
        await using var device = await context.ConnectAsync(profile, cancellationToken);
        DeviceCapabilityValidator.ThrowIfInvalid(DeviceCapabilityValidator.ValidateForStreaming(device.Capabilities, device.Capabilities.MaximumZones));
        if (device is IBrightnessControl master)
        {
            await master.SetBrightnessAsync(profile.BrightnessPercent, cancellationToken);
        }
        var frame = new DeviceTestFrame(device.Capabilities.MaximumZones);
        await using var pidFile = await PidFile.CreateAsync(context.PidPath, cancellationToken);
        try
        {
            for (var index = 0; index < frame.Colors.Length; index++)
            {
                frame.Isolate(index, color);
                Console.WriteLine($"LED {index + 1}/{frame.Colors.Length}: {color}");
                await HoldFrameAsync(device, frame, TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
            }
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await device.SetStaticColorAsync(RgbColor.Black, shutdown.Token);
        }
        return 0;
    }

    private static async Task HoldFrameAsync(ILightDevice device, DeviceTestFrame frame, TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            while (duration is null || Stopwatch.GetElapsedTime(started) < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await device.SendFrameAsync(frame.Colors, cancellationToken);
                var next = Stopwatch.GetElapsedTime(started) + TimeSpan.FromMilliseconds(100);
                if (duration is { } limit && next > limit)
                {
                    next = limit;
                }
                // Timers can wake slightly early. Keep waiting for the target time
                // instead of sending another frame during a partial millisecond.
                while (next - Stopwatch.GetElapsedTime(started) is var delay && delay > TimeSpan.Zero)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(Math.Ceiling(delay.TotalMilliseconds), 1, 100)), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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
        context.RequireIdle();
        if (seconds <= 0)
        {
            throw new LightSync.Core.Configuration.ConfigurationException("--seconds must be greater than zero.");
        }
        var config = await context.LoadAsync(cancellationToken);
        var profile = await context.LoadSelectedProfileAsync(cancellationToken);
        await using var device = await context.ConnectAsync(profile, cancellationToken);

        var zoneCount = device.Capabilities.MaximumZones;
        if (zoneCount <= 0)
        {
            ConsoleUI.Error("The device reported no addressable LEDs.");
            return 1;
        }

        DeviceCapabilityValidator.ThrowIfInvalid(
            DeviceCapabilityValidator.ValidateForStreaming(device.Capabilities, zoneCount));

        // Uses the configured frame rate rather than a fixed one, so this exercises the same
        // pacing the real run will use.
        if (device is IBrightnessControl master)
        {
            await master.SetBrightnessAsync(profile.BrightnessPercent, cancellationToken);
        }
        await using var pidFile = await PidFile.CreateAsync(context.PidPath, cancellationToken);
        var fps = config.Capture.Fps;
        var interval = TimeSpan.FromSeconds(1.0 / fps);

        Console.WriteLine($"Streaming a moving rainbow to {device.Name} across {zoneCount} LEDs at {fps} fps.");
        Console.WriteLine($"Running for {seconds}s, or press Ctrl+C to stop.");

        var colors = new RgbColor[zoneCount];
        var frames = 0;
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        try
        {
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                var phase = frames / (double)fps * 0.3;

                for (var zone = 0; zone < zoneCount; zone++)
                {
                    colors[zone] = HueToRgb((((double)zone / zoneCount) + phase) % 1.0);
                }

                await device.SendFrameAsync(colors, cancellationToken);
                frames++;

                ConsoleUI.WriteLiveStatus($"{ConsoleUI.ZoneStrip(colors)} {frames} frames");
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await device.SetStaticColorAsync(RgbColor.Black, shutdown.Token);
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
