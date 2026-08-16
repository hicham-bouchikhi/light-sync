using LightSync.Core.Devices;
using LightSync.Devices.Nanoleaf;

namespace LightSync.Devices.Nanoleaf.Tests;

public sealed class NanoleafAuthenticationTests : IDisposable
{
    private readonly string secretsPath =
        Path.Combine(Path.GetTempPath(), $"light-sync-secrets-{Guid.NewGuid():N}.json");

    private readonly string variableName = $"LIGHT_SYNC_TEST_TOKEN_{Guid.NewGuid():N}";

    private NanoleafSettings Settings => new()
    {
        Host = "192.168.1.24",
        TokenEnvironmentVariable = variableName,
    };

    [Fact]
    public void ReadsTheTokenFromTheEnvironment()
    {
        Environment.SetEnvironmentVariable(variableName, "token-from-environment");

        Assert.Equal("token-from-environment", NanoleafAuthentication.ResolveToken(Settings, secretsPath));
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Environment.SetEnvironmentVariable(variableName, "  padded-token\n");

        Assert.Equal("padded-token", NanoleafAuthentication.ResolveToken(Settings, secretsPath));
    }

    [Fact]
    public async Task FallsBackToTheSecretsFile()
    {
        await NanoleafAuthentication.SaveTokenAsync(
            "token-from-file", secretsPath, TestContext.Current.CancellationToken);

        Assert.Equal("token-from-file", NanoleafAuthentication.ResolveToken(Settings, secretsPath));
    }

    [Fact]
    public async Task PrefersTheEnvironmentOverTheFile()
    {
        await NanoleafAuthentication.SaveTokenAsync(
            "token-from-file", secretsPath, TestContext.Current.CancellationToken);
        Environment.SetEnvironmentVariable(variableName, "token-from-environment");

        Assert.Equal("token-from-environment", NanoleafAuthentication.ResolveToken(Settings, secretsPath));
    }

    [Fact]
    public void ThrowsAHelpfulErrorWhenNoTokenIsAvailable()
    {
        var exception = Assert.Throws<DeviceAuthenticationException>(
            () => NanoleafAuthentication.ResolveToken(Settings, secretsPath));

        Assert.Contains(variableName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("light-sync pair", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TreatsAnEmptyEnvironmentVariableAsAbsent()
    {
        Environment.SetEnvironmentVariable(variableName, "   ");

        Assert.Throws<DeviceAuthenticationException>(
            () => NanoleafAuthentication.ResolveToken(Settings, secretsPath));
    }

    [Fact]
    public async Task TreatsAMalformedSecretsFileAsAbsentWithoutEchoingIt()
    {
        await File.WriteAllTextAsync(
            secretsPath, "{ this is not json", TestContext.Current.CancellationToken);

        var exception = Assert.Throws<DeviceAuthenticationException>(
            () => NanoleafAuthentication.ResolveToken(Settings, secretsPath));

        Assert.DoesNotContain("this is not json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolveReportsFailureRatherThanThrowing()
    {
        Assert.False(NanoleafAuthentication.TryResolveToken(Settings, out var token, secretsPath));
        Assert.Null(token);
    }

    [Fact]
    public async Task TryResolveReturnsTheTokenWhenPresent()
    {
        await NanoleafAuthentication.SaveTokenAsync(
            "a-token", secretsPath, TestContext.Current.CancellationToken);

        Assert.True(NanoleafAuthentication.TryResolveToken(Settings, out var token, secretsPath));
        Assert.Equal("a-token", token);
    }

    [Fact]
    public async Task SavesTheTokenReadableOnlyByItsOwner()
    {
        await NanoleafAuthentication.SaveTokenAsync(
            "a-token", secretsPath, TestContext.Current.CancellationToken);

        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes are not meaningful on Windows.");
            return;
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(secretsPath));
    }

    [Fact]
    public void SettingsToStringNeverRevealsTheToken()
    {
        Environment.SetEnvironmentVariable(variableName, "super-secret-token-value");

        var text = Settings.ToString();

        Assert.DoesNotContain("super-secret-token-value", text, StringComparison.Ordinal);
        // It names the variable, which is safe and useful for diagnostics.
        Assert.Contains(variableName, text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(variableName, null);

        if (File.Exists(secretsPath))
        {
            File.Delete(secretsPath);
        }

        GC.SuppressFinalize(this);
    }
}
