using Godot;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The side mirrors' rims (MirrorRim) and the own cameras' placement (OwnCamera).</summary>
public class MirrorRimTests
{
    private static Vector3[] Quad(float x0, float y0, float x1, float y1) => [new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0)];

    [Fact]
    public void TheOutlineLeavesOutTheEdgesTwoFacesShare()
    {
        // a pane of two triangles across a diagonal, and one of two quads side by side
        var tri = new[] { new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(1, 1, 0) }, new Vector3[] { new(0, 0, 0), new(1, 1, 0), new(0, 1, 0) } };
        var loops = MirrorRim.Outline(tri);
        Assert.Single(loops);
        Assert.Equal(4, loops[0].Count);
        var pair = new[] { Quad(0, 0, 1, 1), Quad(1, 0, 2, 1) };
        loops = MirrorRim.Outline(pair);
        Assert.Single(loops);
        Assert.Equal(6, loops[0].Count);
        Assert.DoesNotContain(loops[0], p => p.X == 1 && p.Y > 0 && p.Y < 1);
    }

    [Fact]
    public void TheRingSitsInsideTheHousingsEdgeByTheWidth()
    {
        var loop = Quad(-0.5f, -0.5f, 0.5f, 0.5f);
        var (v, uv) = MirrorRim.Ring(loop, 0.1f);
        Assert.Equal(4 * 12, v.Length);
        Assert.Equal(v.Length, uv.Length);
        // the lip's corners a tenth in on both axes, the outer ones the housing's edge
        Assert.Equal(0.4f, v.Where((_, i) => uv[i].Y == 1).Max(p => p.X), 0.001f);
        Assert.Equal(-0.4f, v.Where((_, i) => uv[i].Y == 1).Min(p => p.Y), 0.001f);
        Assert.Equal(0.5f, v.Where((_, i) => uv[i].Y == 0).Max(p => p.X), 0.001f);
        Assert.All(v, p => Assert.Equal(0f, p.Z));
    }

    [Fact]
    public void TheWidthIsTwoThirdsOfTheRearFramesShareOfTheHeight()
    {
        Assert.Equal(6f / 64 * 2 / 3, MirrorRim.RimShare, 0.0001f);
        Assert.Equal(0.06f * MirrorRim.RimShare, MirrorRim.Width(Quad(0, 1.0f, 0.1f, 1.06f)), 0.0001f);
    }

    [Fact]
    public void TheFrameColourIsTheArtsTopRowsLessTheSurroundAndTheGlass()
    {
        // a 8x8 art: the surround's colour at the corner, a frame of (40, 44, 48) on row 0 from x 1..6, the rest keyed
        int w = 8, h = 8; var a = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { a[i * 4] = 200; a[i * 4 + 1] = 190; a[i * 4 + 2] = 180; a[i * 4 + 3] = 255; }
        for (int x = 1; x < 7; x++) { int i = x * 4; a[i] = 40; a[i + 1] = 44; a[i + 2] = 48; }
        for (int y = 1; y < h; y++) for (int x = 0; x < w; x++) a[(y * w + x) * 4 + 3] = 0;
        Assert.Equal(((byte)40, (byte)44, (byte)48), MirrorRim.FrameColour(a, w, h));
    }

    [Fact]
    public void AnOwnCameraLooksBackAndOutward()
    {
        var l = OwnCamera.Forward(left: true);
        var r = OwnCamera.Forward(left: false);
        Assert.True(l.Z > 0.9f && r.Z > 0.9f);   // back along the car
        Assert.True(l.X < 0 && r.X > 0);         // turned to its own side
        Assert.Equal(-r.X, l.X, 0.0001f);
        Assert.True(l.Y <= 0 && l.Y > -0.2f);    // level or a little down
        Assert.Equal(1f, l.Length(), 0.0001f);
        // a real door mirror: 15 to 20 degrees out, 30 to 40 across, the inner edge inboard of straight back so the flank shows
        Assert.InRange(OwnCamera.YawDegrees, 15f, 20f);
        Assert.InRange(OwnCamera.FovDegrees, 30f, 40f);
        Assert.True(OwnCamera.InboardDegrees > 0);
    }

    [Fact]
    public void TheSideMirrorsAreDrawnByTheirOwnCamerasUnlessSharedIsSet()
    {
        var s = new I76.Game.Shell.ShellSettings();
        Assert.Equal(SideMirrorRender.Own, s.SideMirrorRender);
        var row = Assert.Single(I76.Game.Ui.DevSettings.All, e => e.Key == "side_mirror_render");
        Assert.Equal(2, row.Values.Length);
        Assert.StartsWith("own", row.Values[row.Get(s)]);
        row.Set(s, 0);
        Assert.Equal(SideMirrorRender.Shared, s.SideMirrorRender);
        Assert.Equal("shared", row.Values[row.Get(s)]);
    }

    [Fact]
    public void TheAimHookGivesAllFourOrNone()
    {
        var own = (OwnCamera.FovDegrees, OwnCamera.YawDegrees, OwnCamera.OutboardMetres, OwnCamera.PitchDegrees);
        Assert.Equal(own, OwnCamera.Aim(null));
        Assert.Equal(own, OwnCamera.Aim("35,18"));
        Assert.Equal((35f, 18f, 0.3f, 5f), OwnCamera.Aim("35,18,0.3,5"));
    }
}
