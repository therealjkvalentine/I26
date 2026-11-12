using I76.Game.Assets;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The art kept per look for F12 (<see cref="LookKept{TKey, T}"/>: the cockpit's frames, radar and fills, its panels and sheets): made
/// once per look from the first change on, handed back on a change back, let go at the mission's end, nothing kept when it is off.
/// </summary>
public class LookKeptTests
{
    private const bool Modern = false, Look1997 = true;

    /// <summary>Art made for a look, counted (what the caller would read and decode from a pack).</summary>
    private sealed class Maker
    {
        public int Made;
        public string Make(bool look) { Made++; return (look ? "1997#" : "modern#") + Made; }
    }

    /// <summary>A caller as the cockpit is: its art shown now, a change of look keeping it and taking back or making the new look's.</summary>
    private static string Change(LookKept<bool, string> kept, Maker maker, ref string shown, bool look)
    {
        var was = shown;
        shown = kept.Change(look, () => was, out var back) ? back : maker.Make(look);
        return shown;
    }

    [Fact]
    public void EachLookIsMadeOnceThenSwapped()
    {
        var kept = new LookKept<bool, string>();
        var maker = new Maker();
        string shown = maker.Make(Modern);
        kept.Made(Modern);
        // modern -> 1997 -> modern -> 1997 ...: two makes in all, the start's and the first 1997
        Assert.Equal("1997#2", Change(kept, maker, ref shown, Look1997));
        Assert.Equal("modern#1", Change(kept, maker, ref shown, Modern));
        Assert.Equal("1997#2", Change(kept, maker, ref shown, Look1997));
        for (int i = 0; i < 10; i++) Change(kept, maker, ref shown, i % 2 == 0 ? Modern : Look1997);
        Assert.Equal(2, maker.Made);
        // what is kept is the look not shown, one entry
        Assert.Equal(1, kept.Count);
        Assert.Equal(Look1997, kept.Shown);
        Assert.Equal("1997#2", shown);
        Assert.Equal(["modern#1"], kept.Others);
        Assert.True(kept.Has(Modern));
        Assert.False(kept.Has(Look1997));
    }

    [Fact]
    public void TheStartsArtIsKeptAtTheFirstChange()
    {
        // a start in the 1997 look: its art (made before any change) is what the first change back hands over
        var kept = new LookKept<bool, string>();
        var maker = new Maker();
        string shown = maker.Make(Look1997);
        kept.Made(Look1997);
        kept.Made(Modern);   // a second note does not move the start's look
        Change(kept, maker, ref shown, Modern);
        Assert.Equal("1997#1", Change(kept, maker, ref shown, Look1997));
        Assert.Equal(2, maker.Made);
    }

    [Fact]
    public void OffEveryChangeMakesItAgainAndNothingIsKept()
    {
        var kept = new LookKept<bool, string> { Keep = false };
        var maker = new Maker();
        string shown = maker.Make(Modern);
        kept.Made(Modern);
        int asked = 0;
        for (int i = 0; i < 6; i++)
        {
            bool look = i % 2 == 0 ? Look1997 : Modern;
            var was = shown;
            shown = kept.Change(look, () => { asked++; return was; }, out var back) ? back : maker.Make(look);
        }
        Assert.Equal(7, maker.Made);
        Assert.Equal(0, kept.Count);
        // the art shown is not even asked for when it is not to be kept
        Assert.Equal(0, asked);
        Assert.False(kept.Has(Modern) || kept.Has(Look1997));
    }

    [Fact]
    public void TurnedOffInTheMissionWhatWasKeptGoes()
    {
        var kept = new LookKept<bool, string>();
        var maker = new Maker();
        string shown = maker.Make(Modern);
        kept.Made(Modern);
        Change(kept, maker, ref shown, Look1997);
        Assert.Equal(1, kept.Count);
        kept.Keep = false;
        Change(kept, maker, ref shown, Modern);
        Assert.Equal(0, kept.Count);
        Assert.Equal("modern#3", shown);
    }

    [Fact]
    public void TheMissionsEndLetsTheOtherLooksGo()
    {
        // the cockpit's key is the factory's look and its own (HD Textures row off: the modern look with the originals, a third look)
        var kept = new LookKept<(bool Hd, bool Faithful), string>();
        kept.Made((true, false));
        kept.Change((false, true), () => "modern 8x", out _);
        kept.Change((false, false), () => "1997", out _);
        Assert.Equal(2, kept.Count);
        var gone = kept.Release();
        Assert.Equal(["1997", "modern 8x"], gone.Order());
        Assert.Equal(0, kept.Count);
        // the art shown is the caller's: not in what was let go, and the look shown is still known
        Assert.Equal((false, false), kept.Shown);
    }

    [Fact]
    public void AChangeToTheLookShownKeepsNothing()
    {
        var kept = new LookKept<bool, string>();
        kept.Made(Modern);
        int asked = 0;
        Assert.False(kept.Change(Modern, () => { asked++; return "x"; }, out _));
        Assert.Equal(0, asked);
        Assert.Equal(0, kept.Count);
    }
}
