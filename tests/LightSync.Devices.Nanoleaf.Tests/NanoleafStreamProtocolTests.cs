using LightSync.Core.Colors;
using LightSync.Devices.Nanoleaf;

namespace LightSync.Devices.Nanoleaf.Tests;

public class NanoleafStreamProtocolTests
{
    [Fact]
    public void UsesTheDocumentedStreamPort()
    {
        Assert.Equal(60222, NanoleafStreamProtocol.StreamPort);
    }

    [Fact]
    public void ComputesFrameSizeAsHeaderPlusEightBytesPerPanel()
    {
        Assert.Equal(2, NanoleafStreamProtocol.FrameSizeBytes(0));
        Assert.Equal(10, NanoleafStreamProtocol.FrameSizeBytes(1));

        // The measured NL72K4: 24 LEDs -> 2 + 24*8 = 194 bytes, which is the frame size the
        // real device was observed to accept.
        Assert.Equal(194, NanoleafStreamProtocol.FrameSizeBytes(24));
    }

    [Fact]
    public void WritesTheExactByteLayoutVerifiedAgainstHardware()
    {
        var buffer = new byte[NanoleafStreamProtocol.FrameSizeBytes(1)];

        var written = NanoleafStreamProtocol.WriteFrame(
            buffer, [0x0176], [new RgbColor(0xFF, 0x00, 0x00)], transitionTime: 0x0032);

        Assert.Equal(10, written);
        Assert.Equal<byte[]>(
        [
            0x00, 0x01,       // panel count, big endian
            0x01, 0x76,       // panel id, big endian
            0xFF, 0x00, 0x00, // red, green, blue
            0x00,             // white channel, always zero
            0x00, 0x32,       // transition time, big endian
        ], buffer);
    }

    [Fact]
    public void WritesPanelCountBigEndian()
    {
        // 256 panels distinguishes big endian (0x01,0x00) from little endian (0x00,0x01).
        var ids = Enumerable.Range(0, 256).ToArray();
        var colors = new RgbColor[256];
        var buffer = new byte[NanoleafStreamProtocol.FrameSizeBytes(256)];

        NanoleafStreamProtocol.WriteFrame(buffer, ids, colors);

        Assert.Equal(0x01, buffer[0]);
        Assert.Equal(0x00, buffer[1]);
    }

    [Fact]
    public void WritesPanelIdBigEndian()
    {
        var buffer = new byte[NanoleafStreamProtocol.FrameSizeBytes(1)];

        NanoleafStreamProtocol.WriteFrame(buffer, [0x1234], [RgbColor.Black]);

        Assert.Equal(0x12, buffer[2]);
        Assert.Equal(0x34, buffer[3]);
    }

    [Fact]
    public void WritesChannelsInRedGreenBlueOrder()
    {
        var buffer = new byte[NanoleafStreamProtocol.FrameSizeBytes(1)];

        NanoleafStreamProtocol.WriteFrame(buffer, [0], [new RgbColor(0x11, 0x22, 0x33)]);

        Assert.Equal(0x11, buffer[4]);
        Assert.Equal(0x22, buffer[5]);
        Assert.Equal(0x33, buffer[6]);
    }

    [Fact]
    public void AlwaysZeroesTheWhiteChannel()
    {
        var buffer = new byte[NanoleafStreamProtocol.FrameSizeBytes(2)];
        Array.Fill(buffer, (byte)0xEE);

        NanoleafStreamProtocol.WriteFrame(buffer, [0, 1], [ColorConstants.White, ColorConstants.White]);

        Assert.Equal(0, buffer[7]);
        Assert.Equal(0, buffer[15]);
    }

    [Fact]
    public void WritesEveryPanelSequentially()
    {
        var buffer = new byte[NanoleafStreamProtocol.FrameSizeBytes(3)];

        var written = NanoleafStreamProtocol.WriteFrame(
            buffer,
            [1, 2, 3],
            [ColorConstants.Red, ColorConstants.Green, ColorConstants.Blue]);

        Assert.Equal(26, written);
        Assert.Equal(1, buffer[3]);
        Assert.Equal(255, buffer[4]);
        Assert.Equal(2, buffer[11]);
        Assert.Equal(255, buffer[13]);
        Assert.Equal(3, buffer[19]);
        Assert.Equal(255, buffer[22]);
    }

    [Fact]
    public void DefaultsToAnImmediateTransition()
    {
        var buffer = new byte[NanoleafStreamProtocol.FrameSizeBytes(1)];

        NanoleafStreamProtocol.WriteFrame(buffer, [0], [ColorConstants.Red]);

        Assert.Equal(0x00, buffer[8]);
        Assert.Equal(0x01, buffer[9]);
    }

    [Fact]
    public void AcceptsABufferLargerThanNeededAndReportsBytesWritten()
    {
        var buffer = new byte[1024];

        var written = NanoleafStreamProtocol.WriteFrame(buffer, [0, 1], [RgbColor.Black, RgbColor.Black]);

        Assert.Equal(18, written);
    }

    [Fact]
    public void RejectsABufferTooSmallForTheFrame()
    {
        var tooSmall = new byte[9];

        Assert.Throws<ArgumentException>(
            () => NanoleafStreamProtocol.WriteFrame(tooSmall, [0], [RgbColor.Black]));
    }

    [Fact]
    public void RejectsMismatchedIdAndColourCounts()
    {
        var buffer = new byte[128];

        Assert.Throws<ArgumentException>(
            () => NanoleafStreamProtocol.WriteFrame(buffer, [0, 1], [RgbColor.Black]));
    }

    [Fact]
    public void RejectsAPanelIdThatDoesNotFitTheProtocolField()
    {
        var buffer = new byte[128];

        Assert.Throws<ArgumentException>(
            () => NanoleafStreamProtocol.WriteFrame(buffer, [70000], [RgbColor.Black]));
        Assert.Throws<ArgumentException>(
            () => NanoleafStreamProtocol.WriteFrame(buffer, [-1], [RgbColor.Black]));
    }

    [Fact]
    public void WritesAnEmptyFrameAsJustTheCount()
    {
        var buffer = new byte[8];

        var written = NanoleafStreamProtocol.WriteFrame(buffer, [], []);

        Assert.Equal(2, written);
        Assert.Equal(0, buffer[0]);
        Assert.Equal(0, buffer[1]);
    }

    [Fact]
    public void ProducesTheFrameShapeTheRealLampAccepts()
    {
        // 24 sequential ids starting at 0 is what an NL72K4 was measured to accept; frames
        // mentioning id 24 or above were rejected outright by the device.
        var ids = Enumerable.Range(0, 24).ToArray();
        var colors = new RgbColor[24];
        Array.Fill(colors, ColorConstants.Green);
        var buffer = new byte[NanoleafStreamProtocol.FrameSizeBytes(24)];

        var written = NanoleafStreamProtocol.WriteFrame(buffer, ids, colors);

        Assert.Equal(194, written);
        Assert.Equal(24, (buffer[0] << 8) | buffer[1]);
        Assert.Equal(23, (buffer[186] << 8) | buffer[187]);
    }
}
