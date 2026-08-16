using LightSync.Core.Pipeline;

namespace LightSync.Core.Tests;

public class PipelineMetricsTests
{
    [Fact]
    public void CountsEachKindOfEventSeparately()
    {
        var metrics = new PipelineMetrics();

        metrics.RecordCaptured();
        metrics.RecordCaptured();
        metrics.RecordProcessed();
        metrics.RecordDropped();
        metrics.RecordDeviceError();
        metrics.RecordSent(TimeSpan.FromMilliseconds(10));

        Assert.Equal(2, metrics.CapturedFrames);
        Assert.Equal(1, metrics.ProcessedFrames);
        Assert.Equal(1, metrics.DroppedFrames);
        Assert.Equal(1, metrics.DeviceErrors);
        Assert.Equal(1, metrics.SentFrames);
    }

    [Fact]
    public void AveragesLatencyOverEverySentFrame()
    {
        var metrics = new PipelineMetrics();

        metrics.RecordSent(TimeSpan.FromMilliseconds(10));
        metrics.RecordSent(TimeSpan.FromMilliseconds(30));

        Assert.Equal(20.0, metrics.AverageLatency.TotalMilliseconds, 0.001);
    }

    [Fact]
    public void ReportsZeroLatencyBeforeAnythingIsSent()
    {
        Assert.Equal(TimeSpan.Zero, new PipelineMetrics().AverageLatency);
    }

    [Fact]
    public async Task ReportsAFrameRateInTheRightOrderOfMagnitude()
    {
        // Regression guard: the window used to mix Stopwatch ticks with TimeSpan ticks, which
        // under-reported the rate by a factor of 100 on Linux.
        var metrics = new PipelineMetrics();
        const int targetFps = 50;

        var deadline = DateTime.UtcNow.AddMilliseconds(1300);
        while (DateTime.UtcNow < deadline)
        {
            metrics.RecordSent(TimeSpan.FromMilliseconds(1));
            await Task.Delay(1000 / targetFps, TestContext.Current.CancellationToken);
        }

        Assert.InRange(metrics.Fps, targetFps / 3.0, targetFps * 3.0);
    }

    [Fact]
    public void TracksUptimeAsWallClockTime()
    {
        var metrics = new PipelineMetrics();

        Assert.InRange(metrics.Uptime, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SummaryMentionsDeviceErrorsOnlyWhenSomeOccurred()
    {
        var clean = new PipelineMetrics();
        clean.RecordSent(TimeSpan.FromMilliseconds(1));
        Assert.DoesNotContain("device errors", clean.ToString(), StringComparison.Ordinal);

        var faulty = new PipelineMetrics();
        faulty.RecordSent(TimeSpan.FromMilliseconds(1));
        faulty.RecordDeviceError();
        Assert.Contains("device errors", faulty.ToString(), StringComparison.Ordinal);
    }
}
