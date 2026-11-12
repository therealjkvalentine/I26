using System.Numerics;
using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's Mirrors row (MirrorsRow over the rear mirror's mode and [port] side_mirrors: Off, Rear, Rear + Left, All), the key's
/// words (SideMirrorRow) and the test that leaves a mirror
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
    public void TheMirrorsRowIsOffRearRearLeftAllAndAllIsTheDefault()
    {
        Assert.Equal(["Off", "Rear", "Rear + Left*", "All*"], MirrorsRow.Names);
        var s = new ShellSettings();
        Assert.Equal(MirrorsRow.All, MirrorsRow.Index(s));
        Assert.Equal(2, s.Graphics[0xd]);   // the original's High
        MirrorsRow.Step(s);
        Assert.Equal(MirrorsRow.Off, MirrorsRow.Index(s));
        Assert.Equal(0, s.Graphics[0xd]);
        MirrorsRow.Step(s);
        Assert.Equal((2, SideMirrorMode.Off), (s.Graphics[0xd], s.SideMirrors));   // Rear: on at High again
        MirrorsRow.Step(s);
        Assert.Equal((2, SideMirrorMode.Left), (s.Graphics[0xd], s.SideMirrors));
        MirrorsRow.Step(s);
        Assert.Equal((2, SideMirrorMode.Auto), (s.Graphics[0xd], s.SideMirrors));
        Assert.Equal(MirrorsRow.All, MirrorsRow.Index(s));
    }

    [Theory]
    [InlineData(0, SideMirrorMode.Auto, MirrorsRow.Off)]   // the rear off draws no mirror at all
    [InlineData(0, SideMirrorMode.Left, MirrorsRow.Off)]
    [InlineData(0, SideMirrorMode.On, MirrorsRow.Off)]
    [InlineData(0, SideMirrorMode.Off, MirrorsRow.Off)]
    [InlineData(1, SideMirrorMode.Off, MirrorsRow.Rear)]   // the original's Low, objects only
    [InlineData(2, SideMirrorMode.Off, MirrorsRow.Rear)]
    [InlineData(2, SideMirrorMode.Left, MirrorsRow.RearLeft)]
    [InlineData(1, SideMirrorMode.Auto, MirrorsRow.All)]
    [InlineData(2, SideMirrorMode.On, MirrorsRow.All)]     // on, set by hand
    public void EveryPairOfKeysShowsTheNearestValue(byte rear, SideMirrorMode side, int shown) => Assert.Equal(shown, MirrorsRow.Index(rear, side));

    [Fact]
    public void AFileTheRowCannotSayIsKeptUntilTheRowIsChanged()
    {
        // the rear off, the side mirrors on: shown as Off, and picking Off again writes nothing
        var s = new ShellSettings { SideMirrors = SideMirrorMode.Left };
        s.Graphics[0xd] = 0;
        MirrorsRow.Put(s, MirrorsRow.Off);
        Assert.Equal((0, SideMirrorMode.Left), (s.Graphics[0xd], s.SideMirrors));
        // the rear's Low and an on set by hand stay under Rear and All
        var low = new ShellSettings { SideMirrors = SideMirrorMode.On };
        low.Graphics[0xd] = 1;
        MirrorsRow.Put(low, MirrorsRow.All);
        Assert.Equal((1, SideMirrorMode.On), (low.Graphics[0xd], low.SideMirrors));
        MirrorsRow.Put(low, MirrorsRow.Rear);
        Assert.Equal((1, SideMirrorMode.Off), (low.Graphics[0xd], low.SideMirrors));
        // Off turns the rear off alone; the side key is kept for the mirror key's back on in play
        var all = new ShellSettings();
        MirrorsRow.Put(all, MirrorsRow.Off);
        Assert.Equal((0, SideMirrorMode.Auto), (all.Graphics[0xd], all.SideMirrors));
        // the file: nothing written for the defaults
        Assert.Null(SideMirrorRow.Word(new ShellSettings().SideMirrors));
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
    public void TheRowIsTheOriginalsSeventhAndCopiesBothKeys()
    {
        var sheet = GraphicsPages.Rows[GraphicsPages.Sheet];
        Assert.Equal(GraphicsPages.OriginalRows - 1, Array.IndexOf(sheet, GraphicsRow.Mirrors));
        var to = new ShellSettings();
        var from = new ShellSettings { SideMirrors = SideMirrorMode.Left };
        from.Graphics[0xd] = 1;
        GraphicsPages.Copy(GraphicsRow.Mirrors, from, to);
        Assert.Equal((1, SideMirrorMode.Left), (to.Graphics[0xd], to.SideMirrors));
        Assert.Contains("Rear + Left", GraphicsHelp.Text(GraphicsRow.Mirrors, false));
        Assert.Contains("1997 look", GraphicsHelp.Text(GraphicsRow.Mirrors, true));
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
