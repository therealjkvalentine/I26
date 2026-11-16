using I76.Game.Ui;
using Xunit;
using Says = I76.Game.Ui.StandByLine.Says;

namespace I76.Game.Tests;

/// <summary>
/// The line in a briefing's corner while its mission loads (StandByLine): PLEASE STAND BY faint until the mission is ready to start
/// (its build, then the rest of its load), READY as faint only once it is (a press then starts it at once), and PLEASE STAND BY
/// in full when the player waits on the mission.
/// </summary>
public class StandByLineTests
{
    /// <summary>Under the black before the movie's first frame: nothing yet.</summary>
    [Fact]
    public void NothingBeforeThePicture() => Assert.Equal(Says.Nothing, StandByLine.For(pictureUp: false, ready: false, waitedOn: false).Says);

    /// <summary>The mission not ready (built or not: the rest of its load is still to run): the line, faint, never READY.</summary>
    [Fact]
    public void FaintStandByUntilTheMissionIsReady() => Assert.Equal((Says.StandBy, true), StandByLine.For(pictureUp: true, ready: false, waitedOn: false));

    /// <summary>The mission ready, the movie playing on: READY, as faint, for the rest of it (a press goes straight into the mission).</summary>
    [Fact]
    public void FaintReadyOnlyWhenTheMissionIsReady()
    {
        Assert.Equal((Says.Ready, true), StandByLine.For(pictureUp: true, ready: true, waitedOn: false));
        Assert.Equal("READY", StandByLine.Text(Says.Ready, "PLEASE STAND BY . . ."));
    }

    /// <summary>A skip kept, or the picture held, before the mission can start: the boot screen's line in full.</summary>
    [Fact]
    public void StandByInFullWhenThePlayerWaits()
    {
        Assert.Equal((Says.StandBy, false), StandByLine.For(pictureUp: true, ready: false, waitedOn: true));
        Assert.Equal("PLEASE STAND BY...", StandByLine.Text(Says.StandBy, "PLEASE STAND BY..."));   // (Nitro's line as its boot screen has it)
    }

    /// <summary>A briefing watched while the mission builds and then attaches: stand by all through, READY only at the end.</summary>
    [Fact]
    public void ABriefingSaysStandByThroughTheWholeLoadThenReady()
    {
        var seen = new List<(Says, bool)>();
        void At(bool up, bool ready) { var l = StandByLine.For(up, ready, false); if (seen.Count == 0 || seen[^1] != l) seen.Add(l); }
        At(false, false);
        At(true, false);   // building
        At(true, false);   // built, the rest of the load to run
        At(true, true);    // ready
        Assert.Equal([(Says.Nothing, true), (Says.StandBy, true), (Says.Ready, true)], seen);
    }

    [Theory]
    [InlineData(Says.StandBy, true)]
    [InlineData(Says.Ready, false)]
    [InlineData(Says.Nothing, false)]
    public void TheProgressLineIsUnderStandByOnly(Says says, bool shown) => Assert.Equal(shown, StandByLine.LineShown(says));

    [Theory]
    [InlineData(1080, 2)]
    [InlineData(1440, 2)]   // (3440x1440)
    [InlineData(720, 1)]
    [InlineData(480, 1)]    // a small window: never under a pixel
    [InlineData(2160, 3)]
    public void TheLinesThicknessFollowsTheWindow(float height, int px) => Assert.Equal(px, StandByLine.Thickness(height));

    [Fact]
    public void TheOneAndTwoPixelVariants()
    {
        Assert.Equal(1, StandByLine.Thickness(1080, 1));
        Assert.Equal(1, StandByLine.Thickness(1440, 1));
        Assert.Equal(2, StandByLine.Thickness(1080, 2));
        Assert.Equal(3, StandByLine.Thickness(1440, 2));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(-0.5, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(0.004, 0)]
    [InlineData(0.25, 25)]
    [InlineData(0.5049, 50)]
    [InlineData(0.9999, 99)]    // short of the end: a pixel short, never full while work remains
    [InlineData(1.0, 100)]
    [InlineData(1.5, 100)]
    public void TheLineFillsWholePixelsFromTheLeft(double progress, int filled) => Assert.Equal(filled, StandByLine.Filled(progress, 100));

    [Fact]
    public void TheLineFollowsALoadUpAndIsFullOnlyAtItsEnd()
    {
        var m = new LoadProgressModel(LoadProgressModel.First);
        int width = 137, last = 0;
        foreach (var s in LoadProgressModel.First)
        {
            m.Mark(s.Stamp);
            int f = StandByLine.Filled(m.Value, width);
            Assert.True(f >= last, s.Stamp);
            Assert.True(f < width || s.Stamp == LoadProgressModel.First[^1].Stamp, s.Stamp);
            last = f;
        }
        Assert.Equal(width, last);
        Assert.Equal(0, StandByLine.Filled(0.5, 0));
    }
}
