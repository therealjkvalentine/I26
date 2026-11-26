using System.Numerics;
using I76.Formats.Assembly;
using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The door mirrors' glass tilted in its housing ([port] side_mirror_tilt_right / _left, MirrorAim.Tilt): the head stays where the art
/// places it, the glass alone turns toward the driver about its outer corner, back into the housing, and the picture keeps its aim.
/// A glass laid out as the Piranha's right one: square to the car, ahead and far to the right of the eye, seen nearly edge-on.
/// </summary>
public class MirrorTiltTests
{
    private static readonly Vector3 Eye = new(-0.41f, 1.25f, 0.03f);
    // a 13 x 12 cm pane facing back (its normal -z), 1.3 m right of the eye and 40 cm ahead, turned 4 degrees outward
    private static readonly MirrorGlass.Pane Pane = Glass();

    private static MirrorGlass.Pane Glass()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -4f * MathF.PI / 180f);
        var mid = new Vector3(0.89f, 1.12f, 0.42f);
        Vector3[] square = [new(-0.065f, -0.06f, 0), new(0.065f, -0.06f, 0), new(0.065f, 0.06f, 0), new(-0.065f, 0.06f, 0)];
        return new MirrorGlass.Pane(false, "XX51MIRR", [[.. square.Select(p => mid + Vector3.Transform(p, turn))]], Vector3.Transform(-Vector3.UnitZ, turn));
    }

    private static Vector3 Mid(IEnumerable<Vector3[]> faces) { var all = faces.SelectMany(f => f).ToArray(); return all.Aggregate(Vector3.Zero, (a, b) => a + b) / all.Length; }

    /// <summary>The whole head's turn to the driver (side_mirror_head=turned), as Cockpit.Aim makes it: the picture 16.4 degrees out.</summary>
    private static (Quaternion Full, Vector3 Axis) Full()
    {
        var axis = MirrorAim.Axis(false, 16.4f, 0);
        var mid = Mid(Pane.Faces);
        return (MirrorAim.Turn(Eye, mid, Pane.Normal, mid, axis), axis);
    }

    [Fact]
    public void TheRightGlassTiltsByDefaultTheLeftDoesNotAndTheHeadStays()
    {
        var s = new ShellSettings();
        Assert.Equal(15, s.SideMirrorTiltRight);
        Assert.Equal(0, s.SideMirrorTiltLeft);
        Assert.False(s.SideMirrorHeadTurned);   // the housing where the art places it
        Assert.Equal(15, MirrorAim.GlassTilt(s.SideMirrorHeadTurned, s.SideMirrorTiltRight));
        Assert.Equal(0, MirrorAim.GlassTilt(s.SideMirrorHeadTurned, s.SideMirrorTiltLeft));
        // with the head turned the glass is not tilted on top; out of range is clamped
        Assert.Equal(0, MirrorAim.GlassTilt(true, 15));
        Assert.Equal(MirrorAim.MaxTilt, MirrorAim.GlassTilt(false, 90));
        Assert.Equal(0, MirrorAim.GlassTilt(false, -5));
        Assert.Equal((10, 37), MirrorAim.TiltOverride("10,37"));
        Assert.Equal((0, 45), MirrorAim.TiltOverride("-3,80"));
        Assert.Null(MirrorAim.TiltOverride(null));
        Assert.Null(MirrorAim.TiltOverride("15"));
    }

    [Fact]
    public void TheTiltTurnsAboutTheHeadsTurnAxisAndStopsAtIt()
    {
        var (full, _) = Full();
        float whole = MirrorAim.AngleDegrees(full);
        Assert.InRange(whole, 30f, 45f);   // the Piranha's right head turns 37
        var t = MirrorAim.Tilt(full, 15);
        Assert.Equal(15f, MirrorAim.AngleDegrees(t), 0.05f);
        var a = Vector3.Normalize(new Vector3(full.X, full.Y, full.Z)) * MathF.Sign(full.W);
        var b = Vector3.Normalize(new Vector3(t.X, t.Y, t.Z)) * MathF.Sign(t.W);
        Assert.True(Vector3.Dot(a, b) > 0.9999f);
        Assert.Equal(whole, MirrorAim.AngleDegrees(MirrorAim.Tilt(full, 80)), 0.05f);   // never past the head's own turn
        Assert.Equal(Quaternion.Identity, MirrorAim.Tilt(full, 0));
    }

    [Fact]
    public void TheTiltedGlassFacesTheDriverMoreAndShowsMore()
    {
        var (full, axis) = Full();
        var mid = Mid(Pane.Faces);
        float art = MirrorAim.OffFace(Eye, mid, Pane.Normal);
        Assert.InRange(art, 70f, 80f);   // a sliver
        float before = art, seen0 = MirrorGlass.Picture(Pane, Eye).Aspect;
        foreach (int deg in new[] { 5, 10, 15, 20, 30 })
        {
            var q = MirrorAim.Tilt(full, deg);
            var faces = MirrorAim.Tilted(Pane.Faces, q, MirrorAim.TiltPivot(Pane.Faces[0], q, Eye));
            float off = MirrorAim.OffFace(Eye, Mid(faces), Vector3.Transform(Pane.Normal, q));
            Assert.True(off < before - 3f, $"{deg}: {off} not under {before}");
            before = off;
        }
        // at 15 the glass is seen more than twice as wide across the line of sight (its height about the same)
        var q15 = MirrorAim.Tilt(full, 15);
        var f15 = MirrorAim.Tilted(Pane.Faces, q15, MirrorAim.TiltPivot(Pane.Faces[0], q15, Eye));
        float seen15 = MirrorGlass.Picture(Pane with { Faces = f15 }, Eye).Aspect;
        Assert.True(seen15 > 2f * seen0, $"{seen15} against {seen0}, {art} off its face");
        // turned all the way the glass reflects the eye's ray into the picture's axis: the picture's aim is the mirror's own there
        var all = MirrorAim.Tilted(Pane.Faces, full, MirrorAim.TiltPivot(Pane.Faces[0], full, Eye));
        var r = MirrorAim.Reflect(Eye, Mid(all), Vector3.Normalize(Vector3.Transform(Pane.Normal, full)));
        Assert.True(Vector3.Dot(Vector3.Normalize(r), axis) > MathF.Cos(2f * MathF.PI / 180f));
    }

    [Fact]
    public void TheGlassTurnsAboutItsOuterCornerBackIntoTheHousing()
    {
        var (full, _) = Full();
        var q = MirrorAim.Tilt(full, 15);
        var pivot = MirrorAim.TiltPivot(Pane.Faces[0], q, Eye);
        Assert.Contains(pivot, Pane.Faces[0]);   // a corner of the pane, which stays put
        // the outboard one: the glass's end of the housing as the driver sees it
        Assert.Equal(Pane.Faces[0].Max(p => p.X), pivot.X, 0.02f);
        var tilted = MirrorAim.Tilted(Pane.Faces, q, pivot)[0];
        var toEye = Vector3.Normalize(Eye - Mid(Pane.Faces));
        for (int k = 0; k < tilted.Length; k++)
            Assert.True(Vector3.Dot(tilted[k] - Pane.Faces[0][k], toEye) <= 1e-5f, $"corner {k} came out toward the eye");
        Assert.Contains(tilted, p => Vector3.Distance(p, pivot) < 1e-6f);
    }
}
