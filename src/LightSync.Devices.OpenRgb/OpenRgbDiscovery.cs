namespace LightSync.Devices.OpenRgb;

public static class OpenRgbDiscovery
{
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
