namespace LightSync.Core.Devices;

/// <summary>
/// Checks a device can actually do what the configuration asks of it, before the pipeline
/// starts pushing frames at it.
/// </summary>
public static class DeviceCapabilityValidator
{
    public static ValidationResult ValidateForStreaming(DeviceCapabilities capabilities, int zoneCount)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoneCount);

        List<string> problems = [];

        if (!capabilities.SupportsStreaming)
        {
            problems.Add("The device does not support streaming, so it cannot follow the screen.");
        }

        if (!capabilities.SupportsPerZoneColor && zoneCount > 1)
        {
            problems.Add(
                $"The device cannot address zones individually, so it cannot render {zoneCount} " +
                "zones. Set mapping.zoneCount to 1.");
        }

        if (capabilities.MaximumZones > 0 && zoneCount > capabilities.MaximumZones)
        {
            problems.Add(
                $"Configured zoneCount is {zoneCount} but the device supports at most " +
                $"{capabilities.MaximumZones}.");
        }

        if (capabilities.MaximumZones == 0)
        {
            problems.Add("The device reports no addressable zones.");
        }

        return new ValidationResult(problems);
    }

    public static ValidationResult ValidateForStaticColor(DeviceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        List<string> problems = [];

        if (!capabilities.SupportsStaticColor)
        {
            problems.Add("The device does not support setting a static colour.");
        }

        return new ValidationResult(problems);
    }

    public static void ThrowIfInvalid(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsValid)
        {
            throw new DeviceException(
                "Device capability check failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, result.Problems.Select(p => "  - " + p)));
        }
    }
}

public sealed class ValidationResult(IReadOnlyList<string> problems)
{
    public IReadOnlyList<string> Problems { get; } = problems;

    public bool IsValid => Problems.Count == 0;
}
