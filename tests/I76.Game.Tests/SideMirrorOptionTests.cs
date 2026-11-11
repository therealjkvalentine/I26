using System.Numerics;
using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's Side Mirrors row (SideMirrorRow over [port] side_mirrors: Off, Left, Both) and the test that leaves a mirror
/// out of view undrawn (MirrorCull). Without the engine.
/// </summary>
public class SideMirrorOptionTests
{
    [Theory]
    [InlineData("off", SideMirrorMode.Off)]
    [InlineData("on", SideMirrorMode.On)]
    [InlineData("auto", SideMirrorMode.Auto)]
    [InlineData("left", SideMirrorMode.Left)]
    [InlineData(" Left ", SideMirrorMode.Left)]
    [InlineData("right", SideMirrorMode.Auto)]   // no such word: the default
    [InlineData(null, SideMirrorMode.Auto)]
    public void TheKeysWordsReadBack(string? word, SideMirrorMode mode) => Assert.Equal(mode, SideMirrorRow.Parse(word));

    [Fact]
    public void EveryModeIsWrittenAndReadBackAndAutoIsNotWritten()
    {
        foreach (var m in Enum.GetValues<SideMirrorMode>())
            Assert.Equal(m, SideMirrorRow.Parse(SideMirrorRow.Word(m)));
        Assert.Null(SideMirrorRow.Word(SideMirrorMode.Auto));
        Assert.Equal("left", SideMirrorRow.Word(SideMirrorMode.Left));
        Assert.Equal(SideMirrorMode.Auto, new ShellSettings().SideMirrors);
    }

    [Fact]
    public void TheRowIsOffLeftBothAndKeepsAnOnSetByHand()
    {
        Assert.Equal(["Off", "Left*", "Both*"], SideMirrorRow.Names);
        var s = new ShellSettings();
        Assert.Equal(2, SideMirrorRow.Index(s.SideMirrors));   // Both, today's default
        SideMirrorRow.Step(s);
        Assert.Equal(SideMirrorMode.Off, s.SideMirrors);
        SideMirrorRow.Step(s);
        Assert.Equal(SideMirrorMode.Left, s.SideMirrors);
        SideMirrorRow.Step(s);
        Assert.Equal(SideMirrorMode.Auto, s.SideMirrors);
        // on (both, and the added glasses) shows as Both and stays on when Both is picked again
        var on = new ShellSettings { SideMirrors = SideMirrorMode.On };
        Assert.Equal(2, SideMirrorRow.Index(on.SideMirrors));
        SideMirrorRow.Put(on, 2);
        Assert.Equal(SideMirrorMode.On, on.SideMirrors);
        SideMirrorRow.Put(on, 1);
        Assert.Equal(SideMirrorMode.Left, on.SideMirrors);
    }

    [Fact]
    public void LeftIsTheDriversDoorMirrorAlone()
    {
        Assert.True(MirrorBands.SideShown(SideMirrorMode.Left, left: true));
        Assert.False(MirrorBands.SideShown(SideMirrorMode.Left, left: false));
        Assert.Equal((true, false), MirrorBands.Shown(SideMirrorMode.Left, hasGlass: true));
        Assert.Equal((false, false), MirrorBands.Shown(SideMirrorMode.Left, hasGlass: false));
        foreach (bool left in new[] { true, false })
        {
            Assert.False(MirrorBands.SideShown(SideMirrorMode.Off, left));
            Assert.True(MirrorBands.SideShown(SideMirrorMode.Auto, left));
            Assert.True(MirrorBands.SideShown(SideMirrorMode.On, left));
        }
    }

    [Fact]
    public void TheRowIsOnTheSheetUnderRearMirrorAndCopiedWithIt()
    {
        var sheet = GraphicsPages.Rows[GraphicsPages.Sheet];
        Assert.Equal(Array.IndexOf(sheet, GraphicsRow.RearMirror) + 1, Array.IndexOf(sheet, GraphicsRow.SideMirrors));
        var to = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.SideMirrors, new ShellSettings { SideMirrors = SideMirrorMode.Left }, to);
        Assert.Equal(SideMirrorMode.Left, to.SideMirrors);
        Assert.Contains("Left", GraphicsHelp.Text(GraphicsRow.SideMirrors, false));
        Assert.Contains("1997 look", GraphicsHelp.Text(GraphicsRow.SideMirrors, true));
    }

    [Fact]
    public void TheMirrorRateReadsBackAndAlternateIsNotWritten()
    {
        foreach (var r in Enum.GetValues<MirrorRate>()) Assert.Equal(r, MirrorRateRow.Parse(MirrorRateRow.Word(r)));
        Assert.Null(MirrorRateRow.Word(MirrorRate.Alternate));
        Assert.Equal(MirrorRate.Alternate, MirrorRateRow.Parse("nonsense"));
        Assert.Equal(MirrorRate.Alternate, new ShellSettings().MirrorRate);
        Assert.Equal(["Alternate*", "Half", "Full*"], MirrorRateRow.Names);
        var to = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.MirrorRate, new ShellSettings { MirrorRate = MirrorRate.Full }, to);
        Assert.Equal(MirrorRate.Full, to.MirrorRate);
    }

    /// <summary>Over eight frames: how often the rear mirror and each side are drawn (the tick every second frame, the sides' turn flipping each tick).</summary>
    private static (int Rear, int L, int R) Count(MirrorRate rate, bool rightSeen = true)
    {
        int rear = 0, l = 0, r = 0;
        bool tick = false, turn = false;
        for (int f = 0; f < 8; f++)
        {
            tick = !tick;
            if (tick) turn = !turn;
            if (MirrorRateRow.Draws(rate, tick)) rear++;
            if (MirrorRateRow.Draws(rate, tick, side: true, turn: turn, otherSeen: rightSeen)) l++;
            if (rightSeen && MirrorRateRow.Draws(rate, tick, side: true, turn: !turn, otherSeen: true)) r++;
        }
        return (rear, l, r);
    }

    [Fact]
    public void FullDrawsEveryFrameHalfEverySecondAlternateTheSidesInTurn()
    {
        Assert.Equal((8, 8, 8), Count(MirrorRate.Full));
        Assert.Equal((4, 4, 4), Count(MirrorRate.Half));
        Assert.Equal((4, 2, 2), Count(MirrorRate.Alternate));
        // a side alone in view (the other out of view or Left): every second frame under Alternate too
        Assert.Equal((4, 4, 0), Count(MirrorRate.Alternate, rightSeen: false));
    }

    /// <summary>A 10 cm box 1.4 m from the eye, <paramref name="yaw"/> degrees right of the view's axis (negative left) and <paramref name="pitch"/> up, in the camera's frame (the view down -z).</summary>
    private static Vector3[] Box(float yaw, float pitch = -5f, float distance = 1.4f)
    {
        float y = yaw * MathF.PI / 180f, p = pitch * MathF.PI / 180f;
        var c = new Vector3(MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p), -MathF.Cos(y) * MathF.Cos(p)) * distance;
        var corners = new Vector3[8];
        for (int k = 0; k < 8; k++) corners[k] = c + new Vector3((k & 1) == 0 ? -0.05f : 0.05f, (k & 2) == 0 ? -0.05f : 0.05f, (k & 4) == 0 ? -0.05f : 0.05f);
        return corners;
    }

    [Fact]
    public void AGlassOutsideTheViewIsNotDrawn()
    {
        // the owner's view: Field of View Monitor (the original's 73.74 high) on a 3440x1440 window, 60.8 degrees to each side
        var (tx, ty) = MirrorCull.HalfTangents(73.74f, keepHeight: true, 3440f / 1440f, 0f);
        Assert.Equal(60.8f, MathF.Atan(tx) * 180f / MathF.PI, 0.1f);
        // the Piranha's right glass 73 degrees right, looking ahead: out; turned 60 to the right: in
        Assert.False(MirrorCull.InView(Box(73f), tx, ty));
        Assert.True(MirrorCull.InView(Box(73f - 60f), tx, ty));
        // the left glass about 40 left: in
        Assert.True(MirrorCull.InView(Box(-40f), tx, ty));
        // behind the eye (looking back), above or below the view: out
        Assert.False(MirrorCull.InView(Box(170f), tx, ty));
        Assert.False(MirrorCull.InView(Box(-120f), tx, ty));
        Assert.False(MirrorCull.InView(Box(0f, pitch: 50f), tx, ty));
        Assert.False(MirrorCull.InView(Box(0f, pitch: -50f), tx, ty));
        // a glass across the view's edge is in (part of it shows)
        Assert.True(MirrorCull.InView(Box(61.5f), tx, ty));
        // nothing to test, nothing drawn
        Assert.False(MirrorCull.InView([], tx, ty));
    }

    [Fact]
    public void TheMarginDrawsAGlassJustOutsideTheEdge()
    {
        var (tx, ty) = MirrorCull.HalfTangents(73.74f, true, 3440f / 1440f, 0f);
        var (mx, my) = MirrorCull.HalfTangents(73.74f, true, 3440f / 1440f, 2f);
        Assert.Equal(62.8f, MathF.Atan(mx) * 180f / MathF.PI, 0.1f);
        Assert.True(my > ty);
        Assert.False(MirrorCull.InView(Box(64f), tx, ty));   // its near edge 61.9 degrees out
        Assert.True(MirrorCull.InView(Box(64f), mx, my));
        Assert.False(MirrorCull.InView(Box(66f), mx, my));
    }

    [Fact]
    public void ANarrowerFieldOfViewOrAKeptWidthChangesWhatIsInView()
    {
        // 90 across (the original's) on the same window keeps the width: 45 to a side
        var (tx, ty) = MirrorCull.HalfTangents(90f, keepHeight: false, 3440f / 1440f, 0f);
        Assert.Equal(45f, MathF.Atan(tx) * 180f / MathF.PI, 0.01f);
        Assert.True(MirrorCull.InView(Box(-40f), tx, ty));
        Assert.False(MirrorCull.InView(Box(-55f), tx, ty));
    }

    [Fact]
    public void TheAnglesSayWhereAGlassLies()
    {
        var a = MirrorCull.Angles(Box(73f, pitch: 0f))!.Value;
        Assert.InRange(a.X0, 70f, 73f);
        Assert.InRange(a.X1, 73f, 76f);
        Assert.InRange(a.Y0, -3f, 0f);
        Assert.Null(MirrorCull.Angles([]));
    }
}
