using System.Diagnostics;
using LightSync.Core.Capture;
using LightSync.Core.Capture.Wayland;

namespace LightSync.Core.Tests;

public class GStreamerFrameSourceTests
{
    private static bool GStreamerAvailable => File.Exists("/usr/bin/gst-launch-1.0")
        || Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Any(dir => File.Exists(Path.Combine(dir, "gst-launch-1.0"))) == true;

    [Fact]
    public async Task BuildsAPipelineRequestingTheExactTargetFormat()
    {
        await using var source = new GStreamerFrameSource(
            "videotestsrc", targetWidth: 96, targetHeight: 8, fps: 30);

        var arguments = string.Join(' ', source.BuildArguments());

        Assert.Contains("videotestsrc", arguments, StringComparison.Ordinal);
        Assert.Contains("format=BGRx", arguments, StringComparison.Ordinal);
        Assert.Contains("width=96", arguments, StringComparison.Ordinal);
        Assert.Contains("height=8", arguments, StringComparison.Ordinal);
        Assert.Contains("framerate=30/1", arguments, StringComparison.Ordinal);
        Assert.Contains("max-rate=30", arguments, StringComparison.Ordinal);
        Assert.Contains("drop-only=true", arguments, StringComparison.Ordinal);
        Assert.Contains("pixel-aspect-ratio=1/1", arguments, StringComparison.Ordinal);
        Assert.Contains("add-borders=true", arguments, StringComparison.Ordinal);
        Assert.Contains("fdsink fd=1", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OmitsCropWhenTheSourceAlreadyMatchesTheRequestedRegion()
    {
        // This is the portal Region case: the stream is already exactly the chosen rectangle.
        await using var source = new GStreamerFrameSource(
            "pipewiresrc path=42",
            targetWidth: 96,
            targetHeight: 8,
            fps: 30,
            crop: new CaptureArea(0, 0, 1600, 1000),
            sourceWidth: 1600,
            sourceHeight: 1000);

        Assert.DoesNotContain("videocrop", string.Join(' ', source.BuildArguments()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CropsInThePipelineWhenAWholeScreenWasSelected()
    {
        // The whole 5120x1440 monitor, with the documented 1600x1000+3200+200 region wanted.
        await using var source = new GStreamerFrameSource(
            "pipewiresrc path=42",
            targetWidth: 96,
            targetHeight: 8,
            fps: 30,
            crop: new CaptureArea(3200, 200, 1600, 1000),
            sourceWidth: 5120,
            sourceHeight: 1440);

        var arguments = string.Join(' ', source.BuildArguments());

        Assert.Contains("videocrop", arguments, StringComparison.Ordinal);
        Assert.Contains("left=3200", arguments, StringComparison.Ordinal);
        Assert.Contains("top=200", arguments, StringComparison.Ordinal);
        Assert.Contains("right=320", arguments, StringComparison.Ordinal);
        Assert.Contains("bottom=240", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputesTheExactFrameSize()
    {
        await using var source = new GStreamerFrameSource(
            "videotestsrc", targetWidth: 96, targetHeight: 8, fps: 30);

        Assert.Equal(96 * 8 * 4, source.FrameSizeBytes);
    }

    [Theory]
    [InlineData(0, 8, 30)]
    [InlineData(96, 0, 30)]
    [InlineData(96, 8, 0)]
    public void RejectsNonPositiveDimensions(int width, int height, int fps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GStreamerFrameSource("videotestsrc", width, height, fps));
    }

    [Fact]
    public void RejectsAnEmptySourceElement()
    {
        Assert.Throws<ArgumentException>(() => new GStreamerFrameSource("  ", 96, 8, 30));
    }

    [Fact]
    public async Task ReadsWholeFramesFromARealGStreamerPipeline()
    {
        Assert.SkipUnless(GStreamerAvailable, "gst-launch-1.0 is not installed.");

        await using var source = new GStreamerFrameSource(
            "videotestsrc pattern=smpte is-live=true", targetWidth: 96, targetHeight: 8, fps: 30);

        var frame = await source.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(frame.IsEmpty);
        Assert.Equal(96, frame.Width);
        Assert.Equal(8, frame.Height);
        Assert.Equal(96 * 8 * 4, frame.Pixels.Length);
        Assert.Equal(1, frame.Sequence);
    }

    [Fact]
    public async Task DeliversSuccessiveFramesWithIncreasingSequenceNumbers()
    {
        Assert.SkipUnless(GStreamerAvailable, "gst-launch-1.0 is not installed.");

        await using var source = new GStreamerFrameSource(
            "videotestsrc pattern=ball is-live=true", targetWidth: 32, targetHeight: 8, fps: 60);

        await source.StartAsync(TestContext.Current.CancellationToken);

        for (var expected = 2; expected <= 5; expected++)
        {
            var frame = await source.ReadFrameAsync(TestContext.Current.CancellationToken);
            Assert.Equal(expected, frame.Sequence);
        }
    }

    [Fact]
    public async Task KeepsReadingUndistortedFramesWhenSourceDimensionsChange()
    {
        Assert.SkipUnless(GStreamerAvailable, "gst-launch-1.0 is not installed.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        // Switch from a landscape window to portrait and back, including a smaller source.
        // concat emits new caps for each input, like PipeWire when a captured window resizes.
        await using var source = new GStreamerFrameSource(
            "videotestsrc pattern=white num-buffers=5 ! video/x-raw,width=80,height=40,pixel-aspect-ratio=1/1 ! resized. " +
            "videotestsrc pattern=red num-buffers=5 ! video/x-raw,width=40,height=80,pixel-aspect-ratio=1/1 ! resized. " +
            "videotestsrc pattern=blue num-buffers=5 ! video/x-raw,width=40,height=20,pixel-aspect-ratio=1/1 ! resized. " +
            "concat name=resized", targetWidth: 32, targetHeight: 16, fps: 30);

        var seenLandscape = false;
        var seenPortrait = false;
        var seenSmaller = false;
        var frame = await source.StartAsync(timeout.Token);
        while (!frame.IsEmpty)
        {
            Assert.Equal(32, frame.Width);
            Assert.Equal(16, frame.Height);
            Assert.Equal(source.FrameSizeBytes, frame.Pixels.Length);
            var center = ((8 * frame.Width) + 16) * CapturedFrame.BytesPerPixel;
            var edge = (8 * frame.Width) * CapturedFrame.BytesPerPixel;
            var data = frame.Pixels.ToArray();
            if (data[center] == 255 && data[center + 1] == 255 && data[center + 2] == 255)
            {
                Assert.Equal(255, data[edge]);
                seenLandscape = true;
            }
            else if (data[center + 2] == 255)
            {
                // Portrait content occupies eight centered columns on the fixed canvas.
                Assert.Equal(0, data[edge]);
                Assert.Equal(0, data[edge + 1]);
                Assert.Equal(0, data[edge + 2]);
                Assert.Equal(255, data[((8 * frame.Width) + 12) * 4 + 2]);
                Assert.Equal(0, data[((8 * frame.Width) + 11) * 4 + 2]);
                seenPortrait = true;
            }
            else if (data[center] == 255)
            {
                Assert.Equal(255, data[edge]);
                seenSmaller = true;
            }

            frame = await source.ReadFrameAsync(timeout.Token);
        }

        Assert.True(seenLandscape);
        Assert.True(seenPortrait);
        Assert.True(seenSmaller);
    }

    [Fact]
    public async Task ReturnsAnEmptyFrameWhenThePipelineEndsCleanly()
    {
        Assert.SkipUnless(GStreamerAvailable, "gst-launch-1.0 is not installed.");

        await using var source = new GStreamerFrameSource(
            "videotestsrc num-buffers=2", targetWidth: 16, targetHeight: 4, fps: 30);

        await source.StartAsync(TestContext.Current.CancellationToken);
        await source.ReadFrameAsync(TestContext.Current.CancellationToken);

        var afterEnd = await source.ReadFrameAsync(TestContext.Current.CancellationToken);

        Assert.True(afterEnd.IsEmpty);
    }

    [Fact]
    public async Task ReportsAFailingPipelineRatherThanReturningGarbage()
    {
        Assert.SkipUnless(GStreamerAvailable, "gst-launch-1.0 is not installed.");

        await using var source = new GStreamerFrameSource(
            "pipewiresrc path=999999999", targetWidth: 16, targetHeight: 4, fps: 30);

        // Either the element fails to launch or the pipeline errors out; both must surface as
        // a CaptureException or an empty frame, never as a partially filled buffer.
        try
        {
            var frame = await source.StartAsync(TestContext.Current.CancellationToken);
            Assert.True(frame.IsEmpty || frame.Pixels.Length == source.FrameSizeBytes);
        }
        catch (CaptureException)
        {
        }
    }

    [Fact]
    public async Task SustainsSixtyFramesPerSecondFromATestPattern()
    {
        Assert.SkipUnless(GStreamerAvailable, "gst-launch-1.0 is not installed.");

        await using var source = new GStreamerFrameSource(
            "videotestsrc pattern=ball is-live=true", targetWidth: 96, targetHeight: 8, fps: 60);

        await source.StartAsync(TestContext.Current.CancellationToken);

        // Warm up so pipeline startup is not counted.
        for (var i = 0; i < 10; i++)
        {
            await source.ReadFrameAsync(TestContext.Current.CancellationToken);
        }

        var started = Stopwatch.GetTimestamp();
        const int frames = 60;
        for (var i = 0; i < frames; i++)
        {
            Assert.False((await source.ReadFrameAsync(TestContext.Current.CancellationToken)).IsEmpty);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var fps = frames / elapsed.TotalSeconds;

        // Generous lower bound: this asserts the reader keeps up with a 60 fps source rather
        // than benchmarking the machine.
        Assert.True(fps > 30, $"only sustained {fps:0.0} fps");
    }

    [Fact]
    public async Task DisposingBeforeStartingIsHarmless()
    {
        var source = new GStreamerFrameSource("videotestsrc", 16, 4, 30);

        await source.DisposeAsync();
    }

    [Fact]
    public async Task ReadingBeforeStartingIsRejected()
    {
        await using var source = new GStreamerFrameSource("videotestsrc", 16, 4, 30);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.ReadFrameAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelsARealPipelineThatStopsDeliveringFrames()
    {
        Assert.SkipUnless(GStreamerAvailable, "gst-launch-1.0 is not installed.");

        await using var source = new RecoveringFrameSource(
            () => new GStreamerFrameSource("videotestsrc is-live=true ! valve drop=true", 16, 4, 30),
            frameTimeout: TimeSpan.FromSeconds(1), startupTimeout: TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<TimeoutException>(() => source.StartAsync(TestContext.Current.CancellationToken));
        // Disposal then terminates the child after the cancelled stdout read has finished.
    }
}
