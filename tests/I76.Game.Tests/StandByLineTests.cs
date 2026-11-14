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
}
