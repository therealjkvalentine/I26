using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>A movie's late frames (MoviePace): against the recent schedule, frames passed over included, the game's longest frame.</summary>
public class MoviePaceTests
{
    private const double F = 1 / 15.0;

    /// <summary>Frames shown on time (a 60 Hz display under them: each up to a display frame after its due time) are none of them late.</summary>
    [Fact]
    public void FramesOnTimeAreNotLate()
    {
        var p = new MoviePace(F);
        for (int i = 0; i < 300; i++) p.Frame(10 + i * F + (i % 4) / 60.0, i);
        Assert.Equal(300, p.Frames);
        Assert.Equal(0, p.Late().Late);
        Assert.True(p.WorstFrame < 0.1);
    }

    /// <summary>A 300 ms hitch: the frames due in it came up when it ended; those over 1.5 frame times (100 ms) late count, passed-over ones too.</summary>
    [Fact]
    public void AHitchMakesTheFramesDueInItLate()
    {
        var p = new MoviePace(F);
        for (int i = 0; i <= 30; i++) p.Frame(i * F, i);
        // the game stops for 310 ms after frame 30, then shows frame 34 (31 to 33 passed over)
        p.Frame(30 * F + 0.31, 34);
        for (int i = 35; i < 60; i++) p.Frame(i * F, i);
        var (late, worst) = p.Late();
        // 31 to 34 were due 67, 133, 200 and 267 ms after 30: up at 310, so 243, 177, 110 and 43 ms late: three over 100 ms
        Assert.Equal(3, late);
        Assert.Equal(0.31 - F, worst, 3);
        Assert.Equal(0.31, p.WorstFrame, 3);
    }

    /// <summary>The picture waited on the sound (a long stall) and goes on later: after the window, the new schedule is the one.</summary>
    [Fact]
    public void AScheduleShiftedForGoodIsLateOnlyWhileItShifts()
    {
        var p = new MoviePace(F);
        for (int i = 0; i < 30; i++) p.Frame(i * F, i);
        for (int i = 30; i < 120; i++) p.Frame(i * F + 1.0, i);   // everything from 30 on a second later
        var (late, _) = p.Late();
        // frames 30 on came up a second late until the old schedule left the half-second window: frame 30 and the few after it
        Assert.InRange(late, 1, 8);
        Assert.True(p.Frames == 120);
    }

    /// <summary>The same frame shown again (the game drawing faster than the movie) counts once; frames with nothing shown only time the game.</summary>
    [Fact]
    public void RepeatsAndEmptyFramesAreNotFrames()
    {
        var p = new MoviePace(F);
        p.Frame(0, 0); p.Frame(0.01, -1); p.Frame(0.02, 0); p.Frame(F, 1);
        Assert.Equal(2, p.Frames);
        Assert.Equal(0, p.Late().Late);
        Assert.Equal(F - 0.02, p.WorstFrame, 6);
    }
}
