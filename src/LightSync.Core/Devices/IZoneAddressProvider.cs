namespace LightSync.Core.Devices;

/// <summary>Maps each frame index to the vendor's physical address.</summary>
public interface IZoneAddressProvider
{
    IReadOnlyList<int> ZoneAddresses { get; }

    string ZoneAddressSource { get; }
}
