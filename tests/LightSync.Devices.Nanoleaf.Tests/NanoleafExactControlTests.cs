using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LightSync.Core.Colors;
using LightSync.Core.Devices;
using LightSync.Devices.Nanoleaf;

namespace LightSync.Devices.Nanoleaf.Tests;

public sealed class NanoleafExactControlTests
{
    [Fact]
    public async Task SendsExactRgbBytesInTheConfiguredPhysicalAddressOrder()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        using var handler = new RecordedDeviceHandler(port);
        using var http = new HttpClient(handler);
        var secretPath = Path.Combine(Path.GetTempPath(), $"light-sync-test-{Guid.NewGuid():N}.local.json");
        await NanoleafAuthentication.SaveTokenAsync("test-token", secretPath, cancellation.Token);
        try
        {
            var settings = new NanoleafSettings
            {
                Host = "127.0.0.1", SecretsFilePath = secretPath,
                TokenEnvironmentVariable = "LIGHT_SYNC_TEST_UNUSED_" + Guid.NewGuid().ToString("N"),
                LedMapping = [7, 2],
            };
            await using var adapter = new NanoleafAdapter(settings, (s, token) => new NanoleafApi(s, token, http));
            await adapter.ConnectAsync(cancellation.Token);
            int[] expectedAddresses = [7, 2];
            Assert.Equal(expectedAddresses, adapter.ZoneAddresses);
            await adapter.SendFrameAsync(new[] { new RgbColor(1, 127, 254), new RgbColor(45, 67, 89) }, cancellation.Token);
            var datagram = await receiver.ReceiveAsync(cancellation.Token);
            Assert.Equal<byte[]>(
            [
                0, 2,
                0, 7, 1, 127, 254, 0, 0, 1,
                0, 2, 45, 67, 89, 0, 0, 1,
            ], datagram.Buffer);
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    [Fact]
    public async Task RejectsBothShortAndLongFramesBeforeEnablingExternalControl()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var handler = new RecordedDeviceHandler(60222);
        using var http = new HttpClient(handler);
        var secretPath = Path.Combine(Path.GetTempPath(), $"light-sync-test-{Guid.NewGuid():N}.local.json");
        await NanoleafAuthentication.SaveTokenAsync("test-token", secretPath, cancellation.Token);
        try
        {
            var settings = new NanoleafSettings
            {
                Host = "127.0.0.1", SecretsFilePath = secretPath,
                TokenEnvironmentVariable = "LIGHT_SYNC_TEST_UNUSED_" + Guid.NewGuid().ToString("N"),
                LedMapping = [7, 2],
            };
            await using var adapter = new NanoleafAdapter(settings, (s, token) => new NanoleafApi(s, token, http));
            await adapter.ConnectAsync(cancellation.Token);
            await Assert.ThrowsAsync<DeviceException>(() => adapter.SendFrameAsync(new RgbColor[1], cancellation.Token));
            await Assert.ThrowsAsync<DeviceException>(() => adapter.SendFrameAsync(new RgbColor[3], cancellation.Token));
            Assert.False(adapter.IsStreaming);
            Assert.Equal(0, handler.ExternalControlRequests);
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    [Theory]
    [InlineData("1,1")]
    [InlineData("-1,2")]
    [InlineData("65536,2")]
    public void RejectsDuplicateAndUnrepresentablePhysicalAddresses(string mapping)
    {
        var settings = NanoleafSettings.FromDictionary(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ledMapping"] = mapping,
        });
        Assert.NotEmpty(settings.Validate());
    }

    private sealed class RecordedDeviceHandler(int port) : HttpMessageHandler
    {
        public int ExternalControlRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = """{"name":"Recorded device","model":"NL72K4"}""";
            if (request.Method == HttpMethod.Put)
            {
                ExternalControlRequests++;
                body = string.Create(CultureInfo.InvariantCulture,
                    $"{{\"streamControlIpAddr\":\"127.0.0.1\",\"streamControlPort\":{port},\"streamControlProtocol\":\"udp\"}}");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
