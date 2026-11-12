using LightSync.Application;
using LightSync.Cli.Commands;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Cli.Tests;

public sealed class CommandParityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("run --source screen --demo --preview --seconds 1 --set screen.brightness=0.4 --set screen.audioIntensityEnabled=false")]
    [InlineData("devices update --device lamp --name Lamp --setting host=127.0.0.1 --setting port=16021 --enabled false")]
    [InlineData("devices show --device lamp")]
    [InlineData("test-color 255,16,3 --zone 2 --isolate --seconds 1 --device lamp")]
    [InlineData("test-color black --led 1=red --led 2=17,29,241")]
    [InlineData("test-chase --colour #0080FF --milliseconds 50 --device lamp")]
    [InlineData("brightness 75 --device lamp")]
    [InlineData("run --device first --device second --audio-source playback.monitor")]
    [InlineData("run --source ambient --preview --seconds 1 --set lighting.mode=pulse --set lighting.speed=1.5")]
    [InlineData("run --source ambient --device first --device second")]
    [InlineData("config set screen.audioIntensityEnabled false --config /tmp/isolated-light-sync.json")]
    [InlineData("config set lighting.color 128,64,255 --config /tmp/isolated-light-sync.json")]
    public void NewCommandsAndGlobalOptionsParse(string command)
    {
        Assert.Empty(Program.CreateCommand().Parse(command).Errors);
    }

    [Theory]
    [InlineData("red", 255, 0, 0)]
    [InlineData("#0A80FF", 10, 128, 255)]
    [InlineData("10,128,255", 10, 128, 255)]
    public void ExactColoursPreserveAllChannels(string value, byte red, byte green, byte blue)
    {
        Assert.Equal(new RgbColor(red, green, blue), ConfigCommand.ParseColor(value));
    }

    [Theory]
    [InlineData("256,0,0")]
    [InlineData("#FFFF")]
    [InlineData("yellow")]
    public void InvalidExactColoursFailBeforeContactingDevices(string value)
    {
        Assert.Throws<ConfigurationException>(() => ConfigCommand.ParseColor(value));
    }

    [Fact]
    public void TuningCoversBothFrontendsAndKeepsTheModesIndependent()
    {
        var config = ConfigCommand.Apply(new AppConfig(),
        [
            "audio.source=music.monitor", "audio.mode=rainbow", "audio.gain=15", "audio.brightness=0.6",
            "audio.motion=1.5", "audio.smoothing=0.35", "audio.noiseGate=0.005", "audio.color=12,34,56",
            "lighting.mode=pulse", "lighting.color=128,64,255", "lighting.brightness=0.8",
            "lighting.speed=2", "lighting.direction=right-to-left",
            "screen.brightness=0.4", "screen.audioIntensityEnabled=false", "screen.audioSource=screen.monitor",
            "capture.fps=60", "mapping.zoneCount=3", "mapping.layout=horizontal", "mapping.direction=top-to-bottom",
            "mapping.reverse=true", "mapping.customOrder=[2,1,0]", "processing.gamma=0.7",
            "processing.saturation=1.5", "processing.smoothing=0.1", "processing.blackLevel=0.02",
            "processing.averaging=colour-weighted",
        ]);
        Assert.Equal("rainbow", config.Audio.Mode);
        Assert.Equal(15, config.Audio.Gain);
        Assert.Equal(0.6, config.Audio.Brightness);
        Assert.Equal(1.5, config.Audio.Motion);
        Assert.Equal(0.35, config.Audio.Smoothing);
        Assert.Equal(new RgbColor(12, 34, 56), config.Audio.Color);
        Assert.Equal("pulse", config.Lighting.Mode);
        Assert.Equal(new RgbColor(128, 64, 255), config.Lighting.Color);
        Assert.Equal(0.8, config.Lighting.Brightness);
        Assert.Equal(2, config.Lighting.Speed);
        Assert.Equal("right-to-left", config.Lighting.Direction);
        Assert.Equal(0.4, config.Screen.Brightness);
        Assert.False(config.Screen.AudioIntensityEnabled);
        Assert.Equal("screen.monitor", config.Screen.AudioSource);
        Assert.Equal("music.monitor", config.Audio.Source);
        Assert.Equal(60, config.Capture.Fps);
        Assert.Equal([2, 1, 0], config.Mapping.CustomOrder!);
        Assert.Equal(0.7, config.Processing.Gamma);
        Assert.Equal("colour-weighted", config.Processing.Averaging);
    }

    [Theory]
    [InlineData("screen.brightness=1.1")]
    [InlineData("audio.gain=0")]
    [InlineData("audio.mode=unknown")]
    [InlineData("audio.smoothing=1")]
    [InlineData("capture.fps=241")]
    [InlineData("screen.typo=true")]
    [InlineData("lighting.mode=unknown")]
    [InlineData("lighting.brightness=1.1")]
    [InlineData("lighting.speed=3.1")]
    [InlineData("lighting.direction=up")]
    [InlineData("lighting.color=300,0,0")]
    [InlineData("lighting.typo=true")]
    [InlineData("audio")]
    public void BadTuningIsRejected(string assignment)
    {
        Assert.Throws<ConfigurationException>(() => ConfigCommand.Apply(new AppConfig(), [assignment]));
    }

    [Theory]
    [InlineData("lighting.brightness=0", 0)]
    [InlineData("lighting.brightness=1", 1)]
    [InlineData("lighting.speed=0", 0)]
    [InlineData("lighting.speed=3", 3)]
    public void LightingRangesAcceptTheirBoundaries(string assignment, double expected)
    {
        var config = ConfigCommand.Apply(new AppConfig(), [assignment]);

        var actual = assignment.StartsWith("lighting.brightness", StringComparison.Ordinal)
            ? config.Lighting.Brightness
            : config.Lighting.Speed;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ConfigSetRepairsAnInvalidStoredValue()
    {
        using var files = new TestFiles();
        await File.WriteAllTextAsync(files.Config, """{ "lighting": { "mode": "strobe" } }""", Token);
        var context = new CommandContext(files.Config);

        Assert.Equal(0, await ConfigCommand.SetAsync(context, "lighting.mode", "static", Token));

        var stored = await ConfigurationLoader.LoadValidatedAsync(files.Config, Token);
        Assert.Equal("static", stored.Lighting.Mode);
    }

    [Fact]
    public void BlackIsAValidLightingColourAndOffSwitch()
    {
        var config = ConfigCommand.Apply(new AppConfig(), ["lighting.color=black"]);

        Assert.Equal(RgbColor.Black, config.Lighting.Color);
    }

    [Fact]
    public async Task RunRejectsUnknownSourcesAndScreenOnlyOptionsBeforeStarting()
    {
        using var files = new TestFiles();
        var context = new CommandContext(files.Config);

        await Assert.ThrowsAsync<ConfigurationException>(() =>
            RunCommand.RunAsync(context, new RunOptions { Source = "strobe" }, Token));
        await Assert.ThrowsAsync<ConfigurationException>(() =>
            RunCommand.RunAsync(context, new RunOptions { Source = "ambient", Demo = true }, Token));
    }

    [Fact]
    public void SelectedLedUsesOneBasedIndexAndIsolationBlacksOutTheRest()
    {
        var color = new RgbColor(1, 2, 3);
        Assert.Equal([RgbColor.Black, color, RgbColor.Black],
            TestCommands.BuildTestFrame(3, color, 2, true, ColorConstants.White).Colors.ToArray());
        Assert.Equal([ColorConstants.White, color, ColorConstants.White],
            TestCommands.BuildTestFrame(3, color, 2, false, ColorConstants.White).Colors.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => TestCommands.BuildTestFrame(3, color, 4, true, RgbColor.Black));
    }

    [Fact]
    public void IndividualLedChangesPreserveOtherRequestedChannels()
    {
        var frame = TestCommands.BuildTestFrame(3, ColorConstants.White, null, false, RgbColor.Black);
        TestCommands.ApplyLedChanges(frame, ["1=red", "2=17,29,241"]);
        Assert.Equal([ColorConstants.Red, new RgbColor(17, 29, 241), ColorConstants.White], frame.Colors.ToArray());
        Assert.Throws<ConfigurationException>(() => TestCommands.ApplyLedChanges(frame, ["0=red"]));
    }

    [Fact]
    public async Task DryRunCannotSilentlyApplyDeviceMutations()
    {
        using var files = new TestFiles();
        var parse = Program.CreateCommand().Parse(["devices", "add", "--dry-run", "--config", files.Config]);
        Assert.Equal(1, await parse.InvokeAsync(cancellationToken: Token));
        Assert.False(File.Exists(Path.Combine(files.Directory, "devices.json")));
    }

    [Fact]
    public async Task CliCrudUsesTheDesktopStoreAndKeepsLegacyImportIdsStable()
    {
        using var files = new TestFiles();
        var context = new CommandContext(files.Config);
        var initial = Assert.Single(await context.LoadProfilesAsync(Token));
        Assert.Equal(initial.Id, Assert.Single(await context.LoadProfilesAsync(Token)).Id);
        Assert.Equal(0, await ProfileCommands.SaveAsync(context, true, "PC", "openrgb",
            ["host=localhost", "port=6742", "controllerName=Keyboard", "serial=serial-1"], [], false, 80, Token));
        var desktop = await DeviceProfileStore.LoadAsync(context.ProfilesPath, new DeviceConfig(), Token);
        var pc = Assert.Single(desktop, p => p.Name == "PC");
        Assert.False(pc.SyncEnabled);
        Assert.Equal(80, pc.BrightnessPercent);
        Assert.Equal("Keyboard", pc.Device.Settings["controllerName"]);
        var selected = new CommandContext(files.Config, deviceSelectors: [pc.Id]);
        await ProfileCommands.SaveAsync(selected, false, "Renamed", null, ["location=USB"], ["serial"], true, null, Token);
        var updated = await selected.LoadSelectedProfileAsync(Token);
        Assert.Equal("Renamed", updated.Name);
        Assert.True(updated.SyncEnabled);
        Assert.Equal("USB", updated.Device.Settings["location"]);
        Assert.False(updated.Device.Settings.ContainsKey("serial"));
        await ProfileCommands.RemoveAsync(selected, Token);
        Assert.Equal(initial.Id, Assert.Single(await context.LoadProfilesAsync(Token)).Id);
    }

    [Fact]
    public void DefaultSyncSelectionSkipsDisabledProfilesAndExplicitSelectionIsExact()
    {
        var first = new DeviceProfile { Name = "Same" };
        var second = new DeviceProfile { Name = "Same", SyncEnabled = false };
        var context = new CommandContext("/tmp/test-config.json");
        Assert.Equal([first], context.SelectProfiles([first, second], true));
        var explicitSelection = new CommandContext("/tmp/test-config.json", deviceSelectors: [second.Id]);
        Assert.Equal([second], explicitSelection.SelectProfiles([first, second], true));
        var ambiguous = new CommandContext("/tmp/test-config.json", deviceSelectors: ["Same"]);
        Assert.Throws<ConfigurationException>(() => ambiguous.SelectProfiles([first, second], true));
        Assert.Throws<ConfigurationException>(() => context.SelectProfiles([first, second], false));
    }

    [Fact]
    public async Task InvalidProfileAndSecretSettingsDoNotChangeTheStore()
    {
        using var files = new TestFiles();
        var context = new CommandContext(files.Config);
        await context.LoadProfilesAsync(Token);
        var before = await File.ReadAllTextAsync(context.ProfilesPath, Token);
        await Assert.ThrowsAsync<ConfigurationException>(() => ProfileCommands.SaveAsync(context, true,
            "Invalid", "fake", [], [], null, 101, Token));
        await Assert.ThrowsAsync<ConfigurationException>(() => ProfileCommands.SaveAsync(context, true,
            "Secret", "nanoleaf", ["token=do-not-store"], [], null, null, Token));
        Assert.Equal(before, await File.ReadAllTextAsync(context.ProfilesPath, Token));
    }

    [Fact]
    public async Task NestedConfigCommandPersistsWithoutChangingProfiles()
    {
        using var files = new TestFiles();
        var parse = Program.CreateCommand().Parse(["config", "set", "audio.motion", "1.5", "--config", files.Config]);
        Assert.Equal(0, await parse.InvokeAsync(cancellationToken: Token));
        var stored = await ConfigurationLoader.LoadValidatedAsync(files.Config, Token);
        Assert.Equal(1.5, stored.Audio.Motion);
        Assert.Equal(0.75, stored.Screen.Brightness);
        Assert.False(File.Exists(Path.Combine(files.Directory, "devices.json")));
    }
}

internal sealed class TestFiles : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "light-sync-cli-tests-" + Guid.NewGuid().ToString("N"));
    public string Config => Path.Combine(Directory, "config.json");

    public TestFiles() => System.IO.Directory.CreateDirectory(Directory);

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}
