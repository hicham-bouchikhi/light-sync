using LightSync.Application;
using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb.Tests;

public sealed class OpenRgbServerLauncherTests
{
    private static readonly string[] ExpectedArguments =
        ["--gui", "--server", "--server-host", "127.0.0.1", "--server-port", "16742", "--noautoconnect"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("localhost")]
    [InlineData("192.0.2.1")]
    public async Task ReusesReachableServerWithoutInspectingOrStartingProcesses(string address)
    {
        var host = new FakeHost(true);
        await EnsureAsync(host, new OpenRgbSettings { Host = address });
        Assert.Equal(0, host.RunningChecks);
        Assert.Equal(0, host.Starts);
    }

    [Fact]
    public async Task OpensMissingLocalApplicationAndWaitsForItsServer()
    {
        var host = new FakeHost(false, false, true);
        List<string> status = [];
        await EnsureAsync(host, reportStatus: status.Add);
        Assert.Equal(1, host.Starts);
        Assert.Equal(3, host.Probes);
        Assert.Equal(1, host.DetectionWaits);
        Assert.Contains("Opening OpenRGB", Assert.Single(status), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitsForAlreadyRunningApplicationWithoutOpeningAnotherCopy()
    {
        var host = new FakeHost(false, false, true) { IsRunning = true };
        List<string> status = [];
        await EnsureAsync(host, reportStatus: status.Add);
        Assert.Equal(0, host.Starts);
        Assert.Contains("already running", Assert.Single(status), StringComparison.Ordinal);
        Assert.Equal(0, host.DetectionWaits);
    }

    [Theory]
    [InlineData("192.0.2.1")]
    [InlineData("pc.example")]
    public async Task DoesNotStartLocalApplicationForAnUnavailableRemoteServer(string address)
    {
        var host = new FakeHost(false);
        var error = await Assert.ThrowsAsync<DeviceUnreachableException>(() =>
            EnsureAsync(host, new OpenRgbSettings { Host = address }));
        Assert.Contains(address + ":6742", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, host.Starts);
        Assert.Equal(0, host.RunningChecks);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task StartsOnlyForLoopbackEndpoints(string address)
    {
        var host = new FakeHost(false, true);
        await EnsureAsync(host, new OpenRgbSettings { Host = address });
        Assert.Equal(1, host.Starts);
    }

    [Fact]
    public async Task ReportsApplicationExitBeforeServerReadiness()
    {
        var host = new FakeHost(false) { StartedProcessExitCode = 7 };
        var error = await Assert.ThrowsAsync<DeviceUnreachableException>(() => EnsureAsync(host));
        Assert.Contains("exited with code 7", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, host.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundsWaitingForAStartedOrExistingServer(bool running)
    {
        var host = new FakeHost(false) { IsRunning = running };
        var error = await Assert.ThrowsAsync<DeviceUnreachableException>(() =>
            OpenRgbServerLauncher.EnsureAvailableAsync(new OpenRgbSettings(), host,
                TimeSpan.FromMilliseconds(50), null, Token));
        Assert.Contains("SDK server at 127.0.0.1:6742 is not ready", error.Message, StringComparison.Ordinal);
        Assert.Equal(running ? 0 : 1, host.Starts);
    }

    [Fact]
    public async Task CancellationStopsReadinessPollingAndAllowsLaterCalls()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var host = new FakeHost(false) { OnStart = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpenRgbServerLauncher.EnsureAvailableAsync(new OpenRgbSettings(), host,
                TimeSpan.FromSeconds(2), null, cancellation.Token));
        await EnsureAsync(new FakeHost(true));
        Assert.Equal(1, host.Starts);
    }

    [Fact]
    public async Task ConcurrentRequestsOpenOnlyOneApplication()
    {
        var firstHost = new FakeHost(false, false, true);
        var secondHost = new FakeHost(true);
        var first = EnsureAsync(firstHost);
        var second = EnsureAsync(secondHost);
        await Task.WhenAll(first, second);
        Assert.Equal(1, firstHost.Starts);
        Assert.Equal(0, secondHost.Starts);
    }

    [Fact]
    public async Task LaunchFailureIsReportedAndDoesNotBlockFutureRequests()
    {
        var host = new FakeHost(false) { OnStart = () => throw new DeviceUnreachableException("Cannot launch") };
        await Assert.ThrowsAsync<DeviceUnreachableException>(() => EnsureAsync(host));
        await EnsureAsync(new FakeHost(true));
        Assert.Equal(1, host.Probes);
    }

    [Fact]
    public async Task BoundsWaitingForHardwareAfterTheSocketIsReady()
    {
        var host = new FakeHost(false, true)
        {
            OnDetection = cancellationToken => Task.Delay(Timeout.Infinite, cancellationToken),
        };
        await Assert.ThrowsAsync<DeviceUnreachableException>(() =>
            OpenRgbServerLauncher.EnsureAvailableAsync(new OpenRgbSettings(), host,
                TimeSpan.FromMilliseconds(50), null, Token));
        Assert.Equal(1, host.Starts);
        Assert.Equal(1, host.DetectionWaits);
    }

    [Fact]
    public void OpensGuiWithSdkServerOnConfiguredPortWithoutShellCommands()
    {
        var start = OpenRgbServerLauncher.CreateStartInfo(new OpenRgbSettings { Host = "localhost", Port = 16742 });
        Assert.False(start.UseShellExecute);
        Assert.Equal(ExpectedArguments, start.ArgumentList);
    }

    [Fact]
    public async Task CanProbeAnActualSdkServerWithoutSendingControlCommands()
    {
        await using var server = new SdkServerFixture();
        await OpenRgbServerLauncher.EnsureAvailableAsync(server.Settings, null, Token);
        Assert.Equal(0, server.ControlWriteCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task WaitsForDetectionCompletionEvenWhenSdkPortAndPartialListAreReady(int count)
    {
        await using var server = new SdkServerFixture
        {
            Version = 6,
            Controllers = count == 0 ? [] : [SdkServerFixture.ControllerData("GPU", "", "")],
            StartupDetectionDelay = TimeSpan.FromMilliseconds(100),
        };
        await OpenRgbDiscovery.WaitForDetectionAsync(server.Settings, Token);
        Assert.True(server.DetectionComplete);
        Assert.Equal(0, server.ControlWriteCount);
    }

    [Fact]
    public async Task WaitsForOlderServerListToSettleWithoutControlCommands()
    {
        await using var server = new SdkServerFixture();
        await OpenRgbDiscovery.WaitForDetectionAsync(server.Settings, Token);
        Assert.Equal(0, server.ControlWriteCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RejectsMalformedDetectionNotification(bool malformedStart, bool malformedProgress)
    {
        await using var server = new SdkServerFixture
        {
            Version = 6,
            StartupDetectionDelay = TimeSpan.FromMilliseconds(10),
            MalformedDetectionNotification = malformedStart,
            MalformedDetectionProgress = malformedProgress,
        };
        var error = await Assert.ThrowsAsync<DeviceException>(() =>
            OpenRgbDiscovery.WaitForDetectionAsync(server.Settings, Token));
        Assert.Contains("invalid detection notification", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelsWhenOlderServerHasNoControllersOrCompletionNotification()
    {
        await using var server = new SdkServerFixture { Controllers = [] };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpenRgbDiscovery.WaitForDetectionAsync(server.Settings, cancellation.Token));
    }

    private static Task EnsureAsync(FakeHost host, OpenRgbSettings? settings = null, Action<string>? reportStatus = null) =>
        OpenRgbServerLauncher.EnsureAvailableAsync(settings ?? new OpenRgbSettings(), host,
            TimeSpan.FromSeconds(2), reportStatus, Token);

    private sealed class FakeHost(params bool[] results) : IOpenRgbServerHost
    {
        private readonly Queue<bool> responses = new(results);
        private bool running;

        public bool IsRunning
        {
            get
            {
                RunningChecks++;
                return running;
            }
            init => running = value;
        }

        public int? StartedProcessExitCode { get; init; }

        internal Action? OnStart { get; init; }

        internal Func<CancellationToken, Task>? OnDetection { get; init; }

        internal int Probes { get; private set; }

        internal int RunningChecks { get; private set; }

        internal int Starts { get; private set; }

        internal int DetectionWaits { get; private set; }

        public Task<bool> CanConnectAsync(OpenRgbSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Probes++;
            return Task.FromResult(responses.TryDequeue(out var available) && available);
        }

        public void Start(OpenRgbSettings settings)
        {
            Starts++;
            OnStart?.Invoke();
        }

        public Task WaitForDetectionAsync(OpenRgbSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DetectionWaits++;
            return OnDetection?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
    }
}
