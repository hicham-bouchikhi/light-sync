using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LightSync.Application;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Desktop;

internal sealed partial class MainWindow
{
    private readonly LightingVisualizer lightingVisualizer = new();
    private AmbientSyncSession? lightingSession;
    private bool lightingRunning;
    private bool lightingOptionsDirty;

    private const string InvalidLightingHexMessage =
        "Colour must be red/green/blue/white/black, #RRGGBB, or R,G,B (0–255).";

    private void InitializeLighting() => LightingVisualizerHost.Child = lightingVisualizer;

    private void LoadLightingOptions()
    {
        var options = configuration.Lighting;
        LightingModeBox.SelectedIndex = options.Mode switch { "pulse" => 1, "rainbow" => 2, _ => 0 };
        LightingRedBox.Value = options.Color.R;
        LightingGreenBox.Value = options.Color.G;
        LightingBlueBox.Value = options.Color.B;
        LightingBrightnessBox.Value = (decimal)(options.Brightness * 100);
        LightingSpeedBox.Value = (decimal)options.Speed;
        LightingDirectionBox.SelectedIndex = options.Direction == "right-to-left" ? 1 : 0;
        RefreshLightingColor();
        RefreshLightingControls();
    }

    private RgbColor CurrentLightingColor => new((byte)(LightingRedBox.Value ?? 0),
        (byte)(LightingGreenBox.Value ?? 0), (byte)(LightingBlueBox.Value ?? 0));

    private LightingConfig ReadLightingOptions() => new()
    {
        Mode = LightingModeBox.SelectedIndex switch { 1 => "pulse", 2 => "rainbow", _ => "static" },
        Color = CurrentLightingColor,
        Brightness = (double)(LightingBrightnessBox.Value ?? 100) / 100,
        Speed = (double)(LightingSpeedBox.Value ?? 1),
        Direction = LightingDirectionBox.SelectedIndex == 1 ? "right-to-left" : "left-to-right",
    };

    private void RefreshLightingColor()
    {
        var color = CurrentLightingColor;
        LightingColorPreview.Background = Brush(color);
        LightingHexBox.Text = color.ToString();
    }

    private void RefreshLightingControls()
    {
        var editable = !busy && !closing && (!exclusive || lightingRunning);
        var mode = LightingModeBox.SelectedIndex;
        // Rainbow supplies its own hues, so the colour controls are only meaningful for
        // static and pulse. Values are kept, not cleared, for when the mode changes back.
        var usesColour = mode is 0 or 1;
        LightingSpeedBox.IsEnabled = editable && mode is 1 or 2;
        LightingDirectionBox.IsEnabled = editable && mode == 2;
        LightingRedBox.IsEnabled = editable && usesColour;
        LightingGreenBox.IsEnabled = editable && usesColour;
        LightingBlueBox.IsEnabled = editable && usesColour;
        LightingHexBox.IsEnabled = editable && usesColour;
        LightingBrightnessBox.IsEnabled = editable;
        LightingColorPreview.Opacity = usesColour ? 1 : 0.35;
    }

    private void OnLightingColorChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!loaded)
        {
            return;
        }

        RefreshLightingColor();
        UpdateLightingOptions();
    }

    private void OnLightingModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!loaded)
        {
            return;
        }

        RefreshLightingControls();
        UpdateLightingOptions();
    }

    private void OnLightingSettingsChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!loaded)
        {
            return;
        }

        // Brightness and speed must not rewrite the hex box while it is being edited.
        UpdateLightingOptions();
    }

    private void OnLightingDirectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!loaded)
        {
            return;
        }

        UpdateLightingOptions();
    }

    private void OnLightingHexCommitted(object? sender, RoutedEventArgs e) => CommitLightingHex();

    private void OnLightingHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitLightingHex();
            e.Handled = true;
        }
    }

    private void CommitLightingHex()
    {
        if (!loaded)
        {
            return;
        }

        if (!ColorConstants.TryParse(LightingHexBox.Text, out var color))
        {
            StatusLabel.Text = InvalidLightingHexMessage;
            RefreshLightingColor();
            return;
        }

        LightingRedBox.Value = color.R;
        LightingGreenBox.Value = color.G;
        LightingBlueBox.Value = color.B;
        RefreshLightingColor();
        UpdateLightingOptions();
        if (StatusLabel.Text == InvalidLightingHexMessage)
        {
            StatusLabel.Text = "Lighting colour updated.";
        }
    }

    private void UpdateLightingOptions()
    {
        if (!loaded || closing)
        {
            return;
        }

        var options = ReadLightingOptions();
        lightingSession?.UpdateOptions(options);
        configuration = configuration with { Lighting = options };
        lightingOptionsDirty = true;
    }

    private async void OnStartLighting(object? sender, RoutedEventArgs e) => await ExecuteAsync(StartLightingAsync);

    private async Task StartLightingAsync()
    {
        await StopWorkerAsync();
        var enabled = profiles.Where(p => p.SyncEnabled).ToArray();
        if (enabled.Length == 0)
        {
            throw new InvalidOperationException("Choose at least one device under Sync devices in Lighting modes.");
        }

        List<ILightDevice> devices = [];
        foreach (var profile in enabled)
        {
            devices.Add((await ConnectProfileAsync(profile)).Device);
        }

        var options = ReadLightingOptions();
        configuration = configuration with { Lighting = options };
        lightingOptionsDirty = true;
        await SaveOptionsAsync();
        RefreshDevices();
        var targets = enabled.Select(p => connected[p.Id]).ToArray();
        // Build the session before starting the worker so an edit that lands immediately after
        // Start applies to it rather than being lost.
        var session = new AmbientSyncSession(devices, enabled, options, reportEveryFrames: 3);
        lightingSession = session;
        lightingRunning = true;
        lightingVisualizer.Clear();
        LightingMetrics.Text = $"{EffectLabel(options.Mode)} · {options.Color} · holding on {enabled.Length} device(s).";
        StatusLabel.Text = "Lighting mode running. Colour, brightness, speed and direction apply live.";
        BeginWorker(async cancellationToken =>
        {
            try
            {
                await session.RunAsync(status =>
                {
                    lightingVisualizer.Update(status.Colors.Span);
                    LightingMetrics.Text = string.Create(CultureInfo.InvariantCulture,
                        $"{status.Frames} frames · {EffectLabel(status.Options.Mode)} · {status.Options.Color} · brightness {status.Options.Brightness:P0} · speed {status.Options.Speed:0.0}");
                }, cancellationToken);
            }
            finally
            {
                lightingSession = null;
            }
        }, isExclusive: true, targets);
    }

    private static string EffectLabel(string mode) => mode switch
    {
        "pulse" => "Pulse",
        "rainbow" => "Rainbow",
        _ => "Static colour",
    };
}
