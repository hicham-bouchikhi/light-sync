using LightSync.Core.Audio;
using LightSync.Core.Colors;
using LightSync.Core.Configuration;
using LightSync.Core.Devices;

namespace LightSync.Core.Tests;

public sealed class AudioTests
{
    private const int Size = 1024;
    private const int Rate = 48000;

    [Theory]
    [InlineData(93.75, 0)]
    [InlineData(750, 1)]
    [InlineData(6000, 2)]
    public void SeparatesBassMidAndTreble(double frequency, int band)
    {
        var analyzer = new AudioAnalyzer(Rate, Size);
        var result = analyzer.Analyze(Tone(frequency), 2);
        var energies = new[] { result.Bass, result.Mid, result.Treble };
        Assert.InRange(result.Level, 0.34, 0.36);
        Assert.InRange(energies[band], 0.34, 0.36);
        for (var i = 0; i < energies.Length; i++)
        {
            if (i != band)
            {
                Assert.True(energies[i] < 0.002);
            }
        }
    }

    [Fact]
    public void OppositeStereoPhasesDoNotCancelTheLighting()
    {
        var normal = new AudioAnalyzer(Rate, Size).Analyze(Tone(93.75), 2);
        var reversed = new AudioAnalyzer(Rate, Size).Analyze(Tone(93.75, reverseRight: true), 2);
        Assert.Equal(normal, reversed);
    }

    [Fact]
    public void SilenceAndDcOffsetProduceNoEnergy()
    {
        var samples = new float[Size * 2];
        samples.AsSpan().Fill(0.75f);
        Assert.Equal(default, new AudioAnalyzer(Rate, Size).Analyze(samples, 2));
        samples.AsSpan().Clear();
        Assert.Equal(default, new AudioAnalyzer(Rate, Size).Analyze(samples, 2));
    }

    [Fact]
    public void InvalidSamplesDoNotPoisonTheSpectrum()
    {
        var samples = new float[Size * 2];
        samples[0] = float.NaN;
        samples[1] = float.PositiveInfinity;
        Assert.Equal(default, new AudioAnalyzer(Rate, Size).Analyze(samples, 2));
    }

    [Fact]
    public void RejectsMismatchedBlockSizes()
    {
        var analyzer = new AudioAnalyzer(Rate, Size);
        Assert.Throws<ArgumentException>(() => analyzer.Analyze(new float[17], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioAnalyzer(Rate, 1000));
    }

    [Fact]
    public void SpectrumUsesAllThreeBandsOnASingleZoneDevice()
    {
        var mapper = new AudioColorMapper(new AudioConfig { Gain = 1, Smoothing = 0 });
        var colors = new RgbColor[1];
        mapper.Map(new AudioFeatures(1, 1, 0.5, 0.25), colors);
        Assert.Equal(new RgbColor(255, 128, 64), colors[0]);
    }

    [Fact]
    public void SpectrumRunsFromBassRedThroughMidGreenToTrebleBlue()
    {
        var mapper = new AudioColorMapper(new AudioConfig { Gain = 1, Smoothing = 0 });
        var colors = new RgbColor[3];
        mapper.Map(new AudioFeatures(1, 1, 1, 1), colors);
        Assert.Equal(new[] { new RgbColor(255, 0, 0), new RgbColor(0, 255, 0), new RgbColor(0, 0, 255) }, colors);
    }

    [Theory]
    [InlineData("volume", 0.5)]
    [InlineData("bass", 0.25)]
    public void FixedColourModesFollowTheSelectedEnergy(string mode, double energy)
    {
        var mapper = new AudioColorMapper(new AudioConfig
        {
            Mode = mode, Gain = 1, Smoothing = 0, Color = new RgbColor(200, 100, 40),
        });
        var colors = new RgbColor[4];
        mapper.Map(new AudioFeatures(0.5, 0.25, 0, 0), colors);
        Assert.All(colors, color => Assert.Equal(new RgbColor((byte)(200 * energy), (byte)(100 * energy), (byte)(40 * energy)), color));
    }

    [Fact]
    public void NoiseGateAndSmoothingEventuallyReachExactBlack()
    {
        var mapper = new AudioColorMapper(new AudioConfig { Smoothing = 0.95 });
        var colors = new[] { new RgbColor(255, 255, 255) };
        for (var i = 0; i < 300; i++)
        {
            mapper.Map(new AudioFeatures(0.001, 1, 1, 1), colors);
        }

        Assert.Equal(RgbColor.Black, colors[0]);
    }

    [Fact]
    public void MapsEqualAudioAcrossDevicesWithDifferentLengthsAndCleansUpOnEof()
    {
        var shortFrame = new RgbColor[3];
        var longFrame = new RgbColor[5];
        var mapper = new AudioColorMapper(new AudioConfig { Gain = 1, Smoothing = 0 });
        var feature = new AudioFeatures(1, 1, 1, 1);
        mapper.Map(feature, shortFrame);
        mapper.Map(feature, longFrame);
        Assert.Equal(shortFrame[0], longFrame[0]);
        Assert.Equal(shortFrame[1], longFrame[2]);
        Assert.Equal(shortFrame[2], longFrame[4]);
    }

    [Fact]
    public async Task SessionSendsFullFramesToEveryDeviceAndBlacksOutOnSourceFailure()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var first = new FakeDevice(3);
        await using var second = new FakeDevice(7);
        await first.ConnectAsync(cancellation.Token);
        await second.ConnectAsync(cancellation.Token);
        await using var capture = new FiniteAudioCapture();
        var session = new AudioSyncSession(capture, [first, second], new AudioConfig { Smoothing = 0 });
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(null, cancellation.Token));
        Assert.Equal(3, first.GetLastFrame()!.Length);
        Assert.Equal(7, second.GetLastFrame()!.Length);
        Assert.Equal(RgbColor.Black, first.LastStaticColor);
        Assert.Equal(RgbColor.Black, second.LastStaticColor);
    }

    [Fact]
    public async Task SessionReportsSpectrumAndRgbAndAppliesLiveOptionsToNextFrame()
    {
        var token = TestContext.Current.CancellationToken;
        await using var device = new FakeDevice(3);
        await device.ConnectAsync(token);
        await using var capture = new FiniteAudioCapture(11);
        var options = new AudioConfig { Mode = "rainbow", Smoothing = 0 };
        var session = new AudioSyncSession(capture, [device], options);
        var reported = false;
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(status =>
        {
            Assert.Equal(10, status.Frames);
            Assert.Equal(32, status.Spectrum.Length);
            Assert.Equal(3, status.Colors.Length);
            Assert.Contains(status.Colors.ToArray(), color => color != RgbColor.Black);
            session.UpdateOptions(options with { Brightness = 0 });
            reported = true;
        }, token));
        Assert.True(reported);
        Assert.Equal(11, device.FrameCount);
        Assert.All(device.GetLastFrame()!, color => Assert.Equal(RgbColor.Black, color));
    }

    [Fact]
    public async Task SessionCleansUpEarlierDevicesWhenALaterSendFails()
    {
        var token = TestContext.Current.CancellationToken;
        await using var first = new FakeDevice(3);
        await first.ConnectAsync(token);
        await using var broken = new BrokenDevice();
        await using var capture = new FiniteAudioCapture();
        var session = new AudioSyncSession(capture, [first, broken], new AudioConfig());
        await Assert.ThrowsAsync<DeviceException>(() => session.RunAsync(null, token));
        Assert.Equal(1, first.FrameCount);
        Assert.Equal(RgbColor.Black, first.LastStaticColor);
        Assert.True(broken.BlackoutAttempted);
    }

    [Fact]
    public async Task CancelledSessionStillBlacksOutDevices()
    {
        await using var first = new FakeDevice(3);
        await first.ConnectAsync(TestContext.Current.CancellationToken);
        await using var capture = new FiniteAudioCapture();
        var session = new AudioSyncSession(capture, [first], new AudioConfig());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync(null, new CancellationToken(true)));
        Assert.Equal(RgbColor.Black, first.LastStaticColor);
    }

    [Fact]
    public void ParsesOnlyPlaybackMonitorsFromRecordedPactlOutput()
    {
        const string output = """
            51	alsa_output.pci-0000_00_1f.3.analog-stereo.monitor	PipeWire	float32le 2ch 48000Hz	SUSPENDED
            52	alsa_input.pci-0000_00_1f.3.analog-stereo	PipeWire	float32le 2ch 48000Hz	RUNNING
            65	bluez_output.11_22_33_44_55_66.1.monitor	PipeWire	float32le 2ch 48000Hz	SUSPENDED
            """;
        string[] expected =
        [
            "@DEFAULT_MONITOR@", "alsa_output.pci-0000_00_1f.3.analog-stereo.monitor",
            "bluez_output.11_22_33_44_55_66.1.monitor",
        ];
        Assert.Equal(expected, PulseAudioSources.ParseMonitors(output));
    }

    [Fact]
    public void AudioConfigurationRoundTripsAndRejectsNonFiniteGain()
    {
        var config = new AppConfig { Audio = new AudioConfig { Mode = "bass", Color = new RgbColor(1, 2, 3) } };
        var copy = ConfigurationLoader.Parse(ConfigurationLoader.Serialize(config), "test");
        Assert.Equal(config.Audio, copy.Audio);
        Assert.Empty(copy.Validate());
        Assert.NotEmpty((config.Audio with { Gain = double.NaN }).Validate());
    }

    [Fact]
    public void PartialAudioConfigurationPreservesDefaultsAndExplicitZeroValues()
    {
        var config = ConfigurationLoader.Parse("""{"audio":{"mode":"volume","brightness":0,"smoothing":0}}""", "test");
        Assert.Equal(3, config.Audio.Gain);
        Assert.Equal(0, config.Audio.Brightness);
        Assert.Equal(0, config.Audio.Smoothing);
        Assert.Equal(0.005, config.Audio.NoiseGate);
        Assert.Equal(new RgbColor(128, 64, 255), config.Audio.Color);
        Assert.Empty(config.Validate());
    }

    private static float[] Tone(double frequency, bool reverseRight = false)
    {
        var samples = new float[Size * 2];
        for (var i = 0; i < Size; i++)
        {
            samples[i * 2] = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * i / Rate));
            samples[(i * 2) + 1] = samples[i * 2] * (reverseRight ? -1 : 1);
        }

        return samples;
    }

    private sealed class FiniteAudioCapture(int blocks = 1) : IAudioCapture
    {
        private readonly float[] samples = Tone(93.75);
        private int remaining = blocks;
        public int SampleRate => Rate;
        public int Channels => 2;
        public int SamplesPerChannel => Size;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<ReadOnlyMemory<float>> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlyMemory<float> result = remaining-- > 0 ? samples : ReadOnlyMemory<float>.Empty;
            return Task.FromResult(result);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BrokenDevice : ILightDevice
    {
        public string Name => "Broken test device";
        public DeviceCapabilities Capabilities { get; } = new(4, true, true, false, false, true);
        public bool BlackoutAttempted { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendFrameAsync(ReadOnlyMemory<RgbColor> colors, CancellationToken cancellationToken) =>
            throw new DeviceException("Recorded transport failure");
        public Task SetStaticColorAsync(RgbColor color, CancellationToken cancellationToken)
        {
            BlackoutAttempted = color == RgbColor.Black;
            throw new DeviceException("Recorded shutdown failure");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
