using LightSync.Core.Capture.Wayland;

namespace LightSync.Core.Tests;

public class PortalSelectionTests
{
    [Theory]
    [InlineData(PortalSourceType.Monitor, false)]
    [InlineData(PortalSourceType.Window, true)]
    [InlineData(PortalSourceType.Virtual, true)]
    public void OnlyWholeMonitorStreamsNeedTheSavedAreaCropped(PortalSourceType type, bool preCropped)
    {
        var selection = new PortalSelection(42, 0, 0, 800, 600, type, null);
        Assert.Equal(preCropped, selection.IsPreCropped);
    }
}
