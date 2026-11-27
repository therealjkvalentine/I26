using Godot;
using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The door mirrors' turned glass ([port] side_mirror_face=turned, the default): the turned head's glass and rounded ring
/// (side_mirror_head=turned's) on the housing where the art places it, brought in front of that housing by scaling it about the
/// driver's eye (DoorFace.TurnedShare), which leaves it exactly where it was on screen. Without the engine.
/// </summary>
public class MirrorTurnedFaceTests
{
    [Fact]
    public void TurnedIsTheDefaultAndTheKeyRoundTrips()
    {
        var s = new ShellSettings();
        Assert.True(s.SideMirrorFaceTurned);
        Assert.False(s.SideMirrorFaceProud);
        Assert.False(s.SideMirrorHeadTurned);   // the housing stays where the art places it
        Assert.False(s.Write(null).HasSectionKey("port", "side_mirror_face"));
        Assert.True(DoorFace.Turned(null) && DoorFace.Turned("") && DoorFace.Turned(" Turned "));
        Assert.False(DoorFace.Turned("proud") || DoorFace.Turned("inset"));
        Assert.True(DoorFace.Proud("proud") && !DoorFace.Proud(null) && !DoorFace.Proud("inset"));
        foreach (var (word, turned, proud) in new[] { ("proud", false, true), ("inset", false, false), ("turned", true, false) })
        {
            var f = new SettingsFile();
            f.SetValue("port", "side_mirror_face", word);
            var r = ShellSettings.Read(f);
            Assert.Equal((turned, proud), (r.SideMirrorFaceTurned, r.SideMirrorFaceProud));
            var back = ShellSettings.Read(r.Write(null));
            Assert.Equal((turned, proud), (back.SideMirrorFaceTurned, back.SideMirrorFaceProud));
        }
    }

    [Fact]
    public void TheDevPanelOffersTheThreeFaces()
    {
        var row = Assert.Single(I76.Game.Ui.DevSettings.All, e => e.Key == "side_mirror_face");
        var s = new ShellSettings();
        Assert.Equal(0, row.Get(s));
        row.Set(s, 1); Assert.True(s.SideMirrorFaceProud && !s.SideMirrorFaceTurned);
        row.Set(s, 2); Assert.False(s.SideMirrorFaceProud || s.SideMirrorFaceTurned);
        row.Set(s, 0); Assert.True(s.SideMirrorFaceTurned && !s.SideMirrorFaceProud);
    }

    private static List<(Vector3 A, Vector3 B, Vector3 C)> Quad(Vector3 o, Vector3 a, Vector3 b) =>
        [(o, o + a, o + a + b), (o, o + a + b, o + b)];

    [Fact]
    public void TheGlassComesInFrontOfTheHousingWithoutMovingOnScreen()
    {
        var eye = Vector3.Zero;
        // the housing's end: a square 1 m ahead; the turned glass a square through it, turned 30 degrees, half of it behind the end
        var end = Quad(new Vector3(-0.05f, -0.05f, -1f), new Vector3(0.1f, 0, 0), new Vector3(0, 0.1f, 0));
        var basis = new Basis(Vector3.Up, 30f * MathF.PI / 180f);
        var mid = new Vector3(0, 0, -1f);
        var glass = Quad(mid + basis * new Vector3(-0.06f, -0.06f, 0), basis * new Vector3(0.12f, 0, 0), basis * new Vector3(0, 0.12f, 0));
        const float clear = 0.003f;
        float share = DoorFace.TurnedShare(eye, glass, end, clear);
        Assert.InRange(share, 0.9f, 0.999f);
        // scaled about the eye, every point of the glass stands at least the clearance in front of the end along the eye's ray
        foreach (var (a, b, c) in glass)
            foreach (var x in new[] { a, b, c, (a + b + c) / 3 })
            {
                var y = eye + (x - eye) * share;
                var d = y.Normalized();
                foreach (var (ta, tb, tc) in end)
                    if (DoorFace.Hit(eye, d, ta, tb, tc) is { } t) Assert.True(y.Length() <= t - clear + 1e-4f, $"{y} behind the end at {t}");
                // the same direction from the eye: the same place on screen
                Assert.True(d.Dot(x.Normalized()) > 0.99999f);
            }
        // a glass already wholly in front is left where it is
        var near = Quad(new Vector3(-0.06f, -0.06f, -0.9f), new Vector3(0.12f, 0, 0), new Vector3(0, 0.12f, 0));
        Assert.Equal(1f, DoorFace.TurnedShare(eye, near, end, clear));
    }
}
