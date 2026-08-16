using LightSync.Devices.Nanoleaf;

namespace LightSync.Devices.Nanoleaf.Tests;

public class NanoleafDiscoveryTests
{
    /// <summary>Verbatim avahi-browse -tpr output from the development network.</summary>
    private const string RealOutput = """
        +;wlan0;IPv4;Nanoleaf\032IML\0328B7;_nanoleafapi._tcp;local
        =;enp7s0;IPv4;Nanoleaf\032IML\0328B7;_nanoleafapi._tcp;local;808AF7226825.local;192.168.1.30;16021;"id=3D68" "eui64=0000808AF7226825" "md=NL72K4" "srcvers=4.0.0"
        =;enp7s0;IPv6;Nanoleaf\032IML\0328B7;_nanoleafapi._tcp;local;808AF7226825.local;192.168.1.30;16021;"id=3D68" "eui64=0000808AF7226825" "md=NL72K4" "srcvers=4.0.0"
        =;enp7s0;IPv4;Nanoleaf\032IML\032294-3340;_nanoleafapi._tcp;local;808AF726FC0F.local;192.168.1.24;16021;"id=3340" "eui64=0000808AF726FC0F" "md=NL72K4" "srcvers=3.0.31"
        =;wlan0;IPv4;Nanoleaf\032IML\032294-3340;_nanoleafapi._tcp;local;808AF726FC0F.local;192.168.1.24;16021;"id=3340" "eui64=0000808AF726FC0F" "md=NL72K4" "srcvers=3.0.31"
        """;

    [Fact]
    public void FindsEveryDistinctDevice()
    {
        var devices = NanoleafDiscovery.ParseAvahiOutput(RealOutput);

        Assert.Equal(2, devices.Count);
        Assert.Equal(["192.168.1.24", "192.168.1.30"], devices.Select(d => d.Host));
    }

    [Fact]
    public void ReadsModelAndFirmwareFromTheTxtRecord()
    {
        var devices = NanoleafDiscovery.ParseAvahiOutput(RealOutput);

        var lamp = devices.Single(d => d.Host == "192.168.1.24");
        Assert.Equal("NL72K4", lamp.Model);
        Assert.Equal("3.0.31", lamp.FirmwareVersion);
        Assert.Equal(16021, lamp.Port);
    }

    [Fact]
    public void DecodesTheEscapedServiceName()
    {
        var lamp = NanoleafDiscovery.ParseAvahiOutput(RealOutput).Single(d => d.Host == "192.168.1.30");

        Assert.Equal("Nanoleaf IML 8B7", lamp.Name);
    }

    [Fact]
    public void CollapsesTheSameDeviceSeenOnSeveralInterfaces()
    {
        // The real output above advertises .30 on both IPv4 and IPv6 over enp7s0.
        var devices = NanoleafDiscovery.ParseAvahiOutput(RealOutput);

        Assert.Equal(devices.Select(d => d.Host).Distinct().Count(), devices.Count);
    }

    [Fact]
    public void IgnoresUnresolvedAdvertisements()
    {
        // Lines beginning with '+' are announcements without an address yet.
        const string onlyAnnouncements = """
            +;wlan0;IPv4;Nanoleaf\032IML\0328B7;_nanoleafapi._tcp;local
            +;wlan0;IPv6;Nanoleaf\032IML\0328B7;_nanoleafapi._tcp;local
            """;

        Assert.Empty(NanoleafDiscovery.ParseAvahiOutput(onlyAnnouncements));
    }

    [Fact]
    public void IgnoresTruncatedLines()
    {
        Assert.Empty(NanoleafDiscovery.ParseAvahiOutput("=;wlan0;IPv4;Name;_nanoleafapi._tcp"));
    }

    [Fact]
    public void IgnoresRecordsWithAnUnparseablePort()
    {
        const string badPort =
            "=;wlan0;IPv4;Name;_nanoleafapi._tcp;local;host.local;192.168.1.5;not-a-port;\"md=NL72K4\"";

        Assert.Empty(NanoleafDiscovery.ParseAvahiOutput(badPort));
    }

    [Fact]
    public void HandlesAMissingTxtRecord()
    {
        const string noTxt = "=;wlan0;IPv4;Name;_nanoleafapi._tcp;local;host.local;192.168.1.5;16021;";

        var device = Assert.Single(NanoleafDiscovery.ParseAvahiOutput(noTxt));
        Assert.Null(device.Model);
        Assert.Null(device.FirmwareVersion);
    }

    [Fact]
    public void HandlesEmptyOutput()
    {
        Assert.Empty(NanoleafDiscovery.ParseAvahiOutput(string.Empty));
    }
}
