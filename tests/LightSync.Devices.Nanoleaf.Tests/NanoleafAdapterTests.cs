using LightSync.Core.Colors;
using LightSync.Core.Devices;
using LightSync.Devices.Nanoleaf;

namespace LightSync.Devices.Nanoleaf.Tests;

public sealed class NanoleafAdapterTests : IDisposable
{
    private readonly string variableName = $"LIGHT_SYNC_TEST_TOKEN_{Guid.NewGuid():N}";

    /// <summary>
    /// Points the token file at a path that does not exist, so these tests never pick up a real
    /// token from the machine they happen to run on.
    /// </summary>
    private readonly string secretsPath =
        Path.Combine(Path.GetTempPath(), $"light-sync-absent-{Guid.NewGuid():N}.json");

    private NanoleafSettings Settings(string host = "203.0.113.1") => new()
    {
        Host = host,
        TokenEnvironmentVariable = variableName,
        SecretsFilePath = secretsPath,
    };

    [Fact]
    public async Task ReportsMissingTokenBeforeTouchingTheNetwork()
    {
        // No token in the environment and no secrets file: this must fail as an authentication
        // problem, not as a timeout against an unreachable address.
        await using var adapter = new NanoleafAdapter(Settings());

        await Assert.ThrowsAsync<DeviceAuthenticationException>(
            () => adapter.ConnectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReportsAnUnreachableDevice()
    {
        Environment.SetEnvironmentVariable(variableName, "a-token");

        // 203.0.113.0/24 is reserved for documentation, so nothing can answer.
        await using var adapter = new NanoleafAdapter(Settings());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Assert.ThrowsAsync<DeviceUnreachableException>(() => adapter.ConnectAsync(cts.Token));
    }

    [Fact]
    public async Task StartsWithNoAdvertisedZonesUntilConnected()
    {
        await using var adapter = new NanoleafAdapter(Settings());

        // Nothing is known before connecting, so the capability check cannot pass by accident.
        Assert.Equal(0, adapter.Capabilities.MaximumZones);
        Assert.False(adapter.IsStreaming);

        var validation = DeviceCapabilityValidator.ValidateForStreaming(adapter.Capabilities, 24);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task RefusesToStreamBeforeConnecting()
    {
        Environment.SetEnvironmentVariable(variableName, "a-token");
        await using var adapter = new NanoleafAdapter(Settings());

        await Assert.ThrowsAsync<DeviceException>(
            () => adapter.SendFrameAsync(new RgbColor[24], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefusesAStaticColourBeforeConnecting()
    {
        await using var adapter = new NanoleafAdapter(Settings());

        await Assert.ThrowsAsync<DeviceException>(
            () => adapter.SetStaticColorAsync(ColorConstants.Red, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void KnowsTheMeasuredLedCountForModelsWithoutAPanelLayout()
    {
        // The Matter Essentials line answers panelLayout with HTTP 500; an NL72K4 was measured
        // to have exactly 24 addressable LEDs, which also matches the default zone count.
        Assert.Equal(24, NanoleafAdapter.DefaultEssentialsLedCount);
    }

    [Fact]
    public async Task DisconnectingWithoutConnectingIsHarmless()
    {
        await using var adapter = new NanoleafAdapter(Settings());

        await adapter.DisconnectAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportsWhenDiscoveryFindsNothingAndNoHostIsConfigured()
    {
        Environment.SetEnvironmentVariable(variableName, "a-token");

        // An expected model that cannot match anything forces the no-candidates path.
        await using var adapter = new NanoleafAdapter(new NanoleafSettings
        {
            Host = null,
            TokenEnvironmentVariable = variableName,
            SecretsFilePath = secretsPath,
            ExpectedModel = "NL-DOES-NOT-EXIST",
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var exception = await Assert.ThrowsAnyAsync<DeviceException>(
            () => adapter.ConnectAsync(cts.Token));
        Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(variableName, null);
        GC.SuppressFinalize(this);
    }
}
