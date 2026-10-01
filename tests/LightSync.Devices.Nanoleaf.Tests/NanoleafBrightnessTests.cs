using System.Net;
using System.Text;
using System.Text.Json;
using LightSync.Core.Colors;
using LightSync.Devices.Nanoleaf;

namespace LightSync.Devices.Nanoleaf.Tests;

public sealed class NanoleafBrightnessTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task SendsPercentageWithoutChangingColourAndUsesPowerOffForZero(int percent)
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        using var api = new NanoleafApi(new NanoleafSettings { Host = "127.0.0.1" }, "test-token", http);
        await api.SetBrightnessAsync(percent, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(Assert.Single(handler.States));
        var root = json.RootElement;
        Assert.Equal(percent > 0, root.GetProperty("on").GetProperty("value").GetBoolean());
        Assert.Equal(Math.Max(1, percent), root.GetProperty("brightness").GetProperty("value").GetInt32());
        Assert.False(root.TryGetProperty("hue", out _));
        Assert.False(root.TryGetProperty("sat", out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task RejectsOutOfRangeBrightnessBeforeSending(int percent)
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        using var api = new NanoleafApi(new NanoleafSettings { Host = "127.0.0.1" }, "test-token", http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            api.SetBrightnessAsync(percent, TestContext.Current.CancellationToken));
        Assert.Empty(handler.States);
    }

    [Fact]
    public async Task RestoresFullBrightnessWhenStreamingRestartsAfterBlackout()
    {
        var token = TestContext.Current.CancellationToken;
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var secretPath = Path.Combine(Path.GetTempPath(), $"light-sync-brightness-{Guid.NewGuid():N}.local.json");
        await NanoleafAuthentication.SaveTokenAsync("test-token", secretPath, token);
        try
        {
            var settings = new NanoleafSettings
            {
                Host = "127.0.0.1", SecretsFilePath = secretPath, LedMapping = [0],
                TokenEnvironmentVariable = "LIGHT_SYNC_TEST_UNUSED_" + Guid.NewGuid().ToString("N"),
            };
            await using var adapter = new NanoleafAdapter(settings, (s, auth) => new NanoleafApi(s, auth, http));
            await adapter.ConnectAsync(token);
            await adapter.SetStaticColorAsync(RgbColor.Black, token);
            Assert.Equal(0, adapter.BrightnessPercent);
            await adapter.SendFrameAsync(new[] { new RgbColor(255, 255, 255) }, token);
            using var state = JsonDocument.Parse(handler.States[^1]);
            Assert.True(state.RootElement.GetProperty("on").GetProperty("value").GetBoolean());
            Assert.Equal(100, state.RootElement.GetProperty("brightness").GetProperty("value").GetInt32());
            Assert.Equal(100, adapter.BrightnessPercent);
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> States { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = """{"name":"Recorded lamp","model":"NL72K4","state":{"on":{"value":true},"brightness":{"value":1}}}""";
            if (request.Method == HttpMethod.Put)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/state", StringComparison.Ordinal))
                {
                    States.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                    body = "{}";
                }
                else
                {
                    body = """{"streamControlIpAddr":"127.0.0.1","streamControlPort":60222,"streamControlProtocol":"udp"}""";
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
