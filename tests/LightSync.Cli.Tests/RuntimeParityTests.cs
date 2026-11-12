using System.Collections.Concurrent;
using System.Globalization;
using LightSync.Application;
using LightSync.Cli.Commands;
using LightSync.Core.Audio;
using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;
using LightSync.Core.Pipeline;

namespace LightSync.Cli.Tests;

public sealed class RuntimeParityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SharedScreenSessionUsesFullDeviceCountsAndRestoresMasterBeforeBlackout()
    {
        var config = new AppConfig
        {
            Screen = new() { AudioIntensityEnabled = false },
            Processing = new() { Brightness = 0.1, Smoothing = 0, BlackLevel = 0 },
        };
        var brightness = new ScreenBrightness(config.Screen);
        await using var first = new RecordingDevice("First", 3);
        await using var second = new RecordingDevice("Second", 7);
        var profiles = new[] { new DeviceProfile { BrightnessPercent = 63 }, new DeviceProfile { BrightnessPercent = 21 } };
        var firstProcessor = ScreenSyncSession.BuildProcessor(config, 3, brightness);
        var secondProcessor = ScreenSyncSession.BuildProcessor(config, 7, brightness);
        var white = new CapturedFrame(new byte[] { 255, 255, 255, 255 }, 1, 1, 1, TimeSpan.Zero);
        var firstColors = new RgbColor[3];
        var secondColors = new RgbColor[7];
        firstProcessor.Process(white, firstColors);
        secondProcessor.Process(white, secondColors);
        Assert.All(firstColors.Concat(secondColors), color => Assert.Equal(new RgbColor(191, 191, 191), color));

        await using var capture = new SyntheticScreenCapture { FrameLimit = 1 };
        var metrics = new PipelineMetrics();
        var session = new ScreenSyncSession(capture,
            [new(firstProcessor, first), new(secondProcessor, second)], profiles, brightness, metrics,
            () => throw new InvalidOperationException("Disabled intensity must never create audio capture."));
        await Assert.ThrowsAsync<CaptureException>(() => session.RunAsync(Request(), false, Token));
        Assert.Equal(1, metrics.CapturedFrames);
        Assert.Equal(3, first.Frames.Single().Length);
        Assert.Equal(7, second.Frames.Single().Length);
        Assert.Equal("master:100", first.Events[0]);
        Assert.Equal(["master:63", "black"], first.Events.TakeLast(2));
        Assert.Equal(["master:21", "black"], second.Events.TakeLast(2));
        Assert.Null(session.AudioMonitor);
    }

    [Fact]
    public async Task CliScreenDemoUsesEnabledProfilesAndHandlesMissingPlaybackWithoutStopping()
    {
        using var files = new TestFiles();
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, adapters: factory);
        await context.SaveProfilesAsync([Profile("First", 3, 60), Profile("Second", 7, 80),
            Profile("Disabled", 2, 20) with { SyncEnabled = false }], Token);
        Assert.Equal(0, await RunCommand.RunAsync(context, new RunOptions
        {
            Source = "screen", Demo = true, Seconds = 1, Watch = false,
        }, Token, _ => throw new IOException("Playback unavailable")));
        Assert.Equal(2, factory.Devices.Count);
        var first = Assert.Single(factory.For("First"));
        var second = Assert.Single(factory.For("Second"));
        Assert.NotEmpty(first.Frames);
        Assert.All(first.Frames, frame => Assert.Equal(3, frame.Length));
        Assert.All(second.Frames, frame => Assert.Equal(7, frame.Length));
        Assert.Equal(["master:60", "black", "black", "dispose"], first.Events.TakeLast(4));
        Assert.Equal(["master:80", "black", "black", "dispose"], second.Events.TakeLast(4));
        Assert.False(File.Exists(context.PidPath));
    }

    [Fact]
    public async Task PreviewNeverCreatesDeviceConnectionsOrImportsProfiles()
    {
        using var files = new TestFiles();
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, adapters: factory);
        Assert.Equal(0, await RunCommand.RunAsync(context, new RunOptions
        {
            Source = "screen", Demo = true, Preview = true, Seconds = 1, Watch = false,
            Settings = ["screen.audioIntensityEnabled=false"],
        }, Token));
        Assert.Empty(factory.Devices);
        Assert.False(File.Exists(context.ProfilesPath));
        Assert.False(File.Exists(context.PidPath));
    }

    [Fact]
    public async Task CliAmbientRunHoldsTheColourAndRestoresMasterBeforeBlackout()
    {
        using var files = new TestFiles();
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, adapters: factory);
        await context.SaveAsync(new AppConfig
        {
            Lighting = new() { Mode = "static", Color = ColorConstants.Red, Brightness = 1 },
        }, Token);
        await context.SaveProfilesAsync([Profile("First", 3, 60), Profile("Second", 7, 80),
            Profile("Disabled", 2, 20) with { SyncEnabled = false }], Token);

        Assert.Equal(0, await RunCommand.RunAsync(context, new RunOptions
        {
            Source = "ambient", Seconds = 1, Watch = false,
        }, Token));

        Assert.Equal(2, factory.Devices.Count);
        var first = Assert.Single(factory.For("First"));
        var second = Assert.Single(factory.For("Second"));
        Assert.NotEmpty(first.Frames);
        Assert.All(first.Frames, frame =>
        {
            Assert.Equal(3, frame.Length);
            Assert.All(frame, color => Assert.Equal(ColorConstants.Red, color));
        });
        Assert.All(second.Frames, frame =>
        {
            Assert.Equal(7, frame.Length);
            Assert.All(frame, color => Assert.Equal(ColorConstants.Red, color));
        });
        Assert.Equal("master:100", first.Events[0]);
        Assert.Equal(["master:60", "black", "black", "dispose"], first.Events.TakeLast(4));
        Assert.Equal(["master:80", "black", "black", "dispose"], second.Events.TakeLast(4));
        Assert.False(File.Exists(context.PidPath));
    }

    [Fact]
    public async Task AmbientPreviewNeverCreatesDeviceConnectionsOrImportsProfiles()
    {
        using var files = new TestFiles();
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, adapters: factory);
        await context.SaveAsync(new AppConfig
        {
            Lighting = new() { Mode = "rainbow", Speed = 1, Brightness = 0.5 },
        }, Token);

        Assert.Equal(0, await RunCommand.RunAsync(context, new RunOptions
        {
            Source = "ambient", Preview = true, Seconds = 1, Watch = false,
        }, Token));

        Assert.Empty(factory.Devices);
        Assert.False(File.Exists(context.ProfilesPath));
        Assert.False(File.Exists(context.PidPath));
    }

    [Fact]
    public async Task AmbientLiveTuningAppliesColourChangesWithoutReconnecting()
    {
        using var files = new TestFiles();
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, adapters: factory);
        await context.SaveAsync(new AppConfig
        {
            Lighting = new() { Mode = "static", Color = ColorConstants.Red, Brightness = 1 },
        }, Token);
        await context.SaveProfilesAsync([Profile("Lamp", 3, 100)], Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var running = RunCommand.RunAsync(context, new RunOptions { Source = "ambient" }, deadline.Token);
        try
        {
            await WaitUntilAsync(() => factory.For("Lamp").Any(d => d.Frames.Any(f => f.All(c => c == ColorConstants.Red))));

            await ConfigCommand.SetAsync(context, "lighting.color", "blue", Token);

            await WaitUntilAsync(() => factory.For("Lamp").Any(d => d.Frames[^1].All(c => c == ColorConstants.Blue)));
            Assert.Single(factory.For("Lamp")); // Live tuning does not reopen connections.
        }
        finally
        {
            await deadline.CancelAsync();
            Assert.Equal(0, await running);
        }

        Assert.False(File.Exists(context.PidPath));
    }

    [Fact]
    public async Task AmbientProfileChangeRestartsOnTheNewSelection()
    {
        using var files = new TestFiles();
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, adapters: factory);
        var first = Profile("First", 3, 60);
        var second = Profile("Second", 7, 80);
        await context.SaveAsync(new AppConfig
        {
            Lighting = new() { Mode = "static", Color = ColorConstants.Red, Brightness = 1 },
        }, Token);
        await context.SaveProfilesAsync([first], Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var running = RunCommand.RunAsync(context, new RunOptions { Source = "ambient" }, deadline.Token);
        try
        {
            await WaitUntilAsync(() => factory.For("First").Any(d => d.Frames.Length > 0));

            await context.SaveProfilesAsync([second], Token);

            await WaitUntilAsync(() => factory.For("Second").Any(d => d.Frames.Length > 0));
            Assert.Contains("dispose", factory.For("First").Single().Events);
            Assert.Equal("master:100", factory.For("Second").Single().Events[0]);
        }
        finally
        {
            await deadline.CancelAsync();
            Assert.Equal(0, await running);
        }
    }

    [Fact]
    public async Task PreviewDoesNotTakeThePidLock()
    {
        using var files = new TestFiles();
        var context = new CommandContext(files.Config);
        await context.SaveAsync(new AppConfig
        {
            Lighting = new() { Mode = "static", Color = ColorConstants.Red },
        }, Token);
        var runningPid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(context.PidPath, runningPid, Token);
        try
        {
            Assert.Equal(0, await RunCommand.RunAsync(context, new RunOptions
            {
                Source = "ambient", Preview = true, Seconds = 1, Watch = false,
            }, Token));
            Assert.Equal(runningPid, await File.ReadAllTextAsync(context.PidPath, Token));
        }
        finally
        {
            File.Delete(context.PidPath);
        }
    }

    [Fact]
    public async Task ZeroZoneDeviceReportsTheCapabilityProblem()
    {
        using var files = new TestFiles();
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, adapters: factory);
        await context.SaveProfilesAsync([Profile("Zero", 0, 100)], Token);

        var error = await Assert.ThrowsAsync<DeviceException>(() => RunCommand.RunAsync(context,
            new RunOptions { Source = "ambient", Seconds = 1, Watch = false }, Token));

        Assert.Contains("no addressable zones", error.Message);
        Assert.False(File.Exists(context.PidPath));
    }

    [Fact]
    public async Task LiveAudioTuningAndDeviceRemovalMatchDesktopBehavior()
    {
        using var files = new TestFiles();
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, adapters: factory);
        var first = Profile("First", 3, 60);
        var second = Profile("Second", 7, 80);
        await context.SaveAsync(new AppConfig { Audio = new() { Mode = "volume", Smoothing = 0, Color = ColorConstants.Red } }, Token);
        await context.SaveProfilesAsync([first, second], Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var running = RunCommand.RunAsync(context, new RunOptions(), deadline.Token, _ => new GeneratedAudio());
        try
        {
            await WaitUntilAsync(() => factory.For("Second").Any(d => d.Frames.Length > 0));
            await ConfigCommand.SetAsync(context, "audio.brightness", "0", Token);
            await WaitUntilAsync(() => factory.For("First").Any(d => d.Frames is { Length: > 0 } frames && frames[^1].All(c => c == RgbColor.Black)));
            Assert.Single(factory.For("First")); // Live tuning does not reopen connections.
            await context.SaveProfilesAsync([first], Token);
            await WaitUntilAsync(() => factory.For("First").Length == 2);
            Assert.Contains("dispose", factory.For("Second").Single().Events);
            await context.SaveProfilesAsync([], Token);
            Assert.Equal(0, await running.WaitAsync(TimeSpan.FromSeconds(5), Token));
            Assert.All(factory.Devices, device => Assert.Equal(["black", "dispose"], device.Events.TakeLast(2)));
            Assert.False(File.Exists(context.PidPath));
        }
        finally
        {
            await deadline.CancelAsync();
            await running;
        }
    }

    [Fact]
    public async Task RemovingExplicitlySelectedProfileStopsAndReleasesIt()
    {
        using var files = new TestFiles();
        var profile = Profile("Chosen", 3, 30);
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, deviceSelectors: [profile.Id], adapters: factory);
        await context.SaveProfilesAsync([profile], Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var running = RunCommand.RunAsync(context, new RunOptions(), deadline.Token, _ => new GeneratedAudio());
        try
        {
            await WaitUntilAsync(() => factory.For("Chosen").Any(d => d.Frames.Length > 0));
            await context.SaveProfilesAsync([], Token);
            Assert.Equal(0, await running.WaitAsync(TimeSpan.FromSeconds(3), Token));
            Assert.Equal("dispose", factory.For("Chosen").Single().Events[^1]);
        }
        finally
        {
            await deadline.CancelAsync();
            await running;
        }
    }

    [Fact]
    public async Task ChaseUsesExactChannelsVisitsEveryLedAndBlacksOut()
    {
        using var files = new TestFiles();
        var factory = new RecordingFactory();
        var context = new CommandContext(files.Config, adapters: factory);
        await context.SaveProfilesAsync([Profile("Lamp", 3, 42)], Token);
        Assert.Equal(0, await TestCommands.ChaseAsync(context, "17,29,241", 1, Token));
        var device = Assert.Single(factory.Devices);
        Assert.Equal(3, device.Frames.Length);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(new RgbColor(17, 29, 241), device.Frames[i][i]);
            Assert.Equal(1, device.Frames[i].Count(c => c != RgbColor.Black));
        }
        Assert.Equal("master:42", device.Events[0]);
        Assert.Equal(["black", "dispose"], device.Events.TakeLast(2));
    }

    private static CaptureRequest Request() => new(0, new CaptureArea(0, 0, 20, 10), 30, 20, 10);

    private static DeviceProfile Profile(string name, int zones, int brightness) => new()
    {
        Name = name, BrightnessPercent = brightness,
        Device = new() { Adapter = "fake", Settings = new() { ["name"] = name, ["zones"] = zones.ToString(CultureInfo.InvariantCulture) } },
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    private sealed class GeneratedAudio : IAudioCapture
    {
        private readonly float[] samples = Enumerable.Range(0, 1024).Select(i => (float)(0.2 * Math.Sin(i * 0.1))).ToArray();
        public int SampleRate => 48000;
        public int Channels => 1;
        public int SamplesPerChannel => 1024;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task<ReadOnlyMemory<float>> ReadAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken);
            return samples;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingFactory : IDeviceAdapterFactory
    {
        public ConcurrentBag<RecordingDevice> Devices { get; } = [];
        public IReadOnlyList<DeviceAdapterDescriptor> AvailableAdapters { get; } = [new("fake", "Recorded", "Test", true)];
        public RecordingDevice[] For(string name) => Devices.Where(d => d.Name == name).ToArray();
        public ILightDevice Create(string adapterId, IReadOnlyDictionary<string, string> settings)
        {
            var device = new RecordingDevice(settings["name"], int.Parse(settings["zones"], CultureInfo.InvariantCulture));
            Devices.Add(device);
            return device;
        }
    }

    private sealed class RecordingDevice(string name, int count) : ILightDevice, IBrightnessControl
    {
        private readonly ConcurrentQueue<string> events = [];
        private readonly ConcurrentQueue<RgbColor[]> frames = [];
        public string Name => name;
        public DeviceCapabilities Capabilities { get; } = new(count, true, true, true, false, true);
        public int BrightnessPercent { get; private set; } = 100;
        public string[] Events => events.ToArray();
        public RgbColor[][] Frames => frames.ToArray();
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetBrightnessAsync(int percent, CancellationToken cancellationToken)
        {
            BrightnessPercent = percent;
            events.Enqueue("master:" + percent.ToString(CultureInfo.InvariantCulture));
            return Task.CompletedTask;
        }
        public Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken)
        {
            frames.Enqueue(colors.ToArray());
            events.Enqueue("frame");
            return Task.CompletedTask;
        }
        public Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken)
        {
            Assert.Equal(RgbColor.Black, color);
            events.Enqueue("black");
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            events.Enqueue("dispose");
            return ValueTask.CompletedTask;
        }
    }
}
