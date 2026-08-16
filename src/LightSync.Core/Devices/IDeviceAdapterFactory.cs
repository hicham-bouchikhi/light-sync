namespace LightSync.Core.Devices;

/// <summary>
/// Resolves an adapter from the <c>device.adapter</c> string in configuration. This is the
/// single point where vendor-specific code enters the pipeline.
/// </summary>
public interface IDeviceAdapterFactory
{
    /// <summary>Adapter identifiers this factory can create, for <c>list-adapters</c>.</summary>
    IReadOnlyList<DeviceAdapterDescriptor> AvailableAdapters { get; }

    ILightDevice Create(string adapterId, IReadOnlyDictionary<string, string> settings);
}

public sealed record DeviceAdapterDescriptor(
    string Id,
    string DisplayName,
    string Description,
    bool IsImplemented);
