using LightSync.Core.Devices;

namespace LightSync.Devices.OpenRgb.Tests;

public sealed class OpenRgbSelectorTests
{
    [Theory]
    [InlineData("GPU", null)]
    [InlineData(null, "gpu-serial")]
    [InlineData("GPU", "gpu-serial")]
    public void ReconnectsUniqueIdentityAfterLocationChanges(string? name, string? serial)
    {
        var controller = Controller(4, "GPU", "gpu-serial", "HID: /dev/hidraw8");
        var settings = new OpenRgbSettings { ControllerName = name, Serial = serial, Location = "HID: /dev/hidraw11" };
        Assert.Same(controller, settings.SelectController([Controller(0, "RAM", "ram", "DIMM:0"), controller]));
    }

    [Fact]
    public void UsesLocationToDistinguishIdenticalNamesWithoutSerials()
    {
        var first = Controller(1, "RAM", "", "I2C: /dev/i2c-6, address 0x5A");
        var second = Controller(0, "RAM", "", "I2C: /dev/i2c-6, address 0x5B");
        var settings = new OpenRgbSettings { ControllerName = "RAM", Location = second.Location };
        Assert.Same(second, settings.SelectController([first, second]));
    }

    [Fact]
    public void RejectsStaleLocationWhenIdentityIsAmbiguous()
    {
        var settings = new OpenRgbSettings { ControllerName = "RAM", Location = "I2C: /dev/i2c-6, address 0x5A" };
        var error = Assert.Throws<DeviceException>(() => settings.SelectController([
            Controller(0, "RAM", "", "I2C: /dev/i2c-7, address 0x5A"),
            Controller(1, "RAM", "", "I2C: /dev/i2c-7, address 0x5B")]));
        Assert.Contains("Several", error.Message, StringComparison.Ordinal);
        Assert.Contains("current location", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GPU", "other-serial")]
    [InlineData("Other GPU", "gpu-serial")]
    [InlineData("gpu", "gpu-serial")]
    [InlineData("GPU ", "gpu-serial")]
    public void NeverIgnoresNameOrSerialMismatch(string name, string serial)
    {
        var settings = new OpenRgbSettings { ControllerName = name, Serial = serial, Location = "PCI:1" };
        var error = Assert.Throws<DeviceException>(() => settings.SelectController([Controller(0, "GPU", "gpu-serial", "PCI:1")]));
        Assert.Contains("No OpenRGB controller", error.Message, StringComparison.Ordinal);
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:6742", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocationOnlySelectorMustMatchEvenWithOneController()
    {
        var settings = new OpenRgbSettings { Location = "PCI:1" };
        var controller = Controller(0, "GPU", "gpu-serial", "PCI:2");
        Assert.Throws<DeviceException>(() => settings.SelectController([controller]));
        Assert.Same(controller, (settings with { Location = "PCI:2" }).SelectController([controller]));
    }

    [Fact]
    public void ReportsEmptyServerSeparatelyFromMissingProfile()
    {
        var error = Assert.Throws<DeviceException>(() => new OpenRgbSettings().SelectController([]));
        Assert.Contains("reports no components", error.Message, StringComparison.Ordinal);
        Assert.Contains("hardware detection", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesMultilineLocationThroughSettingsRoundTrip()
    {
        const string location = "HID: /dev/hidraw7 (Receiver) \r\nWireless Index: 255";
        var controller = Controller(1, "Mouse", "", location);
        var settings = OpenRgbSettings.FromDictionary(new OpenRgbSettings { ControllerName = "Mouse", Location = location }.ToDictionary());
        Assert.Equal(location, settings.Location);
        Assert.Same(controller, settings.SelectController([Controller(0, "Mouse", "", "HID: /dev/hidraw9"), controller]));
    }

    private static OpenRgbController Controller(int index, string name, string serial, string location) =>
        new(index, name, "Test", serial, location, 2, true, []);
}
