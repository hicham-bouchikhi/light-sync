using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb;

public static class OpenRgbDiscovery
{
    /// <summary>Checks SDK readiness with a protocol handshake, without enumerating or changing lights.</summary>
    public static async Task<bool> IsServerAvailableAsync(OpenRgbSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var client = new OpenRgbClient(settings with
        {
            TimeoutMilliseconds = Math.Min(settings.TimeoutMilliseconds, 500),
        });
        try
        {
            await client.ConnectAsync(cancellationToken);
            return true;
        }
        catch (DeviceUnreachableException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    /// <summary>Waits for the initial device scan on a newly launched server without changing its lights.</summary>
    public static async Task WaitForDetectionAsync(OpenRgbSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var client = new OpenRgbClient(settings, forStartup: true);
        await client.ConnectAsync(cancellationToken);
        await client.WaitForDetectionAsync(cancellationToken);
    }

    /// <summary>Enumerates controllers without selecting modes or sending colours.</summary>
    public static async Task<IReadOnlyList<OpenRgbController>> DiscoverAsync(
        OpenRgbSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var client = new OpenRgbClient(settings);
        await client.ConnectAsync(cancellationToken);
        return await client.GetControllersAsync(cancellationToken);
    }
}
