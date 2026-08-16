using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LightSync.Core.Devices;

namespace LightSync.Devices.Nanoleaf;

/// <summary>
/// Client for the Nanoleaf local OpenAPI. The auth token is held only in memory and is never
/// written to logs or exception messages.
/// </summary>
public sealed class NanoleafApi : IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsHttpClient;
    private readonly string token;
    private readonly Uri baseUri;

    public NanoleafApi(NanoleafSettings settings, string token, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            throw new ArgumentException("Host must be resolved before creating the API client.", nameof(settings));
        }

        this.token = token;
        baseUri = settings.BaseUri;
        Host = settings.Host;
        ownsHttpClient = httpClient is null;
        http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public string Host { get; }

    /// <summary>
    /// Requests a new auth token. The device only grants one while it is in pairing mode,
    /// which the user enters by holding the power button until the LED flashes.
    /// </summary>
    public static async Task<string> PairAsync(
        string host,
        int port,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://{host}:{port}/api/v1/new"));

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(uri, content: null, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DeviceUnreachableException($"Could not reach the Nanoleaf device at {host}:{port}.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeviceUnreachableException($"Timed out reaching {host}:{port}.", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                throw new DeviceAuthenticationException(
                    "The device refused to issue a token. Hold its power button for about five " +
                    "seconds until the LED flashes, then try again within 30 seconds.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DeviceException(
                    $"Pairing failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var payload = await response.Content.ReadFromJsonAsync(
                NanoleafJsonContext.Default.NanoleafAuthResponse, cancellationToken);

            if (string.IsNullOrWhiteSpace(payload?.AuthToken))
            {
                throw new DeviceException("The device returned an empty auth token.");
            }

            return payload.AuthToken;
        }
    }

    public async Task<NanoleafDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken)
    {
        var info = await GetAsync(string.Empty, NanoleafJsonContext.Default.NanoleafDeviceInfo, cancellationToken);
        return info ?? throw new DeviceException("The device returned no information.");
    }

    public async Task<NanoleafPanelLayout?> GetPanelLayoutAsync(CancellationToken cancellationToken) =>
        await GetAsync("panelLayout/layout", NanoleafJsonContext.Default.NanoleafPanelLayout, cancellationToken);

    public Task SetPowerAsync(bool on, CancellationToken cancellationToken) =>
        PutAsync(
            "state",
            new NanoleafStateRequest { On = new NanoleafBoolValue { Value = on } },
            NanoleafJsonContext.Default.NanoleafStateRequest,
            cancellationToken);

    /// <summary>
    /// Sets a colour. The API exposes no RGB route, so the colour is converted to the
    /// hue/saturation/brightness triple the device understands.
    /// </summary>
    public Task SetHsvAsync(int hue, int saturation, int brightness, CancellationToken cancellationToken)
    {
        var request = new NanoleafStateRequest
        {
            On = new NanoleafBoolValue { Value = brightness > 0 },
            Hue = new NanoleafWriteValue(Math.Clamp(hue, 0, 359)),
            Saturation = new NanoleafWriteValue(Math.Clamp(saturation, 0, 100)),

            // Matter Essentials devices reject a brightness of 0 with HTTP 400, so the floor
            // for a lit device is 1 and "off" is expressed through the on flag instead.
            Brightness = new NanoleafWriteValue(Math.Clamp(brightness, 1, 100)),
        };

        return PutAsync("state", request, NanoleafJsonContext.Default.NanoleafStateRequest, cancellationToken);
    }

    /// <summary>
    /// Attempts to switch the device into external-control streaming mode. Returns the stream
    /// endpoint on success, or null when the device does not support external control — which
    /// is expected on Matter Essentials models.
    /// </summary>
    public async Task<NanoleafStreamControlResponse?> TryEnableExternalControlAsync(
        string? extControlVersion,
        CancellationToken cancellationToken)
    {
        var request = new NanoleafEffectsWriteRequest(new NanoleafEffectsWrite
        {
            ExtControlVersion = extControlVersion,
        });

        var json = JsonSerializer.Serialize(request, NanoleafJsonContext.Default.NanoleafEffectsWriteRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await SendAsync(HttpMethod.Put, "effects", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // v1 answers with an empty body; only v2 reports where to send frames.
        if (string.IsNullOrWhiteSpace(body))
        {
            return new NanoleafStreamControlResponse { Port = 60222, Protocol = "udp", IpAddress = Host };
        }

        try
        {
            return JsonSerializer.Deserialize(body, NanoleafJsonContext.Default.NanoleafStreamControlResponse);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<T?> GetAsync<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        EnsureSuccess(response, path);
        return await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);
    }

    private async Task PutAsync<T>(
        string path,
        T body,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, typeInfo);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Put, path, content, cancellationToken);
        EnsureSuccess(response, path);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        // The token appears only in the request URI, which is never logged or surfaced.
        using var request = new HttpRequestMessage(method, new Uri(baseUri, $"{token}/{path}"))
        {
            Content = content,
        };

        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DeviceUnreachableException($"Could not reach the Nanoleaf device at {Host}.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeviceUnreachableException($"Timed out talking to the Nanoleaf device at {Host}.", ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Deliberately describes the path only, so the token in the request URI is not echoed.
        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new DeviceAuthenticationException(
                "The Nanoleaf device rejected the auth token. Run 'light-sync pair' to obtain a new one."),
            _ => new DeviceException(
                $"Nanoleaf request '{path}' failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}."),
        };
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            http.Dispose();
        }
    }
}
