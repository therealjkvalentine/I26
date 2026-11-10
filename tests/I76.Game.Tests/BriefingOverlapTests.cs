using I76.Game.Boot;
using I76.Game.Shell;
using Xunit;
using Stage = I76.Game.Boot.CardSequence.Stage;

namespace I76.Game.Tests;

/// <summary>
/// A mission start's card and briefing while the world builds ([port] briefing_while_loading): which starts build on a thread,
/// which play the briefing during it, the key's states (upstream's true/false and the fork's older words), and the card's
/// sequence (CardSequence: upstream's short hold, then the briefing).
/// </summary>
public class BriefingOverlapTests
{
    private static bool Threaded(bool look1997 = false, bool network = false, bool editor = false, bool retry = false)
        => BriefingOverlap.Threaded(look1997, network, editor, retry);

    private static bool Plan(bool setting = true, bool threaded = true, string? briefing = "int01f01.smk", bool found = true, bool skip = false)
        => BriefingOverlap.Plan(setting, threaded, briefing, found, skip);

    [Fact]
    public void AModernLookStartBuildsOnAThread()
    {
        Assert.True(Threaded());
        Assert.False(Threaded(look1997: true));   // the 1997 look: built on the main thread behind the card, then the briefing
        Assert.False(Threaded(network: true));
        Assert.False(Threaded(editor: true));
        Assert.False(Threaded(retry: true));
    }

    [Fact]
    public void TheBriefingPlaysDuringABuildWhenItIsThere()
    {
        Assert.True(Plan());
        Assert.False(Plan(setting: false));   // off: the original's order, the load then the briefing
        Assert.False(Plan(threaded: false));
        Assert.False(Plan(briefing: null));
        Assert.False(Plan(briefing: ""));
        Assert.False(Plan(found: false));   // a briefing named but not in smk/
        Assert.False(Plan(skip: true));     // --skip-movies
    }

    [Fact]
    public void TheBriefingPlaysWhileLoadingByDefault() => Assert.True(new ShellSettings().BriefingWhileLoading);

    /// <summary>The menu's warm-up measured no saving (docs/perf/loadtime-plan.md, round 2): off unless shell.cfg says on.</summary>
    [Fact]
    public void TheMenuWarmupIsOffByDefault() => Assert.False(new ShellSettings().MenuWarmup);

    /// <summary>Upstream's shell.cfg writes a bool (False when off); the fork's older words all read as on.</summary>
    [Theory]
    [InlineData("False", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("\"off\"", false)]
    [InlineData("0", false)]
    [InlineData("True", true)]
    [InlineData("on", true)]
    [InlineData("card_first", true)]
    [InlineData("movie_first", true)]
    [InlineData(null, true)]
    public void TheKeyReadsItsStates(string? value, bool expected) => Assert.Equal(expected, BriefingOverlap.Parse(value));

    /// <summary>The card for its short hold, then the movie (the build going on), then the mission when it is ready.</summary>
    [Fact]
    public void TheCardHoldsThenTheMovieThenTheMission()
    {
        var s = new CardSequence(briefing: true, holdSeconds: 1.34);
        Assert.Equal(Stage.Card, s.Now);
        Assert.Equal(Stage.Card, s.Step(1.0, built: false, movieOver: false));
        Assert.Equal(Stage.Movie, s.Step(1.34, built: false, movieOver: false));
        Assert.True(s.MovieShown);
        Assert.Equal(Stage.Movie, s.Step(30, built: true, movieOver: false));   // built: the movie plays on (a skip or its end is held, MoviePlayer)
        Assert.Equal(Stage.Attach, s.Step(60, built: true, movieOver: true));
    }

    /// <summary>The movie gone before the build is done (it could not be played): the card again until it is.</summary>
    [Fact]
    public void AMovieGoneEarlyBringsTheCardBackUntilTheBuildIsDone()
    {
        var s = new CardSequence(briefing: true, holdSeconds: 1.34);
        s.Step(2, built: false, movieOver: false);
        Assert.Equal(Stage.Movie, s.Now);
        Assert.Equal(Stage.CardAgain, s.Step(3, built: false, movieOver: true));
        Assert.Equal(Stage.CardAgain, s.Step(5, built: false, movieOver: true));
        Assert.Equal(Stage.Attach, s.Step(8, built: true, movieOver: true));
    }

    /// <summary>No briefing (a melee, --skip-movies): the card until the build is done, no longer (the original's card is up for its load).</summary>
    [Fact]
    public void WithoutABriefingTheCardHoldsUntilTheBuildIsDone()
    {
        var s = new CardSequence(briefing: false, holdSeconds: 1.34);
        Assert.Equal(Stage.Card, s.Now);
        Assert.Equal(Stage.Card, s.Step(10, built: false, movieOver: false));
        Assert.Equal(Stage.Attach, s.Step(0.5, built: true, movieOver: false));   // built before the hold: no hold
        Assert.False(s.MovieShown);
    }

    /// <summary>Upstream's card hold (0.7 s up, between its fades).</summary>
    [Fact]
    public void TheHoldIsUpstreams() => Assert.Equal(0.7, BriefingOverlap.CardHold);
}
