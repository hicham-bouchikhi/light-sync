using LightSync.Core.Devices;

namespace LightSync.Core.Tests;

public class DeviceCapabilityValidatorTests
{
    private static DeviceCapabilities Streaming(int maximumZones = 24) => new(
        MaximumZones: maximumZones,
        SupportsStreaming: true,
        SupportsStaticColor: true,
        SupportsBrightness: true,
        SupportsEffects: false,
        SupportsPerZoneColor: true);

    [Fact]
    public void AcceptsZoneCountWithinDeviceMaximum()
    {
        var result = DeviceCapabilityValidator.ValidateForStreaming(Streaming(24), zoneCount: 24);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void RejectsZoneCountAboveDeviceMaximum()
    {
        var result = DeviceCapabilityValidator.ValidateForStreaming(Streaming(12), zoneCount: 24);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("at most 12", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsDeviceThatCannotStream()
    {
        var capabilities = Streaming() with { SupportsStreaming = false };

        var result = DeviceCapabilityValidator.ValidateForStreaming(capabilities, zoneCount: 24);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("does not support streaming", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsMultipleZonesOnDeviceWithoutPerZoneColor()
    {
        var capabilities = Streaming() with { SupportsPerZoneColor = false };

        var result = DeviceCapabilityValidator.ValidateForStreaming(capabilities, zoneCount: 24);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("zoneCount to 1", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsSingleZoneOnDeviceWithoutPerZoneColor()
    {
        var capabilities = Streaming(maximumZones: 1) with { SupportsPerZoneColor = false };

        var result = DeviceCapabilityValidator.ValidateForStreaming(capabilities, zoneCount: 1);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RejectsDeviceReportingNoZones()
    {
        var result = DeviceCapabilityValidator.ValidateForStreaming(Streaming(0), zoneCount: 1);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("no addressable zones", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsStaticColorOnDeviceWithoutSupport()
    {
        var capabilities = Streaming() with { SupportsStaticColor = false };

        var result = DeviceCapabilityValidator.ValidateForStaticColor(capabilities);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ThrowIfInvalidListsEveryProblem()
    {
        var capabilities = Streaming(0) with { SupportsStreaming = false, SupportsPerZoneColor = false };
        var result = DeviceCapabilityValidator.ValidateForStreaming(capabilities, zoneCount: 24);

        var exception = Assert.Throws<DeviceException>(() => DeviceCapabilityValidator.ThrowIfInvalid(result));

        foreach (var problem in result.Problems)
        {
            Assert.Contains(problem, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ThrowIfInvalidDoesNothingWhenValid()
    {
        var result = DeviceCapabilityValidator.ValidateForStreaming(Streaming(), zoneCount: 24);

        DeviceCapabilityValidator.ThrowIfInvalid(result);
    }

    [Fact]
    public void RejectsNonPositiveZoneCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DeviceCapabilityValidator.ValidateForStreaming(Streaming(), zoneCount: 0));
    }
}
