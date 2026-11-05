using I76.Game.Boot;
using I76.Game.Shell;
using Xunit;
using Stage = I76.Game.Boot.CardSequence.Stage;

namespace I76.Game.Tests;

/// <summary>
/// A mission start's card and briefing while the world builds ([port] briefing_while_loading): which starts build on a thread,
/// which play the briefing during it, the key's two states, and the card's sequence (CardSequence).
/// </summary>
public class BriefingOverlapTests
{
    private static bool Threaded(bool look1997 = false, bool network = false, bool editor = false, bool retry = false)
        => BriefingOverlap.Threaded(look1997, network, editor, retry);

    private static bool Plan(bool threaded = true, string? briefing = "int01f01.smk", bool found = true, bool skip = false)
        => BriefingOverlap.Plan(threaded, briefing, found, skip);

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
        Assert.False(Plan(threaded: false));
        Assert.False(Plan(briefing: null));
        Assert.False(Plan(briefing: ""));
        Assert.False(Plan(found: false));   // a briefing named but not in smk/
        Assert.False(Plan(skip: true));     // --skip-movies
    }

    [Fact]
    public void TheCardLeadsByDefault() => Assert.Equal(CardOrder.CardFirst, new ShellSettings().CardOrder);

    [Theory]
    [InlineData("card_first", CardOrder.CardFirst)]
    [InlineData("movie_first", CardOrder.MovieFirst)]
    [InlineData("\"movie_first\"", CardOrder.MovieFirst)]
    [InlineData("MOVIE_FIRST", CardOrder.MovieFirst)]
    [InlineData("on", CardOrder.CardFirst)]      // the older on/off key: both read as the new default
    [InlineData("off", CardOrder.CardFirst)]
    [InlineData(null, CardOrder.CardFirst)]
    public void TheKeyReadsItsStates(string? value, CardOrder expected) => Assert.Equal(expected, BriefingOverlap.Parse(value));

    [Fact]
    public void TheKeysNamesRoundTrip()
    {
        foreach (var o in new[] { CardOrder.CardFirst, CardOrder.MovieFirst }) Assert.Equal(o, BriefingOverlap.Parse(BriefingOverlap.Name(o)));
    }

    /// <summary>card_first: the card for the hold, then the movie (the build going on), then the mission at once when it is ready.</summary>
    [Fact]
    public void TheCardHoldsThenTheMovieThenTheMission()
    {
        var s = new CardSequence(CardOrder.CardFirst, briefing: true, holdSeconds: 6);
        Assert.Equal(Stage.Card, s.Now);
        Assert.Equal(Stage.Card, s.Step(1, built: false, movieOver: false));
        Assert.Equal(Stage.Card, s.Step(5.9, built: true, movieOver: false));   // built early: the card still holds its time
        Assert.Equal(Stage.Movie, s.Step(6.0, built: true, movieOver: false));
        Assert.True(s.MovieShown);
        Assert.Equal(Stage.Movie, s.Step(30, built: true, movieOver: false));
        Assert.Equal(Stage.Attach, s.Step(60, built: true, movieOver: true));
    }

    /// <summary>The movie skipped (or short) before the build is done: the card again until it is.</summary>
    [Fact]
    public void ASkippedMovieBringsTheCardBackUntilTheBuildIsDone()
    {
        var s = new CardSequence(CardOrder.CardFirst, briefing: true, holdSeconds: 6);
        s.Step(6, built: false, movieOver: false);
        Assert.Equal(Stage.Movie, s.Now);
        Assert.Equal(Stage.CardAgain, s.Step(7, built: false, movieOver: true));
        Assert.Equal(Stage.CardAgain, s.Step(9, built: false, movieOver: true));
        Assert.Equal(Stage.Attach, s.Step(11, built: true, movieOver: true));
    }

    /// <summary>movie_first: the movie at once, no card before it; the card after it until the build is done.</summary>
    [Fact]
    public void MovieFirstPlaysTheBriefingAtOnce()
    {
        var s = new CardSequence(CardOrder.MovieFirst, briefing: true, holdSeconds: 6);
        Assert.Equal(Stage.Movie, s.Now);
        Assert.Equal(Stage.Movie, s.Step(0, built: false, movieOver: false));
        Assert.Equal(Stage.CardAgain, s.Step(3, built: false, movieOver: true));
        Assert.Equal(Stage.Attach, s.Step(8, built: true, movieOver: true));
    }

    /// <summary>No briefing (a melee, --skip-movies): the card until the build is done, and the retail time at least, in either order.</summary>
    [Theory]
    [InlineData(CardOrder.CardFirst)]
    [InlineData(CardOrder.MovieFirst)]
    public void WithoutABriefingTheCardHoldsUntilTheBuildIsDone(CardOrder order)
    {
        var s = new CardSequence(order, briefing: false, holdSeconds: 6);
        Assert.Equal(Stage.Card, s.Now);
        Assert.Equal(Stage.Card, s.Step(2, built: true, movieOver: false));   // built before the hold is over
        Assert.Equal(Stage.Card, s.Step(10, built: false, movieOver: false));
        Assert.Equal(Stage.Attach, s.Step(12, built: true, movieOver: false));
        Assert.False(s.MovieShown);
    }

    [Fact]
    public void TheHoldIsTheRetailCardsTimeByDefault()
    {
        var s = new CardSequence(CardOrder.CardFirst, briefing: true);
        Assert.Equal(Stage.Card, s.Step(BriefingOverlap.RetailCardSeconds - 0.01, built: false, movieOver: false));
        Assert.Equal(Stage.Movie, s.Step(BriefingOverlap.RetailCardSeconds, built: false, movieOver: false));
    }
}
