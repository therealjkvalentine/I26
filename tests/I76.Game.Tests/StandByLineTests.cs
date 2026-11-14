using I76.Game.Ui;
using Xunit;
using Says = I76.Game.Ui.StandByLine.Says;

namespace I76.Game.Tests;

/// <summary>
/// The line in a briefing's corner while its mission loads (StandByLine): PLEASE STAND BY faint while the build works and no press
/// skips, READY as faint once one does, and PLEASE STAND BY in full when the player waits on the mission.
/// </summary>
public class StandByLineTests
{
    /// <summary>Under the black before the movie's first frame: nothing yet.</summary>
    [Fact]
    public void NothingBeforeThePicture() => Assert.Equal(Says.Nothing, StandByLine.For(pictureUp: false, loading: true, waitedOn: false).Says);

    /// <summary>The build still working under the playing picture: the line, faint (a press does nothing now).</summary>
    [Fact]
    public void FaintStandByWhileItLoads() => Assert.Equal((Says.StandBy, true), StandByLine.For(pictureUp: true, loading: true, waitedOn: false));

    /// <summary>The build done, the movie playing on: READY, as faint, for the rest of it (a new press skips now).</summary>
    [Fact]
    public void FaintReadyOnceItIsLoaded()
    {
        Assert.Equal((Says.Ready, true), StandByLine.For(pictureUp: true, loading: false, waitedOn: false));
        Assert.Equal("READY", StandByLine.Text(Says.Ready, "PLEASE STAND BY . . ."));
    }

    /// <summary>A skip kept, or the picture held, before the mission can start: the boot screen's line in full, READY or not before.</summary>
    [Fact]
    public void StandByInFullWhenThePlayerWaits()
    {
        Assert.Equal((Says.StandBy, false), StandByLine.For(pictureUp: true, loading: false, waitedOn: true));
        Assert.Equal((Says.StandBy, false), StandByLine.For(pictureUp: true, loading: true, waitedOn: true));
        Assert.Equal("PLEASE STAND BY...", StandByLine.Text(Says.StandBy, "PLEASE STAND BY..."));   // (Nitro's line as its boot screen has it)
    }

    /// <summary>The whole of a briefing watched past its load, then skipped: stand by, ready, then stand by in full.</summary>
    [Fact]
    public void ABriefingWatchedThenSkippedGoesStandByReadyStandBy()
    {
        var seen = new List<(Says, bool)>();
        void At(bool up, bool loading, bool waited) { var l = StandByLine.For(up, loading, waited); if (seen.Count == 0 || seen[^1] != l) seen.Add(l); }
        At(false, true, false);
        At(true, true, false);
        At(true, true, false);
        At(true, false, false);
        At(true, false, false);
        At(true, false, true);
        Assert.Equal([(Says.Nothing, true), (Says.StandBy, true), (Says.Ready, true), (Says.StandBy, false)], seen);
    }
}
