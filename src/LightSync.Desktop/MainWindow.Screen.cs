using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Core.Mapping;
using LightSync.Core.Pipeline;
using LightSync.Core.Processing;
using LightSync.Desktop.Screen;
using Microsoft.Extensions.Logging.Abstractions;

namespace LightSync.Desktop;

internal sealed partial class MainWindow
{
    private readonly ScreenVisualizer screenVisualizer = new();
    private readonly DispatcherTimer screenPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private ScreenPreviewProcessor? screenProcessor;
    private PipelineMetrics? screenMetrics;
    private PortalSelection? screenSelection;
    private bool screenRunning;
    private bool screenOptionsDirty;
    private bool screenSelectionSaved;
    private bool screenRecovering;
    private int screenOutputCount;

    private void InitializeScreen()
    {
        ScreenPreviewHost.Child = screenVisualizer;
        screenVisualizer.SelectionChanged += (_, _) => ScreenZoneLabel.Text = screenVisualizer.SelectionText;
        screenPreviewTimer.Tick += (_, _) => RefreshScreenPreview();
        ScreenSourceBox.SelectionChanged += (_, _) => RefreshScreenSourceHint();
        ScreenTargetBox.SelectionChanged += (_, _) => UpdateScreenControls();
    }

    private void LoadScreenOptions()
    {
        ScreenSourceBox.SelectedIndex = configuration.Capture.IsConfigured ? 1 : 0;
        ScreenFpsBox.Value = configuration.Capture.Fps;
        ScreenZonesBox.Value = configuration.Mapping.ZoneCount;
        ScreenLayoutBox.SelectedIndex = configuration.Mapping.ParsedLayout == ZoneLayout.Vertical ? 0 : 1;
        var descending = configuration.Mapping.ParsedLayout == ZoneLayout.Vertical
            ? configuration.Mapping.ParsedDirection == ZoneDirection.RightToLeft
            : configuration.Mapping.ParsedDirection == ZoneDirection.BottomToTop;
        ScreenReverseBox.IsChecked = descending ^ configuration.Mapping.Reverse;
        ScreenBrightnessBox.Value = (decimal)(configuration.Processing.Brightness * 100);
        ScreenSmoothingBox.Value = (decimal)(configuration.Processing.Smoothing * 100);
        ScreenSaturationBox.Value = (decimal)(configuration.Processing.Saturation * 100);
        ScreenAveragingBox.SelectedIndex = configuration.Processing.ParsedAveraging switch
        {
            ZoneAveraging.Mean => 1,
            ZoneAveraging.ColorWeighted => 2,
            _ => 0,
        };
        RefreshScreenSourceHint();
    }

    private void RefreshScreenTargets()
    {
        var previous = ScreenTargetBox.SelectedItem as ScreenTarget;
        List<ScreenTarget> targets = [new(null, "Preview only · no lights")];
        var enabled = profiles.Where(p => p.SyncEnabled).ToArray();
        if (enabled.Length > 0)
        {
            targets.Add(new ScreenTarget(null, $"All enabled devices · {enabled.Length}", AllEnabled: true));
        }
        foreach (var profile in profiles)
        {
            targets.Add(new ScreenTarget(profile.Id, profile.Name));
        }

        ScreenTargetBox.ItemsSource = targets;
        if (previous is not null)
        {
            ScreenTargetBox.SelectedItem = targets.Find(t => t.ProfileId == previous.ProfileId
                && t.AllEnabled == previous.AllEnabled) ?? targets[0];
        }
        else
        {
            ScreenTargetBox.SelectedItem = enabled.Length > 1 ? targets[1]
                : targets.Find(t => t.ProfileId == enabled.FirstOrDefault()?.Id && !t.AllEnabled) ?? targets[0];
        }
    }

    private void UpdateScreenControls()
    {
        var editable = !busy && !exclusive && !closing;
        ScreenSettings.IsEnabled = editable;
        StartScreenButton.IsEnabled = editable;
        StopScreenButton.IsEnabled = screenRunning;
        var previewOnly = IsScreenPreviewOnly;
        StartScreenButton.Content = previewOnly ? "Start preview" : "Start screen sync";
        ScreenZonesBox.IsEnabled = previewOnly;
        var custom = configuration.Mapping.CustomOrder is { Length: > 0 };
        ScreenLayoutBox.IsEnabled = !custom;
        ScreenReverseBox.IsEnabled = !custom;
        ScreenMappingHint.Text = custom
            ? "Using the saved CLI custom order. Layout and reversal follow that order; its zone count must match the output."
            : "Preview zones apply to preview-only mode. A real device uses all zones in its connected layout.";
    }

    private void RefreshScreenSourceHint()
    {
        ScreenSourceHint.Text = ScreenSourceBox.SelectedIndex switch
        {
            1 => configuration.Capture.IsConfigured
                ? $"Saved area: {configuration.Capture.Area}. Your desktop may ask you to approve sharing again."
                : "No saved area yet. Choose a screen / region first, or try the demo.",
            2 => "Generated test pattern · no screen is shared. You can also send the demo to a selected device.",
            _ => "Your desktop will ask which screen or region to share. The whole selection will be previewed.",
        };
    }

    private bool IsScreenPreviewOnly => ScreenTargetBox.SelectedItem is not ScreenTarget target
        || (target.ProfileId is null && !target.AllEnabled);

    private void OnScreenOverlayChanged(object? sender, RoutedEventArgs e) =>
        screenVisualizer.ShowZones(ScreenOverlayBox.IsChecked == true);

    private async void OnStartScreen(object? sender, RoutedEventArgs e) => await ExecuteAsync(StartScreenAsync);

    private async Task StartScreenAsync()
    {
        await StopWorkerAsync();
        var source = ScreenSourceBox.SelectedIndex;
        var saved = configuration.Capture;
        if (source == 1 && !saved.IsConfigured)
        {
            throw new InvalidOperationException("No saved capture area. Choose a screen / region first.");
        }

        if (source != 2 && !OperatingSystem.IsLinux())
        {
            throw new NotSupportedException("Screen capture currently uses the Linux desktop portal. Try the demo on this platform.");
        }

        List<ConnectedDevice> targets = [];
        var selection = ScreenTargetBox.SelectedItem as ScreenTarget;
        var selectedProfiles = selection?.AllEnabled == true ? profiles.Where(p => p.SyncEnabled).ToArray()
            : profiles.Where(p => p.Id == selection?.ProfileId).ToArray();
        if (selection?.AllEnabled == true && selectedProfiles.Length == 0)
        {
            throw new InvalidOperationException("Enable at least one device in the sync device list.");
        }

        foreach (var profile in selectedProfiles)
        {
            var target = await ConnectProfileAsync(profile);
            targets.Add(target);
            if (target.Device is IBrightnessControl brightness)
            {
                await brightness.SetBrightnessAsync(profile.BrightnessPercent, lifetime.Token);
            }
        }

        var count = targets.Count == 0 ? (int)(ScreenZonesBox.Value ?? 24) : targets[0].Device.Capabilities.MaximumZones;
        var vertical = ScreenLayoutBox.SelectedIndex == 0;
        var mapping = configuration.Mapping with
        {
            ZoneCount = count,
            Layout = vertical ? "vertical" : "horizontal",
            Direction = vertical ? "left-to-right" : "top-to-bottom",
            Reverse = ScreenReverseBox.IsChecked == true,
        };
        var mapper = new ZoneMapper(count, mapping.ParsedLayout, mapping.ParsedDirection, mapping.Reverse, mapping.CustomOrder);
        var processing = configuration.Processing with
        {
            Brightness = (double)(ScreenBrightnessBox.Value ?? 100) / 100,
            Smoothing = (double)(ScreenSmoothingBox.Value ?? 20) / 100,
            Saturation = (double)(ScreenSaturationBox.Value ?? 100) / 100,
            Averaging = ScreenAveragingBox.SelectedIndex switch
            {
                1 => "mean",
                2 => "colour-weighted",
                _ => "luminance-weighted",
            },
        };
        var processor = new ScreenPreviewProcessor(mapper, processing.ToOptions());
        List<ScreenSyncOutput> outputs = [];
        for (var i = 0; i < targets.Count; i++)
        {
            var device = targets[i].Device;
            IColorProcessor deviceProcessor = i == 0 ? processor : new ColorProcessor(
                new ZoneMapper(device.Capabilities.MaximumZones, mapping.ParsedLayout, mapping.ParsedDirection,
                    mapping.Reverse, mapping.CustomOrder), processing.ToOptions());
            outputs.Add(new ScreenSyncOutput(deviceProcessor, device));
        }
        var fps = (int)(ScreenFpsBox.Value ?? 30);
        var area = source == 1 ? saved.Area : new CaptureArea(0, 0, 640, 360);
        var scale = Math.Min(1, Math.Min(640.0 / area.Width, 360.0 / area.Height));
        var request = new CaptureRequest(saved.DisplayId, area, fps,
            Math.Max(1, (int)(area.Width * scale)), Math.Max(1, (int)(area.Height * scale)));
        configuration = configuration with
        {
            Mapping = mapping,
            Processing = processing,
            Capture = saved with { Fps = fps },
        };
        screenOptionsDirty = true;
        await SaveOptionsAsync();
        RefreshDevices();

        screenProcessor = processor;
        screenMetrics = new PipelineMetrics();
        screenSelection = null;
        screenSelectionSaved = source == 2;
        screenRecovering = false;
        screenRunning = true;
        screenOutputCount = targets.Count;
        ScreenStateLabel.Text = source == 2 ? "DEMO" : "STARTING";
        ScreenMetrics.Text = source == 2 ? "Starting demo…" : "Waiting for screen sharing and the first frame…";
        var outputLabel = targets.Count == 0 ? "Preview only" : string.Join(", ", targets.Select(t => t.Device.Name));
        ScreenPreviewLabel.Text = $"{(source == 2 ? "Demo pattern" : "Screen capture")} → {outputLabel} · {count} LEDs in preview";
        StatusLabel.Text = source == 2 ? "Demo preview starting." : "Approve the screen-sharing picker to start the preview.";
        var metrics = screenMetrics;
        screenPreviewTimer.Start();
        BeginWorker(async cancellationToken =>
        {
            await using IScreenCapture capture = source == 2 ? new SyntheticScreenCapture()
                : new PortalScreenCapturer(source == 1 ? saved.RestoreToken : null,
                    selection => Volatile.Write(ref screenSelection, selection), useSelectionBounds: source == 0,
                    recovering => Volatile.Write(ref screenRecovering, recovering));
            await using var preview = targets.Count == 0 ? new PreviewDevice(count) : null;
            if (preview is not null)
            {
                outputs.Add(new ScreenSyncOutput(processor, preview));
            }
            try
            {
                await Task.Run(async () =>
                {
                    var pipeline = new LightSyncPipeline(capture, outputs, metrics,
                        NullLogger<LightSyncPipeline>.Instance);
                    await pipeline.RunAsync(request, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        throw new CaptureException(metrics.CapturedFrames == 0
                            ? "The screen source ended without producing any frames. Choose a screen / region again."
                            : "The screen-sharing source ended. Choose a screen / region again to resume sync.");
                    }
                }, cancellationToken);
            }
            finally
            {
                foreach (var output in outputs)
                {
                    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        await output.Device.SetStaticColorAsync(RgbColor.Black, shutdown.Token);
                    }
                    catch (Exception ex)
                    {
                        metrics.RecordDeviceError($"{output.Device.Name}: blackout failed: {ex.Message}");
                    }
                }
            }
        }, isExclusive: true, targets);
    }

    private void RefreshScreenPreview()
    {
        if (screenProcessor is null || screenMetrics is null)
        {
            return;
        }

        if (Volatile.Read(ref screenRecovering))
        {
            ScreenStateLabel.Text = "RECOVERING";
            ScreenMetrics.Text = "Capture paused · reconnecting to the shared source…";
            StatusLabel.Text = "Screen capture stalled. Reconnecting automatically…";
            return;
        }

        if (!screenProcessor.UpdatePreview(screenVisualizer))
        {
            return;
        }

        ScreenEmptyState.IsVisible = false;
        ScreenStateLabel.Text = ScreenSourceBox.SelectedIndex == 2 ? "DEMO" : "LIVE";
        ScreenZoneLabel.Text = screenVisualizer.SelectionText;
        ScreenMetrics.Text = screenOutputCount == 0
            ? string.Create(CultureInfo.InvariantCulture,
                $"{screenMetrics.Fps:F1} fps · {screenMetrics.CapturedFrames} captured frames · preview only")
            : screenOutputCount == 1 ? screenMetrics.ToString()
            : string.Create(CultureInfo.InvariantCulture,
                $"{screenOutputCount} devices · {screenMetrics.CapturedFrames} captured · {screenMetrics.SentFrames} delivered · {screenMetrics.DroppedFrames} dropped");
        if (!screenSelectionSaved && Volatile.Read(ref screenSelection) is { } selection)
        {
            var area = ScreenSourceBox.SelectedIndex == 0
                ? new CaptureArea(0, 0, selection.Width, selection.Height) : configuration.Capture.Area;
            configuration = configuration with
            {
                Capture = CaptureConfig.FromArea(configuration.Capture.DisplayId, area,
                    configuration.Capture.Fps, selection.RestoreToken),
            };
            screenOptionsDirty = true;
            screenSelectionSaved = true;
            RefreshScreenSourceHint();
        }

        StatusLabel.Text = screenMetrics.DeviceErrors > 0
            ? $"Screen capture is running, but {screenMetrics.DeviceErrors} device frame(s) failed. Last error: {screenMetrics.LastDeviceError}"
            : "Screen preview running · click a zone or output colour to inspect an LED.";
    }

    private void ClearScreenPreview()
    {
        screenPreviewTimer.Stop();
        screenRunning = false;
        screenProcessor = null;
        screenSelection = null;
        screenRecovering = false;
        screenVisualizer.Clear();
        ScreenEmptyState.IsVisible = true;
        ScreenStateLabel.Text = "IDLE";
        ScreenZoneLabel.Text = screenVisualizer.SelectionText;
        ScreenMetrics.Text = screenMetrics is null ? "Ready · no screen capture is running."
            : "Stopped · " + screenMetrics;
    }

    private sealed record ScreenTarget(string? ProfileId, string Label, bool AllEnabled = false)
    {
        public override string ToString() => Label;
    }
}
