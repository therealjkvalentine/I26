using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Only a new press made while a movie plays, and not while its mission still loads under it, skips it (MovieSkipGate).</summary>
public class MovieSkipGateTests
{
    [Fact]
    public void AFreshPressSkips()
    {
        var g = new MovieSkipGate();
        g.HeldAtStart([]);
        Assert.True(g.Press("key:Enter"));
    }

    /// <summary>The Enter, click or pad button that started the mission, still down as the movie starts.</summary>
    [Fact]
    public void APressDownAsTheMovieStartsDoesNotSkipUntilLetGo()
    {
        var g = new MovieSkipGate();
        g.HeldAtStart(["key:Enter", "mouse:Left", "pad0:A"]);
        Assert.False(g.Press("key:Enter"));
        Assert.False(g.Press("mouse:Left"));
        Assert.False(g.Press("pad0:A"));
        g.Release("pad0:A");
        Assert.True(g.Press("pad0:A"));              // let go, then pressed again: the player's skip
        Assert.True(g.Press("pad1:A"));              // another pad's own press
    }

    [Fact]
    public void ASecondPressWithoutARelease_IsNotANewOne()
    {
        var g = new MovieSkipGate();
        Assert.True(g.Press("key:Space"));
        Assert.False(g.Press("key:Space"));
        g.Release("key:Space");
        Assert.True(g.Press("key:Space"));
    }

    /// <summary>A briefing played while its mission loads: no press skips until the load is done, and none is kept for after it.</summary>
    [Fact]
    public void NoPressSkipsWhileTheMissionLoads()
    {
        var g = new MovieSkipGate();
        g.HeldAtStart([]);
        foreach (var input in new[] { "mouse:Left", "mouse:Right", "mouse:Middle", "key:Escape", "key:Space", "key:Enter", "key:KpEnter", "pad0:A", "pad0:B", "pad0:Start" })
            Assert.False(g.Press(input, loading: true));
        g.Release("mouse:Left");
        Assert.False(g.Press("mouse:Left", loading: true));   // pressed again, still loading
        g.Release("mouse:Left");
        Assert.True(g.Press("mouse:Left", loading: false));   // a new press once it is done: the skip
    }

    /// <summary>A press made while the mission loads and still down when the load ends is not a skip: it must be let go and pressed again.</summary>
    [Fact]
    public void APressFromTheLoadStillDownAfterItIsNotASkip()
    {
        var g = new MovieSkipGate();
        Assert.False(g.Press("key:Enter", loading: true));
        Assert.False(g.Press("key:Enter", loading: false));   // (the load done, the same press seen again without a release)
        Assert.True(g.Press("pad0:A", loading: false));       // another input's own new press
        g.Release("key:Enter");
        Assert.True(g.Press("key:Enter", loading: false));
    }

    /// <summary>A movie with no load under it (the intro, a cut-scene, a briefing after the load) skips at once, as before.</summary>
    [Fact]
    public void WithoutALoadAFreshPressSkips()
    {
        var g = new MovieSkipGate();
        g.HeldAtStart(["key:Enter"]);
        Assert.True(g.Press("key:Escape", loading: false));
        Assert.False(g.Press("key:Enter", loading: false));   // the start's own press, still down
    }

    [Fact]
    public void TheWheelAndTheSideButtonsNeverSkip()
    {
        Assert.True(MovieSkipGate.SkipsWith(1));    // left
        Assert.True(MovieSkipGate.SkipsWith(2));    // right
        Assert.True(MovieSkipGate.SkipsWith(3));    // middle
        foreach (int b in new[] { 4, 5, 6, 7, 8, 9 }) Assert.False(MovieSkipGate.SkipsWith(b));   // wheel up/down/left/right, side buttons
    }
}
