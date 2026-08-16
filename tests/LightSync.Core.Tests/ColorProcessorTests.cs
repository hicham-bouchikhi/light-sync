using LightSync.Core.Capture;
using LightSync.Core.Colors;
using LightSync.Core.Mapping;
using LightSync.Core.Processing;

namespace LightSync.Core.Tests;

public class ColorProcessorTests
{
    private static readonly ColorProcessorOptions Neutral = new()
    {
        Brightness = 1.0,
        Gamma = 1.0,
        Saturation = 1.0,
        Smoothing = 0.0,
        BlackLevel = 0.0,
    };

    /// <summary>Builds a BGRx frame from a per-pixel colour function.</summary>
    private static CapturedFrame Frame(int width, int height, Func<int, int, RgbColor> pixel)
    {
        var bytes = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = pixel(x, y);
                var offset = ((y * width) + x) * 4;
                bytes[offset] = color.B;
                bytes[offset + 1] = color.G;
                bytes[offset + 2] = color.R;
                bytes[offset + 3] = 255;
            }
        }

        return new CapturedFrame(bytes, width, height, Sequence: 1, Timestamp: TimeSpan.Zero);
    }

    private static ColorProcessor Processor(
        int zoneCount = 4,
        ZoneLayout layout = ZoneLayout.Vertical,
        ZoneDirection direction = ZoneDirection.LeftToRight,
        bool reverse = false,
        ColorProcessorOptions? options = null,
        IReadOnlyList<int>? customOrder = null) =>
        new(new ZoneMapper(zoneCount, layout, direction, reverse, customOrder), options ?? Neutral);

    [Fact]
    public void SplitsVerticalZonesLeftToRight()
    {
        // Four columns, each a distinct colour.
        RgbColor[] columns = [ColorConstants.Red, ColorConstants.Green, ColorConstants.Blue, ColorConstants.White];
        var frame = Frame(4, 2, (x, _) => columns[x]);
        var processor = Processor(zoneCount: 4);
        var output = new RgbColor[4];

        processor.Process(frame, output);

        Assert.Equal(columns, output);
    }

    [Fact]
    public void ReversedVerticalMappingEmitsColumnsRightToLeft()
    {
        RgbColor[] columns = [ColorConstants.Red, ColorConstants.Green, ColorConstants.Blue, ColorConstants.White];
        var frame = Frame(4, 2, (x, _) => columns[x]);
        var processor = Processor(zoneCount: 4, reverse: true);
        var output = new RgbColor[4];

        processor.Process(frame, output);

        Assert.Equal(columns.Reverse(), output);
    }

    [Fact]
    public void SplitsHorizontalZonesTopToBottom()
    {
        RgbColor[] rows = [ColorConstants.Red, ColorConstants.Green, ColorConstants.Blue];
        var frame = Frame(2, 3, (_, y) => rows[y]);
        var processor = Processor(zoneCount: 3, layout: ZoneLayout.Horizontal, direction: ZoneDirection.TopToBottom);
        var output = new RgbColor[3];

        processor.Process(frame, output);

        Assert.Equal(rows, output);
    }

    [Fact]
    public void HorizontalBottomToTopEmitsRowsInReverse()
    {
        RgbColor[] rows = [ColorConstants.Red, ColorConstants.Green, ColorConstants.Blue];
        var frame = Frame(2, 3, (_, y) => rows[y]);
        var processor = Processor(zoneCount: 3, layout: ZoneLayout.Horizontal, direction: ZoneDirection.BottomToTop);
        var output = new RgbColor[3];

        processor.Process(frame, output);

        Assert.Equal(rows.Reverse(), output);
    }

    [Fact]
    public void CustomOrderRoutesSlicesToTheRequestedOutputs()
    {
        RgbColor[] columns = [ColorConstants.Red, ColorConstants.Green, ColorConstants.Blue];
        var frame = Frame(3, 1, (x, _) => columns[x]);
        var processor = Processor(zoneCount: 3, customOrder: [2, 0, 1]);
        var output = new RgbColor[3];

        processor.Process(frame, output);

        Assert.Equal([ColorConstants.Blue, ColorConstants.Red, ColorConstants.Green], output);
    }

    [Fact]
    public void AveragesEveryPixelInAZone()
    {
        // A single zone over black and white pixels averages to mid grey.
        var frame = Frame(2, 1, (x, _) => x == 0 ? RgbColor.Black : ColorConstants.White);
        var processor = Processor(zoneCount: 1);
        var output = new RgbColor[1];

        processor.Process(frame, output);

        Assert.Equal(new RgbColor(127, 127, 127), output[0]);
    }

    [Fact]
    public void AveragesDownTheFullHeightOfAColumn()
    {
        var frame = Frame(1, 4, (_, y) => y < 2 ? RgbColor.Black : ColorConstants.White);
        var processor = Processor(zoneCount: 1);
        var output = new RgbColor[1];

        processor.Process(frame, output);

        Assert.Equal(new RgbColor(127, 127, 127), output[0]);
    }

    [Fact]
    public void SpreadsTheRemainderWhenWidthDoesNotDivideByZoneCount()
    {
        // 10 columns over 4 zones: boundaries at 0, 2, 5, 7, 10, so the widest zone is 3
        // columns and the narrowest 2 — the remainder is spread rather than dumped on one end.
        var frame = Frame(10, 1, (x, _) => new RgbColor((byte)(x * 20), 0, 0));
        var processor = Processor(zoneCount: 4);
        var output = new RgbColor[4];

        processor.Process(frame, output);

        // The source ramps left to right, so a correct split is strictly increasing.
        Assert.True(output[0].R < output[1].R);
        Assert.True(output[1].R < output[2].R);
        Assert.True(output[2].R < output[3].R);
    }

    [Fact]
    public void HandlesMoreZonesThanCapturedColumns()
    {
        var frame = Frame(2, 1, (x, _) => x == 0 ? ColorConstants.Red : ColorConstants.Blue);
        var processor = Processor(zoneCount: 6);
        var output = new RgbColor[6];

        processor.Process(frame, output);

        Assert.All(output, color => Assert.NotEqual(RgbColor.Black, color));
        Assert.Equal(ColorConstants.Red, output[0]);
        Assert.Equal(ColorConstants.Blue, output[5]);
    }

    [Fact]
    public void FirstFrameIsNotSmoothedAgainstAnImaginaryBlackFrame()
    {
        var frame = Frame(1, 1, (_, _) => ColorConstants.White);
        var processor = Processor(zoneCount: 1, options: Neutral with { Smoothing = 0.9 });
        var output = new RgbColor[1];

        processor.Process(frame, output);

        Assert.Equal(ColorConstants.White, output[0]);
    }

    [Fact]
    public void SmoothingHoldsBackASuddenChange()
    {
        var white = Frame(1, 1, (_, _) => ColorConstants.White);
        var black = Frame(1, 1, (_, _) => RgbColor.Black);
        var processor = Processor(zoneCount: 1, options: Neutral with { Smoothing = 0.5 });
        var output = new RgbColor[1];

        processor.Process(white, output);
        processor.Process(black, output);

        Assert.Equal(new RgbColor(128, 128, 128), output[0]);
    }

    [Fact]
    public void SmoothingConvergesWhenTheSceneHoldsStill()
    {
        var white = Frame(1, 1, (_, _) => ColorConstants.White);
        var black = Frame(1, 1, (_, _) => RgbColor.Black);
        var processor = Processor(zoneCount: 1, options: Neutral with { Smoothing = 0.5 });
        var output = new RgbColor[1];

        processor.Process(white, output);
        for (var i = 0; i < 50; i++)
        {
            processor.Process(black, output);
        }

        Assert.Equal(RgbColor.Black, output[0]);
    }

    [Fact]
    public void ResetDiscardsSmoothingHistory()
    {
        var white = Frame(1, 1, (_, _) => ColorConstants.White);
        var black = Frame(1, 1, (_, _) => RgbColor.Black);
        var processor = Processor(zoneCount: 1, options: Neutral with { Smoothing = 0.9 });
        var output = new RgbColor[1];

        processor.Process(white, output);
        processor.Reset();
        processor.Process(black, output);

        Assert.Equal(RgbColor.Black, output[0]);
    }

    [Fact]
    public void AppliesTheAdjustmentChainToEveryZone()
    {
        var frame = Frame(2, 1, (_, _) => new RgbColor(100, 100, 100));
        var processor = Processor(zoneCount: 2, options: Neutral with { Brightness = 0.5 });
        var output = new RgbColor[2];

        processor.Process(frame, output);

        Assert.All(output, color => Assert.Equal(new RgbColor(50, 50, 50), color));
    }

    [Fact]
    public void AnEmptyFrameClearsTheOutputRatherThanLeavingStaleColours()
    {
        var processor = Processor(zoneCount: 3);
        var output = new RgbColor[3];
        Array.Fill(output, ColorConstants.Red);

        processor.Process(default, output);

        Assert.All(output, color => Assert.Equal(RgbColor.Black, color));
    }

    [Fact]
    public void AcceptsADestinationLargerThanTheZoneCount()
    {
        var frame = Frame(2, 1, (_, _) => ColorConstants.Red);
        var processor = Processor(zoneCount: 2);
        var output = new RgbColor[5];

        processor.Process(frame, output);

        Assert.Equal(ColorConstants.Red, output[0]);
        Assert.Equal(ColorConstants.Red, output[1]);
    }

    [Fact]
    public void RejectsADestinationTooSmallForTheZoneCount()
    {
        var frame = Frame(4, 1, (_, _) => ColorConstants.Red);
        var processor = Processor(zoneCount: 4);

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            var tooSmall = new RgbColor[2];
            processor.Process(frame, tooSmall);
        });

        Assert.Contains("4 zones", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidProcessingOptions()
    {
        var mapper = new ZoneMapper(4, ZoneLayout.Vertical, ZoneDirection.LeftToRight, reverse: false);

        Assert.Throws<ArgumentException>(() => new ColorProcessor(mapper, Neutral with { Gamma = 0.0 }));
        Assert.Throws<ArgumentException>(() => new ColorProcessor(mapper, Neutral with { Smoothing = 1.0 }));
        Assert.Throws<ArgumentException>(() => new ColorProcessor(mapper, Neutral with { BlackLevel = 2.0 }));
    }

    [Fact]
    public void ProcessesTheDefaultTwentyFourZoneLayoutOverARealisticFrame()
    {
        // 96x8 is the shape the GStreamer stage downscales a 1600x1000 region to for 24 zones.
        var frame = Frame(96, 8, (x, _) => new RgbColor((byte)(x * 255 / 95), 0, 0));
        var processor = Processor(zoneCount: 24);
        var output = new RgbColor[24];

        processor.Process(frame, output);

        Assert.Equal(24, processor.ZoneCount);
        for (var zone = 1; zone < 24; zone++)
        {
            Assert.True(output[zone].R > output[zone - 1].R);
        }
    }
}
