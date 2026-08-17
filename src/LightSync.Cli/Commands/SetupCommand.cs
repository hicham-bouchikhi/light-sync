using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Cli.Commands;

internal static class SetupCommand
{
    /// <summary>
    /// Opens the portal picker, saves whatever the user chose, and optionally validates the
    /// device. <paramref name="areaOnly"/> backs the <c>select-area</c> command.
    /// </summary>
    public static async Task<int> RunAsync(
        CommandContext context,
        bool areaOnly,
        CancellationToken cancellationToken)
    {
        var config = await ConfigurationLoader.LoadAsync(context.ConfigPath, cancellationToken);

        var displays = await TryGetDisplaysAsync(context, cancellationToken);
        if (displays.Count > 0)
        {
            ConsoleUI.WriteDisplays(displays);
            Console.WriteLine();
        }

        Console.WriteLine("Your desktop will now ask which part of the screen to share.");
        Console.WriteLine();
        Console.WriteLine("  - Choose the 'Region' tab and drag a rectangle to sync just that area,");
        Console.WriteLine("    or pick a whole screen to sync all of it.");
        Console.WriteLine("  - Tick 'Allow a restore token' if you want to skip this dialog next time.");
        Console.WriteLine();

        PortalSelection selection;
        await using var capturer = new PortalScreenCapturer(config.Capture.RestoreToken);

        try
        {
            // Starting the capture is what shows the picker, and it also proves the chosen
            // source actually produces frames before anything is written to disk.
            var probe = BuildProbeRequest(config);
            var frame = await capturer.StartAsync(probe, cancellationToken);
            selection = capturer.Selection!;

            if (frame.IsEmpty)
            {
                ConsoleUI.Error("The selected source produced no frames. Nothing has been saved.");
                return 1;
            }
        }
        catch (PortalCancelledException)
        {
            ConsoleUI.Warn("Selection cancelled. Nothing has been changed.");
            return 1;
        }
        catch (CaptureException ex)
        {
            ConsoleUI.Error(ex.Message);
            return 1;
        }

        ConsoleUI.Success($"Capturing {selection.Width}x{selection.Height} from {Describe(selection)}.");

        WarnAboutRegionCoordinates(selection);

        var updated = config with
        {
            Capture = CaptureConfig.FromArea(
                displayId: config.Capture.DisplayId,
                area: selection.Area,
                fps: config.Capture.Fps,
                restoreToken: selection.RestoreToken ?? config.Capture.RestoreToken),
        };

        // A region arrives already cropped, so the stream size is the capture size and the
        // origin the portal reports is meaningless. Store the size and leave the origin at zero
        // rather than inventing coordinates.
        await context.SaveAsync(updated, cancellationToken);

        Console.WriteLine($"Saved to {context.ConfigPath}.");
        Console.WriteLine(
            $"  area  {updated.Capture.Width}x{updated.Capture.Height}" +
            $"+{updated.Capture.X}+{updated.Capture.Y}");
        Console.WriteLine($"  fps   {updated.Capture.Fps}");
        Console.WriteLine($"  zones {updated.Mapping.ZoneCount} ({updated.Mapping.Layout})");

        if (selection.RestoreToken is not null)
        {
            Console.WriteLine("  a restore token was saved, so the dialog will be skipped next time");
        }
        else
        {
            ConsoleUI.Warn(
                "  no restore token was issued, so the dialog will appear again on the next run");
        }

        if (areaOnly)
        {
            return 0;
        }

        return await ValidateDeviceAsync(context, updated, cancellationToken);
    }

    /// <summary>
    /// A minimal request just to negotiate the stream. The real frame size is computed from the
    /// saved configuration once the area is known.
    /// </summary>
    private static CaptureRequest BuildProbeRequest(AppConfig config) =>
        new(
            DisplayId: config.Capture.DisplayId,
            Area: config.Capture.IsConfigured ? config.Capture.Area : new CaptureArea(0, 0, 1, 1),
            Fps: config.Capture.Fps,
            TargetWidth: 1,
            TargetHeight: 1);

    private static void WarnAboutRegionCoordinates(PortalSelection selection)
    {
        if (!selection.IsPreCropped)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Note: a region is delivered as its own cropped stream, so the compositor");
        Console.WriteLine("does not report where on the desktop it sits. The saved x and y are 0 and");
        Console.WriteLine("the whole stream is used, which is exactly the area you selected.");
    }

    private static string Describe(PortalSelection selection) => selection.SourceType switch
    {
        PortalSourceType.Monitor => "a whole monitor",
        PortalSourceType.Window => "a window",
        PortalSourceType.Virtual => "the region you selected",
        _ => "the selected source",
    };

    private static async Task<int> ValidateDeviceAsync(
        CommandContext context,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine($"Checking the '{config.Device.Adapter}' device...");

        try
        {
            await using var device = context.Adapters.Create(config.Device.Adapter, config.Device.Settings);
            await device.ConnectAsync(cancellationToken);

            var validation = DeviceCapabilityValidator.ValidateForStreaming(
                device.Capabilities, config.Mapping.ZoneCount);

            if (validation.IsValid)
            {
                ConsoleUI.Success(
                    $"{device.Name} is ready with {device.Capabilities.MaximumZones} addressable LEDs.");
                Console.WriteLine();
                Console.WriteLine("Run 'light-sync run' to start syncing.");
                return 0;
            }

            ConsoleUI.Warn($"{device.Name} cannot render {config.Mapping.ZoneCount} zones:");
            foreach (var problem in validation.Problems)
            {
                Console.WriteLine($"  - {problem}");
            }

            if (device.Capabilities.MaximumZones > 0)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"Set mapping.zoneCount to {device.Capabilities.MaximumZones} in {context.ConfigPath}.");
            }

            return 1;
        }
        catch (DeviceException ex)
        {
            // The capture area is saved either way; only the device step failed.
            ConsoleUI.Warn($"The capture area was saved, but the device is not ready: {ex.Message}");
            return 1;
        }
    }

    private static async Task<IReadOnlyList<DisplayInfo>> TryGetDisplaysAsync(
        CommandContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.Displays.GetDisplaysAsync(cancellationToken);
        }
        catch (DisplayEnumerationException)
        {
            // Display detection is informational here; the portal is what actually chooses.
            return [];
        }
    }
}
