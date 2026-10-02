using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb.Tests;

public sealed class OpenRgbAvailabilityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChecksSdkProtocolWithoutEnumerationOrControl()
    {
        await using var server = new SdkServerFixture();
        Assert.True(await OpenRgbDiscovery.IsServerAvailableAsync(server.Settings, Token));
        Assert.Equal(1, server.ProtocolRequests);
        Assert.Equal(0, server.ControllerRequests);
        Assert.Equal(0, server.ControlWriteCount);
    }

    [Fact]
    public async Task AListeningSocketWithNoSdkReplyIsUnavailable()
    {
        await using var server = new SdkServerFixture { StallCommand = 40 };
        Assert.False(await OpenRgbDiscovery.IsServerAvailableAsync(
            server.Settings with { TimeoutMilliseconds = 100 }, Token));
        Assert.Equal(1, server.ProtocolRequests);
        Assert.Equal(0, server.ControlWriteCount);
    }

    [Fact]
    public async Task CancellationIsNotReportedAsServerUnavailable()
    {
        await using var server = new SdkServerFixture { StallCommand = 40 };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.CancelAfter(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpenRgbDiscovery.IsServerAvailableAsync(server.Settings, cancellation.Token));
    }

    [Fact]
    public async Task RejectsNonSdkResponseInsteadOfTreatingAnOpenPortAsReady()
    {
        await using var server = new SdkServerFixture { BadMagic = true };
        await Assert.ThrowsAsync<DeviceException>(() => OpenRgbDiscovery.IsServerAvailableAsync(server.Settings, Token));
    }
}
