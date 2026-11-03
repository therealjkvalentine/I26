using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The Show FPS overlay's ring of frame times (Ui/FrameStats.cs): window trimming, rate, percentile, text.</summary>
public class FrameStatsTests
{
    [Fact]
    public void SteadyFramesReadAsTheirRate()
    {
        var s = new FrameStats();
        for (int i = 0; i < 300; i++) s.Add(1.0 / 100);
        Assert.Equal(100, s.Fps(0.5), 3);
        Assert.Equal(0.01, s.Percentile(0.97), 6);
        Assert.Equal("FPS 100  p97 100  10.0 ms", s.Text());
    }

    [Fact]
    public void TheRingKeepsOnlyTheWindow()
    {
        var s = new FrameStats(5.0);
        for (int i = 0; i < 1000; i++) s.Add(0.1);   // 100 s of 10 fps
        Assert.InRange(s.Count, 49, 51);
        for (int i = 0; i < 600; i++) s.Add(0.01);    // then 6 s of 100 fps: the slow frames are gone
        Assert.Equal(0.01, s.Percentile(0.97), 6);
    }

    [Fact]
    public void ThePercentileIsTheSlowEnd()
    {
        var s = new FrameStats();
        for (int i = 0; i < 97; i++) s.Add(0.005);
        for (int i = 0; i < 3; i++) s.Add(0.05);
        Assert.Equal(0.005, s.Percentile(0.97), 6);   // 97 of 100 frames were this quick or quicker
        Assert.Equal(0.05, s.Percentile(0.98), 6);
        Assert.Equal(0.05, s.Percentile(1.0), 6);
    }

    [Fact]
    public void NothingYetAndBadDeltasAreIgnored()
    {
        var s = new FrameStats();
        Assert.Equal("FPS --", s.Text());
        s.Add(0); s.Add(-1); s.Add(double.NaN);
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void ACapacityFullRingDropsTheOldest()
    {
        var s = new FrameStats(1000, capacity: 8);
        for (int i = 0; i < 20; i++) s.Add(0.001 * (i + 1));
        Assert.Equal(8, s.Count);
        Assert.Equal(0.020, s.Percentile(1.0), 6);
        Assert.Equal(0.013, s.Percentile(0.0), 6);
    }
}
