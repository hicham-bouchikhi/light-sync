using System.Buffers.Binary;
using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb.Tests;

public sealed class OpenRgbProtocolTests
{
    [Fact]
    public async Task ParsesRecordedControllerData()
    {
        var data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "controller-v4.bin"),
            TestContext.Current.CancellationToken);
        var controller = OpenRgbProtocol.ParseController(data, 0, 4);
        Assert.Contains("Full 104", controller.Name, StringComparison.Ordinal);
        Assert.True(controller.LedCount > 100);
        Assert.Equal(controller.LedCount, controller.Zones.Sum(zone => zone.LedCount));
    }

    [Fact]
    public void RejectsTruncatedControllerFieldsEvenWhenDeclaredSizeMatches()
    {
        var full = SdkServerFixture.ControllerData("GPU", "serial", "PCI:1");
        for (var length = 4; length < full.Length; length++)
        {
            var truncated = full.AsSpan(0, length).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(truncated, (uint)length);
            Assert.Throws<DeviceException>(() => OpenRgbProtocol.ParseController(truncated, 0, 4));
        }
    }

    [Theory]
    [InlineData("port", "oops")]
    [InlineData("port", "0")]
    [InlineData("port", "65536")]
    [InlineData("timeoutMilliseconds", "-1")]
    public void RejectsInvalidSettings(string key, string value) =>
        Assert.Throws<DeviceException>(() => OpenRgbSettings.FromDictionary(new Dictionary<string, string> { [key] = value }));
}
