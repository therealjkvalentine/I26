using Godot;
using I76.Game.Boot;
using I76.Game.Sim;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The main thread's work in a fight (docs/perf/plan.md, "T05 at 60 fps"): the radar screen made again only when what it shows
/// changed, its blips filled on raw bytes as Image.FillRect fills them, and the drop shadows' walk of the scenery spread over frames
/// in the order the one-frame walk took.
/// </summary>
public class FrameCostTests
{
    private static readonly Rect2I A = new(4, 4, 2, 2), B = new(6, 4, 2, 2);

    [Fact]
    public void AnUnchangedScopeIsNotDrawnAgain()
    {
        var p = new Cockpit.ScopePicture();
        var frame = new object();
        Assert.True(p.Update(frame, "range_3", "radled_1", [(A, Colors.White)], force: false));
        Assert.False(p.Update(frame, "range_3", "radled_1", [(A, Colors.White)], force: false));
    }

    [Fact]
    public void AnyChangeOnTheScopeDrawsItAgain()
    {
        var p = new Cockpit.ScopePicture();
        object f1 = new(), f2 = new();
        p.Update(f1, "range_3", "radled_1", [(A, Colors.White)], false);
        Assert.True(p.Update(f1, "range_3", "radled_1", [(B, Colors.White)], false));     // a blip moved a pixel
        Assert.True(p.Update(f1, "range_3", "radled_1", [(B, Colors.Black)], false));     // its colour faded
        Assert.True(p.Update(f1, "range_3", "radled_1", [(B, Colors.Black), (A, Colors.White)], false));   // a car came on
        Assert.True(p.Update(f2, "range_3", "radled_1", [(B, Colors.Black), (A, Colors.White)], false));   // the locked frame turned
        Assert.True(p.Update(f2, "range_1", "radled_1", [(B, Colors.Black), (A, Colors.White)], false));   // R flipped the range
        Assert.True(p.Update(f2, "range_1", "radled_2", [(B, Colors.Black), (A, Colors.White)], false));   // the LED
        Assert.True(p.Update(f2, "range_1", "radled_2", [(B, Colors.Black), (A, Colors.White)], true));    // a forced redraw (panels reset)
        p.Forget();   // the weapon camera had the screen
        Assert.True(p.Update(f2, "range_1", "radled_2", [(B, Colors.Black), (A, Colors.White)], false));
    }

    [Fact]
    public void ABlipFillsItsSquareClippedToThePicture()
    {
        var d = new byte[8 * 4 * 4];
        Cockpit.FillRgba(d, 8, 4, new Rect2I(6, 2, 4, 4), 0x11223344u);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++)
            {
                uint px = System.BitConverter.ToUInt32(d, (y * 8 + x) * 4);
                Assert.Equal(x >= 6 && y >= 2 ? 0x11223344u : 0u, px);
            }
    }

    private sealed class N(string name, params N[] kids)
    {
        public string Name = name;
        public N[] Kids = kids;
    }

    private static N Tree() => new("root", new N("a", new N("a1"), new N("a2", new N("a2x"))), new N("b"), new N("c", new N("c1")));

    private static List<string> Recursive(N n, List<string>? into = null)
    {
        into ??= [];
        into.Add(n.Name);
        foreach (var k in n.Kids) Recursive(k, into);
        return into;
    }

    [Fact]
    public void AWalkSpreadOverFramesVisitsInTheOneFrameWalksOrder()
    {
        var walk = new StepWalk<N>(n => n.Kids);
        var seen = new List<string>();
        walk.Start(Tree());
        int frames = 0;
        // two nodes a frame
        while (true)
        {
            frames++;
            int left = 1;
            if (walk.Step(n => { seen.Add(n.Name); return true; }, () => left-- > 0)) break;
        }
        Assert.Equal(Recursive(Tree()), seen);
        Assert.Equal(4, frames);   // 8 nodes, two a frame
    }

    [Fact]
    public void AWalkSkipsTheChildrenOfANodeItLeavesOut()
    {
        var walk = new StepWalk<N>(n => n.Kids);
        var seen = new List<string>();
        walk.Start(Tree());
        Assert.True(walk.Step(n => { seen.Add(n.Name); return n.Name != "a"; }, () => true));
        Assert.Equal(["root", "a", "b", "c", "c1"], seen);
        Assert.False(walk.Walking);
    }

    private static readonly int P100 = System.Array.IndexOf(RenderScale.Percents, 100), P85 = System.Array.IndexOf(RenderScale.Percents, 85),
        P75 = System.Array.IndexOf(RenderScale.Percents, 75), P67 = System.Array.IndexOf(RenderScale.Percents, 67);

    /// <summary>A window's frames at one GPU and wall time: the last frame's answer.</summary>
    private static int? Window(FrameTarget t, double gpu, double wall, int current, int player)
    {
        int? r = null;
        for (int i = 0; i < FrameTarget.Window; i++) r = t.Add(gpu, wall, current, player);
        return r;
    }

    [Fact]
    public void TheTargetStepsTheScaleDownWhileTheCardHoldsTheFrame()
    {
        var t = new FrameTarget(120);   // 8.3 ms
        Assert.Equal(P85, Window(t, 9.0, 9.2, P100, P100));
        Assert.Equal(P75, Window(t, 8.0, 8.6, P85, P100));
        Assert.Equal(P67, Window(t, 8.0, 8.6, P75, P100));
        Assert.Null(Window(t, 8.0, 8.6, P67, P100));   // 67 % is as low as it goes
    }

    [Fact]
    public void TheTargetLeavesAFrameTheMainThreadHolds()
    {
        var t = new FrameTarget(120);
        Assert.Null(Window(t, 5.0, 12.0, P100, P100));   // 12 ms frames with the card at 5: fewer pixels would not help
    }

    [Fact]
    public void TheTargetStepsBackUpOnlyWhenTheNextStepFitsSeveralTimesAndNeverPastThePlayers()
    {
        var t = new FrameTarget(120);
        // at 75 % the card takes 4 ms: 85 % would take 4 x (85/75)^2 = 5.1 ms, inside 80 % of 8.3
        for (int i = 1; i < FrameTarget.Calm; i++) Assert.Null(Window(t, 4.0, 8.3, P75, P100));
        Assert.Equal(P85, Window(t, 4.0, 8.3, P75, P100));
        // at the player's own 85 % it stays, however much room there is
        var u = new FrameTarget(120);
        for (int i = 0; i < 5; i++) Assert.Null(Window(u, 2.0, 8.3, P85, P85));
        // a step that would not fit (6 ms at 75 %: 7.7 ms at 85 %) is not taken
        var v = new FrameTarget(120);
        for (int i = 0; i < 5; i++) Assert.Null(Window(v, 6.0, 8.3, P75, P100));
    }

    [Fact]
    public void TheTargetGoesBackToThePlayersScaleWhenItIsLowered()
    {
        var t = new FrameTarget(60);
        Assert.Equal(P75, Window(t, 5.0, 10.0, P85, P75));   // the player chose 75 % in the menu: no higher than that
    }
}
