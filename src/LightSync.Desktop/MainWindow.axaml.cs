using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using LightSync.Application;
using LightSync.Core.Audio;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Devices.Nanoleaf;
using LightSync.Devices.OpenRgb;

namespace LightSync.Desktop;

internal sealed partial class MainWindow : Window, IAsyncDisposable
{
    private readonly DeviceAdapterFactory factory = new();
    private readonly AudioVisualizer visualizer = new();
    private readonly Dictionary<string, ConnectedDevice> connected = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource lifetime = new();
    private List<DeviceProfile> profiles = [];
    private AppConfig configuration = new();
    private DeviceProfile? selected;
    private int selectedZone;
    private CancellationTokenSource? workerCancellation;
    private Task? worker;
    private AudioSyncSession? audioSession;
    private bool audioOptionsDirty;
    private bool busy;
    private bool exclusive;
    private bool loaded;
    private bool closing;
    private bool canClose;
    private bool selectingProfile;

    public MainWindow()
    {
        InitializeComponent();
        InitializeScreen();
        VisualizerHost.Child = visualizer;
        AdapterBox.ItemsSource = factory.AvailableAdapters
            .Select(a => a.DisplayName + (a.IsImplemented ? string.Empty : " · planned")).ToArray();
        AudioSourceBox.ItemsSource = new[] { "@DEFAULT_MONITOR@" };
        AudioSourceBox.SelectedIndex = 0;
        DeviceBrightnessSlider.PropertyChanged += (_, change) =>
        {
            if (change.Property == Slider.ValueProperty)
            {
                DeviceBrightnessLabel.Text = $"{Math.Round(DeviceBrightnessSlider.Value)}%";
            }
        };
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            configuration = await ConfigurationLoader.LoadAsync(ConfigurationPaths.ConfigFile, lifetime.Token);
            profiles = (await DeviceProfileStore.LoadAsync(DeviceProfileStore.DefaultPath, configuration.Device, lifetime.Token)).ToList();
            var audio = configuration.Audio;
            AudioModeBox.SelectedIndex = audio.Mode switch { "volume" => 1, "bass" => 2, "rainbow" => 3, _ => 0 };
            GainBox.Value = (decimal)audio.Gain;
            BrightnessBox.Value = (decimal)audio.Brightness * 100;
            SmoothingBox.Value = (decimal)audio.Smoothing * 100;
            GateBox.Value = (decimal)audio.NoiseGate;
            MotionBox.Value = (decimal)audio.Motion;
            AudioRedBox.Value = audio.Color.R;
            AudioGreenBox.Value = audio.Color.G;
            AudioBlueBox.Value = audio.Color.B;
            RedBox.Value = audio.Color.R;
            GreenBox.Value = audio.Color.G;
            BlueBox.Value = audio.Color.B;
            LoadScreenOptions();
            loaded = true;
            RefreshColor();
            RefreshAudioColor();
            RefreshDevices();
            SelectProfile(profiles.FirstOrDefault());
            await RefreshSourcesAsync();
            StatusLabel.Text = "Ready · choose Audio sync, Screen sync, or Devices to configure and test.";
        });
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (busy || closing)
        {
            return;
        }

        busy = true;
        UpdateControls();
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
        finally
        {
            busy = false;
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        var editable = !busy && !exclusive && !closing;
        DeviceActions.IsEnabled = editable;
        DeviceList.IsEnabled = !busy && !closing && (!exclusive || audioSession is not null);
        SyncDeviceList.IsEnabled = !busy && !closing && (!exclusive || audioSession is not null);
        DeviceActivityHint.IsVisible = exclusive;
        DeviceActivityHint.Text = screenRunning
            ? "Screen sync is running. Stop / black out to edit devices or test LEDs."
            : audioSession is not null
            ? "Audio sync is running. Stop / black out to edit settings or test LEDs. You can still remove a device."
            : "An LED chase is running. Stop / black out to edit settings or run another test.";
        ProfileControls.IsEnabled = editable;
        TestControls.IsEnabled = editable;
        AudioControls.IsEnabled = !busy && !closing && (!exclusive || audioSession is not null);
        SourceControls.IsEnabled = editable;
        StartAudioButton.IsEnabled = editable;
        StopAudioButton.IsEnabled = audioSession is not null;
        MotionBox.IsEnabled = AudioModeBox.SelectedIndex == 3;
        StopButton.IsEnabled = workerCancellation is not null || connected.Count > 0;
        UpdateScreenControls();
    }

    private void RefreshDevices()
    {
        RefreshScreenTargets();
        DeviceList.Children.Clear();
        SyncDeviceList.Children.Clear();
        SyncSelectionLabel.Text = $"{profiles.Count(p => p.SyncEnabled)} of {profiles.Count} devices selected · changes apply to the running sync.";
        foreach (var profile in profiles)
        {
            var isConnected = connected.TryGetValue(profile.Id, out var active);
            var detail = isConnected ? $"Connected · {active!.Device.Capabilities.MaximumZones} LEDs"
                : profile.Device.Adapter + " · disconnected";
            var sync = new CheckBox { IsChecked = profile.SyncEnabled, Content = profile.Name };
            sync.IsCheckedChanged += async (_, _) =>
                await ExecuteAsync(() => ChangeSyncSelectionAsync(profile.Id, sync.IsChecked == true));
            var target = new StackPanel { Spacing = 4 };
            target.Children.Add(sync);
            target.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = Brushes.LightSlateGray });
            SyncDeviceList.Children.Add(target);

            var text = new StackPanel { Spacing = 4 };
            text.Children.Add(new TextBlock { Text = profile.Name, FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 12,
                Foreground = isConnected ? Brushes.MediumAquamarine : Brushes.LightSlateGray,
            });
            var select = new Button { Content = text, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            select.Click += (_, _) => SelectProfile(profiles.First(p => p.Id == profile.Id));
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(select);
            var remove = new Button { Content = "Remove device" };
            remove.Click += async (_, _) => await ExecuteAsync(() => RemoveProfileAsync(profile.Id));
            row.Children.Add(remove);
            DeviceList.Children.Add(row);
        }

        if (profiles.Count == 0)
        {
            SyncDeviceList.Children.Add(new TextBlock
            {
                Text = "Add a device in Devices to start syncing.", TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private async Task ChangeSyncSelectionAsync(string id, bool included)
    {
        var resume = audioSession is not null;
        await StopWorkerAsync();
        var index = profiles.FindIndex(p => p.Id == id);
        profiles[index] = profiles[index] with { SyncEnabled = included };
        if (selected?.Id == id)
        {
            selected = profiles[index];
        }

        await SaveProfilesAsync();
        RefreshDevices();
        if (resume && profiles.Any(p => p.SyncEnabled))
        {
            await StartAudioAsync();
        }
        else if (resume)
        {
            StatusLabel.Text = "Audio sync stopped · no devices selected.";
        }
    }

    private void OnManageDevices(object? sender, RoutedEventArgs e) => SectionTabs.SelectedItem = DeviceSection;

    private void SelectProfile(DeviceProfile? profile)
    {
        selected = profile;
        if (profile is null)
        {
            ProfileNameBox.Text = string.Empty;
            MasterBrightnessControls.IsVisible = false;
            ZoneList.Children.Clear();
            TestDeviceLabel.Text = "Add or select a device";
            CapabilitiesLabel.Text = string.Empty;
            MappingLabel.Text = string.Empty;
            return;
        }

        ProfileNameBox.Text = profile.Name;
        selectingProfile = true;
        AdapterBox.SelectedIndex = factory.AvailableAdapters.ToList().FindIndex(a => a.Id == profile.Device.Adapter);
        selectingProfile = false;
        UpdateAdapterSettings();
        HostBox.Text = Setting(profile, "host");
        PortBox.Value = int.TryParse(Setting(profile, "port"), CultureInfo.InvariantCulture, out var port)
            ? port : profile.Device.Adapter == "openrgb" ? OpenRgbSettings.DefaultPort : NanoleafSettings.DefaultPort;
        TokenVariableBox.Text = Setting(profile, "tokenEnvironmentVariable") ?? NanoleafSettings.DefaultTokenEnvironmentVariable;
        LedMappingBox.Text = Setting(profile, "ledMapping");
        ControllerNameBox.Text = Setting(profile, "controllerName");
        ControllerSerialBox.Text = Setting(profile, "serial");
        ControllerLocationBox.Text = Setting(profile, "location");
        DeviceBrightnessSlider.Value = profile.BrightnessPercent;
        selectedZone = 0;
        RefreshZones();
    }

    private static string? Setting(DeviceProfile profile, string key) => profile.Device.Settings.GetValueOrDefault(key);

    private string SelectedAdapterId => AdapterBox.SelectedIndex >= 0
        ? factory.AvailableAdapters[AdapterBox.SelectedIndex].Id : "nanoleaf";

    private void UpdateAdapterSettings()
    {
        var adapter = SelectedAdapterId;
        EndpointSettings.IsVisible = adapter is "nanoleaf" or "openrgb";
        NanoleafProfileSettings.IsVisible = adapter == "nanoleaf";
        NanoleafPairing.IsVisible = adapter == "nanoleaf";
        OpenRgbProfileSettings.IsVisible = adapter == "openrgb";
        HostBox.PlaceholderText = adapter == "openrgb" ? OpenRgbSettings.DefaultHost : "192.168.1.24";
    }

    private void OnAdapterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (selectingProfile || !loaded)
        {
            return;
        }

        UpdateAdapterSettings();
        if (SelectedAdapterId != selected?.Device.Adapter)
        {
            HostBox.Text = SelectedAdapterId == "openrgb" ? OpenRgbSettings.DefaultHost : string.Empty;
            PortBox.Value = SelectedAdapterId == "openrgb" ? OpenRgbSettings.DefaultPort : NanoleafSettings.DefaultPort;
            ControllerNameBox.Text = string.Empty;
            ControllerSerialBox.Text = string.Empty;
            ControllerLocationBox.Text = string.Empty;
        }
    }

    private void RefreshZones()
    {
        ZoneList.Children.Clear();
        MasterBrightnessControls.IsVisible = selected is { } brightnessProfile
            && connected.TryGetValue(brightnessProfile.Id, out var brightnessDevice)
            && brightnessDevice.Device is IBrightnessControl;
        if (selected is null || !connected.TryGetValue(selected.Id, out var active))
        {
            TestDeviceLabel.Text = selected?.Name ?? "Select a device";
            CapabilitiesLabel.Text = "Connect / inspect this device in Settings & pairing to test its LEDs.";
            MappingLabel.Text = string.Empty;
            SelectedZoneLabel.Text = "No LED selected";
            return;
        }

        var capability = active.Device.Capabilities;
        TestDeviceLabel.Text = active.Device.Name;
        var brightnessText = active.Device is IBrightnessControl brightness ? $" · device brightness: {brightness.BrightnessPercent}%" : string.Empty;
        CapabilitiesLabel.Text = $"{capability.MaximumZones} zones · streaming: {capability.SupportsStreaming} · per-zone: {capability.SupportsPerZoneColor}{brightnessText}";
        var addressing = active.Device as IZoneAddressProvider;
        MappingLabel.Text = addressing?.ZoneAddressSource ?? "Frame indices address the device zones sequentially.";
        var colors = active.Frame.Colors;
        for (var i = 0; i < colors.Length; i++)
        {
            var index = i;
            var address = addressing?.ZoneAddresses[i] ?? i;
            var color = colors.Span[i];
            var button = new Button
            {
                Content = $"{i + 1}\nID {address}\n{color}",
                Width = 95,
                Margin = new Avalonia.Thickness(0, 0, 6, 6),
                Background = Brush(color),
                Foreground = ColorMath.Luminance(color) > 0.5 ? Brushes.Black : Brushes.White,
                BorderBrush = i == selectedZone ? Brushes.MediumPurple : Brushes.SlateGray,
                BorderThickness = new Avalonia.Thickness(i == selectedZone ? 3 : 1),
            };
            button.Click += (_, _) =>
            {
                selectedZone = index;
                RefreshZones();
            };
            ZoneList.Children.Add(button);
        }

        var selectedAddress = addressing?.ZoneAddresses[selectedZone] ?? selectedZone;
        SelectedZoneLabel.Text = $"Selected LED {selectedZone + 1} · frame index {selectedZone} · device address {selectedAddress}";
    }

    private static SolidColorBrush Brush(RgbColor color) => new(Color.FromRgb(color.R, color.G, color.B));

    private RgbColor CurrentColor => new((byte)(RedBox.Value ?? 0), (byte)(GreenBox.Value ?? 0), (byte)(BlueBox.Value ?? 0));

    private void OnRgbChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (loaded)
        {
            RefreshColor();
        }
    }

    private void RefreshColor()
    {
        var color = CurrentColor;
        ColorPreview.Background = Brush(color);
        RgbLabel.Text = $"{color} · RGB {color.R}, {color.G}, {color.B}";
    }

    private void OnPreset(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string preset })
        {
            var color = preset switch
            {
                "red" => new RgbColor(255, 0, 0),
                "green" => new RgbColor(0, 255, 0),
                "blue" => new RgbColor(0, 0, 255),
                "white" => new RgbColor(255, 255, 255),
                _ => RgbColor.Black,
            };
            RedBox.Value = color.R;
            GreenBox.Value = color.G;
            BlueBox.Value = color.B;
        }
    }

    private Task SaveProfilesAsync() => DeviceProfileStore.SaveAsync(DeviceProfileStore.DefaultPath, profiles, lifetime.Token);

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (busy || exclusive)
        {
            return;
        }

        var profile = new DeviceProfile
        {
            Name = "New Nanoleaf",
            Device = new DeviceConfig { Adapter = "nanoleaf" },
        };
        profiles.Add(profile);
        RefreshDevices();
        SelectProfile(profile);
        SectionTabs.SelectedItem = DeviceSection;
        DeviceTabs.SelectedIndex = 0;
        StatusLabel.Text = "Enter the device address and save its profile.";
    }

    private async Task<DeviceProfile> SaveSelectedAsync()
    {
        var profile = selected ?? throw new InvalidOperationException("Select or add a device first.");
        var name = ProfileNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Give the device a name.");
        }

        var descriptor = factory.AvailableAdapters[AdapterBox.SelectedIndex < 0 ? 0 : AdapterBox.SelectedIndex];
        if (!descriptor.IsImplemented)
        {
            throw new NotSupportedException($"{descriptor.DisplayName} is planned; its protocol is not implemented yet.");
        }

        var settings = descriptor.Id == profile.Device.Adapter
            ? new Dictionary<string, string>(profile.Device.Settings, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        if (descriptor.Id is "nanoleaf" or "openrgb")
        {
            SetSetting(settings, "host", HostBox.Text);
            SetSetting(settings, "port", ((int)(PortBox.Value ?? 16021)).ToString(CultureInfo.InvariantCulture));
        }
        if (descriptor.Id == "nanoleaf")
        {
            SetSetting(settings, "tokenEnvironmentVariable", TokenVariableBox.Text);
            SetSetting(settings, "ledMapping", LedMappingBox.Text);
        }
        else if (descriptor.Id == "openrgb")
        {
            SetSetting(settings, "controllerName", ControllerNameBox.Text, trimValue: false);
            SetSetting(settings, "serial", ControllerSerialBox.Text, trimValue: false);
            SetSetting(settings, "location", ControllerLocationBox.Text, trimValue: false);
            OpenRgbSettings.FromDictionary(settings);
        }
        var deviceConfig = new DeviceConfig { Adapter = descriptor.Id, Settings = settings };
        if (descriptor.Id == "nanoleaf")
        {
            var problems = NanoleafSettings.FromDictionary(settings).Validate();
            if (problems.Count > 0)
            {
                throw new DeviceException(string.Join(Environment.NewLine, problems));
            }
        }

        if (!deviceConfig.Equals(profile.Device))
        {
            await StopWorkerAsync();
            await DisconnectProfileAsync(profile.Id);
        }

        var updated = profile with
        {
            Name = name, Device = deviceConfig, BrightnessPercent = (int)Math.Round(DeviceBrightnessSlider.Value),
        };
        profiles[profiles.FindIndex(p => p.Id == profile.Id)] = updated;
        selected = updated;
        await SaveProfilesAsync();
        RefreshDevices();
        RefreshZones();
        return updated;
    }

    private static void SetSetting(Dictionary<string, string> settings, string key, string? value, bool trimValue = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            settings.Remove(key);
        }
        else
        {
            settings[key] = trimValue ? value.Trim() : value;
        }
    }

    private async void OnSaveProfile(object? sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await SaveSelectedAsync();
        StatusLabel.Text = "Device profile saved.";
    });

    private async Task<ConnectedDevice> ConnectProfileAsync(DeviceProfile profile)
    {
        if (connected.TryGetValue(profile.Id, out var existing))
        {
            return existing;
        }

        var descriptor = factory.AvailableAdapters.FirstOrDefault(a => a.Id == profile.Device.Adapter);
        if (descriptor is null || !descriptor.IsImplemented)
        {
            throw new DeviceException($"Adapter '{profile.Device.Adapter}' is unavailable.");
        }

        StatusLabel.Text = $"Connecting to {profile.Name}…";
        var device = factory.Create(profile.Device.Adapter, profile.Device.Settings);
        try
        {
            await device.ConnectAsync(lifetime.Token);
            if (!device.Capabilities.SupportsStreaming || device.Capabilities.MaximumZones <= 0)
            {
                throw new DeviceException("This device does not report addressable streaming zones.");
            }

            var active = new ConnectedDevice(device, new DeviceTestFrame(device.Capabilities.MaximumZones));
            connected.Add(profile.Id, active);
            return active;
        }
        catch (Exception)
        {
            await device.DisposeAsync();
            throw;
        }
    }

    private async void OnConnect(object? sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        var profile = await SaveSelectedAsync();
        var active = await ConnectProfileAsync(profile);
        RefreshDevices();
        RefreshZones();
        StatusLabel.Text = $"Connected to {active.Device.Name}. Ready for RGB and LED-order tests.";
    });

    private async Task DisconnectProfileAsync(string id)
    {
        if (connected.Remove(id, out var active))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await active.Device.SetStaticColorAsync(RgbColor.Black, timeout.Token);
            }
            finally
            {
                await active.Device.DisposeAsync();
            }
        }
    }

    private async void OnDisconnect(object? sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await StopWorkerAsync();
        if (selected is { } profile)
        {
            await DisconnectProfileAsync(profile.Id);
        }

        RefreshDevices();
        RefreshZones();
        StatusLabel.Text = "Device disconnected.";
    });

    private async void OnRemove(object? sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        if (selected is { } profile)
        {
            await RemoveProfileAsync(profile.Id);
        }
    });

    private async Task RemoveProfileAsync(string id)
    {
        var resume = audioSession is not null;
        await StopWorkerAsync();
        string? warning = null;
        try
        {
            await DisconnectProfileAsync(id);
        }
        catch (Exception ex)
        {
            warning = ex.Message;
        }

        profiles.RemoveAll(p => p.Id == id);
        await SaveProfilesAsync();
        RefreshDevices();
        SelectProfile(selected?.Id == id ? profiles.FirstOrDefault() : selected);
        if (resume && profiles.Any(p => p.SyncEnabled))
        {
            await StartAudioAsync();
        }

        StatusLabel.Text = warning is null
            ? "Device removed. " + (audioSession is not null ? "Remaining devices are syncing." : "Audio sync stopped.")
            : "Device removed; disconnect reported: " + warning;
    }

    private async void OnPair(object? sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        var profile = await SaveSelectedAsync();
        if (profile.Device.Adapter != "nanoleaf")
        {
            throw new InvalidOperationException("Pairing is available for Nanoleaf devices.");
        }

        var settings = NanoleafSettings.FromDictionary(profile.Device.Settings);
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            throw new InvalidOperationException("Enter a host address before pairing.");
        }

        await StopWorkerAsync();
        await DisconnectProfileAsync(profile.Id);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var token = await NanoleafApi.PairAsync(settings.Host, settings.Port, http, lifetime.Token);
        var secretPath = Path.Combine(ConfigurationPaths.ConfigDirectory, $"nanoleaf-{profile.Id}.local.json");
        await NanoleafAuthentication.SaveTokenAsync(token, secretPath, lifetime.Token);
        var updatedSettings = new Dictionary<string, string>(profile.Device.Settings, StringComparer.Ordinal)
        {
            ["secretsFilePath"] = secretPath,
            ["tokenEnvironmentVariable"] = "NANOLEAF_TOKEN_" + profile.Id.ToUpperInvariant(),
        };
        var updated = profile with { Device = profile.Device with { Settings = updatedSettings } };
        profiles[profiles.FindIndex(p => p.Id == profile.Id)] = updated;
        selected = updated;
        TokenVariableBox.Text = updatedSettings["tokenEnvironmentVariable"];
        await SaveProfilesAsync();
        RefreshDevices();
        RefreshZones();
        StatusLabel.Text = "Paired. Token saved locally with owner-only permissions.";
    });

    private async void OnDiscover(object? sender, RoutedEventArgs e) => await ExecuteAsync(() =>
        DiscoverDevicesAsync("nanoleaf", new Dictionary<string, string>(StringComparer.Ordinal)));

    private async void OnDiscoverOpenRgb(object? sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (SelectedAdapterId == "openrgb")
        {
            SetSetting(settings, "host", HostBox.Text);
            SetSetting(settings, "port", ((int)(PortBox.Value ?? OpenRgbSettings.DefaultPort)).ToString(CultureInfo.InvariantCulture));
        }

        await DiscoverDevicesAsync("openrgb", settings);
    });

    private async Task DiscoverDevicesAsync(string adapterId, IReadOnlyDictionary<string, string> settings)
    {
        try
        {
            var endpoint = adapterId == "openrgb" ? OpenRgbSettings.FromDictionary(settings) : null;
            var message = endpoint is not null
                ? $"Discovering PC components through OpenRGB at {endpoint.Host}:{endpoint.Port}…"
                : "Discovering Nanoleaf devices on the local network…";
            SetDiscoveryStatus(message);
            await AddDiscoveredProfilesAsync(adapterId, settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !lifetime.IsCancellationRequested)
        {
            SetDiscoveryStatus("Discovery failed · " + ex.Message);
            throw;
        }
    }

    private void SetDiscoveryStatus(string message)
    {
        DiscoveryStatusLabel.Text = message;
        StatusLabel.Text = message;
    }

    private async Task AddDiscoveredProfilesAsync(string adapterId, IReadOnlyDictionary<string, string> settings)
    {
        var devices = await DeviceDiscoveryService.DiscoverAsync(adapterId, settings, lifetime.Token);
        if (devices.Count == 0)
        {
            SetDiscoveryStatus(adapterId == "openrgb"
                ? "Connected to OpenRGB, but it reports no components. Check that your hardware appears in OpenRGB's "
                    + "device list, then try discovery again."
                : "No Nanoleaf devices found. Check that your lights are powered on and on the same local network.");
            return;
        }

        var added = 0;
        DeviceProfile? firstAdded = null;
        foreach (var device in devices)
        {
            if (profiles.Any(p => IsSameDiscoveredDevice(p, device)))
            {
                continue;
            }

            var profile = new DeviceProfile
            {
                Name = device.Name,
                Device = device.Device,
                SyncEnabled = device.SupportsStreaming,
            };
            profiles.Add(profile);
            firstAdded ??= profile;
            added++;
        }

        await SaveProfilesAsync();
        RefreshDevices();
        if (firstAdded is not null || selected is null)
        {
            SelectProfile(firstAdded ?? profiles.FirstOrDefault());
        }

        SetDiscoveryStatus($"Found {devices.Count} device(s) · {added} added · {devices.Count - added} already saved. " + (adapterId == "nanoleaf"
            ? "Pair new devices in Device settings."
            : "OpenRGB components need per-LED Direct mode; unsupported components are excluded from sync."));
    }

    private static bool IsSameDiscoveredDevice(DeviceProfile profile, DiscoveredLightDevice device)
    {
        if (profile.Device.Adapter != device.Device.Adapter)
        {
            return false;
        }

        if (device.Device.Adapter == "nanoleaf")
        {
            // Legacy profiles may omit the default port or contain pairing settings.
            return string.Equals(Setting(profile, "host"), device.Device.Settings.GetValueOrDefault("host"),
                StringComparison.Ordinal);
        }

        var keys = new[] { "host", "port", "controllerName", "serial", "location" };
        return keys.All(key => string.Equals(Setting(profile, key) ?? string.Empty,
            device.Device.Settings.GetValueOrDefault(key) ?? string.Empty, StringComparison.Ordinal));
    }

    private async Task RefreshSourcesAsync()
    {
        try
        {
            var sources = (await PulseAudioSources.ListAsync(lifetime.Token)).ToList();
            if (!sources.Contains(configuration.Audio.Source, StringComparer.Ordinal))
            {
                sources.Add(configuration.Audio.Source);
            }

            AudioSourceBox.ItemsSource = sources;
            AudioSourceBox.SelectedItem = configuration.Audio.Source;
        }
        catch (IOException ex)
        {
            StatusLabel.Text = ex.Message + " You can still try the default playback monitor.";
        }
    }

    private async void OnRefreshSources(object? sender, RoutedEventArgs e) => await ExecuteAsync(RefreshSourcesAsync);

    private AudioConfig ReadAudioOptions() => new()
    {
        Source = AudioSourceBox.SelectedItem as string ?? "@DEFAULT_MONITOR@",
        Mode = AudioModeBox.SelectedIndex switch { 1 => "volume", 2 => "bass", 3 => "rainbow", _ => "spectrum" },
        Gain = (double)(GainBox.Value ?? 3),
        Brightness = (double)(BrightnessBox.Value ?? 100) / 100,
        Smoothing = (double)(SmoothingBox.Value ?? 65) / 100,
        NoiseGate = (double)(GateBox.Value ?? 0.005m),
        Motion = (double)(MotionBox.Value ?? 1),
        Color = CurrentAudioColor,
    };

    private RgbColor CurrentAudioColor => new((byte)(AudioRedBox.Value ?? 0),
        (byte)(AudioGreenBox.Value ?? 0), (byte)(AudioBlueBox.Value ?? 0));

    private void RefreshAudioColor() => AudioColorPreview.Background = Brush(CurrentAudioColor);

    private void OnAudioChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (loaded)
        {
            RefreshAudioColor();
            UpdateAudioOptions();
        }
    }

    private void OnAudioModeChanged(object? sender, SelectionChangedEventArgs e) => UpdateAudioOptions();

    private void UpdateAudioOptions()
    {
        if (!loaded || closing)
        {
            return;
        }

        var audio = ReadAudioOptions();
        audioSession?.UpdateOptions(audio);
        configuration = configuration with { Audio = audio };
        audioOptionsDirty = true;
        UpdateControls();
    }

    private async Task SaveOptionsAsync()
    {
        if (audioOptionsDirty || screenOptionsDirty)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await ConfigurationLoader.SaveAsync(ConfigurationPaths.ConfigFile, configuration, timeout.Token);
            audioOptionsDirty = false;
            screenOptionsDirty = false;
        }
    }

    private async void OnStartAudio(object? sender, RoutedEventArgs e) => await ExecuteAsync(StartAudioAsync);

    private async Task StartAudioAsync()
    {
        await StopWorkerAsync();
        var enabled = profiles.Where(p => p.SyncEnabled).ToArray();
        if (enabled.Length == 0)
        {
            throw new InvalidOperationException("Choose at least one device under Sync devices in Audio sync & visualizer.");
        }

        List<ILightDevice> devices = [];
        foreach (var profile in enabled)
        {
            var active = await ConnectProfileAsync(profile);
            if (active.Device is IBrightnessControl brightness)
            {
                await brightness.SetBrightnessAsync(profile.BrightnessPercent, lifetime.Token);
            }

            devices.Add(active.Device);
        }

        var audio = ReadAudioOptions();
        configuration = configuration with { Audio = audio };
        audioOptionsDirty = true;
        await SaveOptionsAsync();
        RefreshDevices();
        var targets = enabled.Select(p => connected[p.Id]).ToArray();
        StatusLabel.Text = $"Audio sync running on {devices.Count} device(s). Tune gain, brightness, response and motion live.";
        BeginWorker(async cancellationToken =>
        {
            await using var capture = new PulseAudioCapture(audio.Source);
            audioSession = new AudioSyncSession(capture, devices, audio, reportEveryFrames: 3);
            await audioSession.RunAsync(status =>
            {
                var gain = configuration.Audio.Gain;
                LevelMeter.Value = Math.Clamp(status.Features.Level * gain, 0, 1);
                visualizer.Update(status, gain);
                AudioMetrics.Text = string.Create(CultureInfo.InvariantCulture,
                    $"{status.Frames} frames · RMS {status.Features.Level:F3} · bass {status.Features.Bass:F3} · mids {status.Features.Mid:F3} · treble {status.Features.Treble:F3}");
            }, cancellationToken);
        }, isExclusive: true, targets);
    }

    private void BeginWorker(Func<CancellationToken, Task> run, bool isExclusive,
        IReadOnlyList<ConnectedDevice> targets)
    {
        workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        exclusive = isExclusive;
        worker = RunWorkerAsync(run, workerCancellation, targets);
        UpdateControls();
    }

    private async Task RunWorkerAsync(Func<CancellationToken, Task> run, CancellationTokenSource cancellation,
        IReadOnlyList<ConnectedDevice> targets)
    {
        try
        {
            await run(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
        finally
        {
            if (exclusive)
            {
                foreach (var active in targets)
                {
                    active.Frame.SetAll(RgbColor.Black);
                }

                visualizer.Clear();
                LevelMeter.Value = 0;
                AudioMetrics.Text = "Stopped";
            }

            audioSession = null;
            if (screenRunning)
            {
                ClearScreenPreview();
            }
            exclusive = false;
            workerCancellation = null;
            cancellation.Dispose();
            UpdateControls();
            RefreshZones();
        }
    }

    private async Task StopWorkerAsync()
    {
        if (workerCancellation is { } cancellation)
        {
            await cancellation.CancelAsync();
        }
        if (worker is { } running)
        {
            await running;
            worker = null;
        }

        await SaveOptionsAsync();
    }

    private async Task ApplyTestAsync(Action<DeviceTestFrame, RgbColor> apply)
    {
        await StopWorkerAsync();
        var profile = selected ?? throw new InvalidOperationException("Select a device first.");
        var active = await ConnectProfileAsync(profile);
        if (active.Device is IBrightnessControl brightness)
        {
            await brightness.SetBrightnessAsync(profile.BrightnessPercent, lifetime.Token);
        }

        apply(active.Frame, CurrentColor);
        await active.Device.SendFrameAsync(active.Frame.Colors, lifetime.Token);
        RefreshDevices();
        RefreshZones();
        HoldTestFrame(active);
        StatusLabel.Text = $"Sending exact RGB to {active.Device.Name}. Test frame refreshed at 10 fps; Stop blacks out the device.";
    }

    private void HoldTestFrame(ConnectedDevice active)
    {
        BeginWorker(async cancellationToken =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await active.Device.SendFrameAsync(active.Frame.Colors, cancellationToken);
            }
        }, isExclusive: false, [active]);
    }

    private async void OnApplyBrightness(object? sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await StopWorkerAsync();
        var profile = selected ?? throw new InvalidOperationException("Select a device first.");
        var percent = (int)Math.Round(DeviceBrightnessSlider.Value);
        var active = await ConnectProfileAsync(profile);
        if (active.Device is not IBrightnessControl brightness)
        {
            throw new DeviceException("This adapter does not implement master brightness control.");
        }

        await brightness.SetBrightnessAsync(percent, lifetime.Token);
        var updated = profile with { BrightnessPercent = percent };
        profiles[profiles.FindIndex(p => p.Id == profile.Id)] = updated;
        selected = updated;
        await SaveProfilesAsync();
        if (ContainsVisibleColor(active.Frame.Colors.Span))
        {
            await active.Device.SendFrameAsync(active.Frame.Colors, lifetime.Token);
            HoldTestFrame(active);
        }

        RefreshDevices();
        RefreshZones();
        StatusLabel.Text = $"{active.Device.Name} device brightness set to {percent}% (0 = off, 100 = full brightness).";
    });

    private static bool ContainsVisibleColor(ReadOnlySpan<RgbColor> colors)
    {
        foreach (var color in colors)
        {
            if (color != RgbColor.Black)
            {
                return true;
            }
        }

        return false;
    }

    private async void OnApplyAll(object? sender, RoutedEventArgs e) =>
        await ExecuteAsync(() => ApplyTestAsync((frame, color) => frame.SetAll(color)));

    private async void OnApplyZone(object? sender, RoutedEventArgs e) =>
        await ExecuteAsync(() => ApplyTestAsync((frame, color) => frame.SetZone(selectedZone, color)));

    private async void OnIsolateZone(object? sender, RoutedEventArgs e) =>
        await ExecuteAsync(() => ApplyTestAsync((frame, color) => frame.Isolate(selectedZone, color)));

    private async void OnChase(object? sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await StopWorkerAsync();
        var profile = selected ?? throw new InvalidOperationException("Select a device first.");
        var active = await ConnectProfileAsync(profile);
        if (active.Device is IBrightnessControl brightness)
        {
            await brightness.SetBrightnessAsync(profile.BrightnessPercent, lifetime.Token);
        }

        var color = CurrentColor;
        if (color == RgbColor.Black)
        {
            throw new InvalidOperationException("Choose a visible colour for the chase test.");
        }

        RefreshDevices();
        BeginWorker(async cancellationToken =>
        {
            try
            {
                for (var index = 0; index < active.Frame.Colors.Length; index++)
                {
                    selectedZone = index;
                    active.Frame.Isolate(index, color);
                    RefreshZones();
                    StatusLabel.Text = $"Chase · LED {index + 1}/{active.Frame.Colors.Length}. Check physical order and channel colour.";
                    for (var tick = 0; tick < 7; tick++)
                    {
                        await active.Device.SendFrameAsync(active.Frame.Colors, cancellationToken);
                        await Task.Delay(100, cancellationToken);
                    }
                }

                StatusLabel.Text = "Chase finished. Enter the verified address order in Device settings if it differs.";
            }
            finally
            {
                active.Frame.SetAll(RgbColor.Black);
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await active.Device.SetStaticColorAsync(RgbColor.Black, shutdown.Token);
            }
        }, isExclusive: true, [active]);
    });

    private async void OnStop(object? sender, RoutedEventArgs e)
    {
        if (workerCancellation is { } cancellation)
        {
            await cancellation.CancelAsync();
        }
        await ExecuteAsync(async () =>
        {
            await StopWorkerAsync();
            List<string> failures = [];
            foreach (var active in connected.Values)
            {
                active.Frame.SetAll(RgbColor.Black);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await active.Device.SetStaticColorAsync(RgbColor.Black, timeout.Token);
                }
                catch (Exception ex)
                {
                    failures.Add($"{active.Device.Name}: {ex.Message}");
                }
            }

            RefreshZones();
            StatusLabel.Text = failures.Count == 0 ? "Stopped · connected devices are black."
                : "Stopped. Could not black out: " + string.Join("; ", failures);
        });
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (canClose)
        {
            return;
        }

        e.Cancel = true;
        if (closing)
        {
            return;
        }

        closing = true;
        await DisposeAsync();
        canClose = true;
        Close();
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        UpdateControls();
        while (busy)
        {
            await Task.Delay(25);
        }

        try
        {
            await StopWorkerAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Could not save sync settings: " + ex.Message;
        }

        workerCancellation?.Dispose();
        foreach (var id in connected.Keys.ToArray())
        {
            try
            {
                await DisconnectProfileAsync(id);
            }
            catch (Exception)
            {
                // Dispose the remaining devices even when one is no longer reachable.
            }
        }

        screenPreviewTimer.Stop();
        screenVisualizer.Dispose();
        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record ConnectedDevice(ILightDevice Device, DeviceTestFrame Frame);
}
