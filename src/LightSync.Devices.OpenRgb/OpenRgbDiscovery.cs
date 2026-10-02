namespace LightSync.Devices.OpenRgb;

public static class OpenRgbDiscovery
{
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
