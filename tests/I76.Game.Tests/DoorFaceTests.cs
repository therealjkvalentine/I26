using Godot;
using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The door mirrors' proud face ([port] side_mirror_face, DoorFace): a rounded rectangle as tall as the housing's end across its level
/// edge, its top and bottom on the end's, wider than the end so every corner of it lies inside the rounded sides, standing in front of
/// the housing. Shapes in the plane's metres, laid out as the Piranha's ends are seen (the left a hexagon with its top 15 degrees off
/// level; the right seen nearly edge-on, a steep lozenge).
/// </summary>
public class DoorFaceTests
{
    private static Vector2 Turn(Vector2 p, float degrees) => p.Rotated(degrees * MathF.PI / 180f);

    /// <summary>A hexagon 15 cm across its points and 10.6 cm tall between its flat top and bottom, turned 15 degrees, counter-clockwise.</summary>
    private static List<Vector2> Hexagon() =>
        [.. new Vector2[] { new(-0.075f, 0), new(-0.035f, -0.053f), new(0.035f, -0.053f), new(0.075f, 0), new(0.035f, 0.053f), new(-0.035f, 0.053f) }.Select(p => Turn(p, 15))];

    [Fact]
    public void TheFaceIsProudByDefaultAndTheKeysRead()
    {
        var s = new ShellSettings();
        Assert.True(s.SideMirrorFaceProud);
        Assert.Equal(DoorFace.DefaultOverLeft, s.SideMirrorFaceOverLeft);
        Assert.Equal(DoorFace.DefaultOverRight, s.SideMirrorFaceOverRight);
        Assert.Equal(30, MirrorAim.DefaultTiltRight);   // the right face turned toward the driver
        Assert.True(DoorFace.Proud(null));
        Assert.True(DoorFace.Proud(""));
        Assert.True(DoorFace.Proud("proud"));
        Assert.False(DoorFace.Proud("inset"));
        Assert.False(DoorFace.Proud(" Inset "));
        Assert.Equal((6, 15), DoorFace.OverOverride("6,15"));
        Assert.Equal((0, DoorFace.MaxOver), DoorFace.OverOverride("-4,200"));
        Assert.Null(DoorFace.OverOverride("6"));
        Assert.Null(DoorFace.OverOverride(null));
    }

    [Fact]
    public void TheFaceIsAsTallAsTheEndAlongItsLevelEdgeAndCoversEveryCorner()
    {
        var hex = Hexagon();
        var f = DoorFace.Fit(hex, new Vector2(0, 1), 30, 0);
        // its top along the hexagon's top edge (15 degrees off level, within LevelDegrees), the height the hexagon's between its flats
        Assert.Equal(15f, MathF.Acos(MathF.Abs(f.X.Dot(new Vector2(1, 0)))) * 180f / MathF.PI, 0.1f);
        Assert.Equal(0.106f, f.Height, 0.0005f);
        foreach (var p in hex)
        {
            var q = f.ToFace(p);
            Assert.InRange(q.Y, -0.5f - 1e-4f, 0.5f + 1e-4f);
            Assert.True(f.Inside(q), $"{p} outside the face at {q}");
            Assert.Equal(p.X, f.ToPlane(q).X, 1e-5f);
        }
        // the top and bottom edges' corners on the face's top and bottom, and the pointed ends inside its rounded sides, not on them
        Assert.Equal(4, hex.Count(p => MathF.Abs(MathF.Abs(f.ToFace(p).Y) - 0.5f) < 1e-3f));
        Assert.All(hex, p => Assert.True(MathF.Abs(f.ToFace(p).X) < f.Width / 2 - DoorFace.Margin * 0.5f));
        Assert.True(f.Width > 0.15f / 0.106f, $"{f.Width} heights: narrower than the end's points");
    }

    [Fact]
    public void TheReachWidensBothSidesAndTheCornersFollowTheShare()
    {
        var hex = Hexagon();
        var a = DoorFace.Fit(hex, new Vector2(0, 1), 30, 0);
        var b = DoorFace.Fit(hex, new Vector2(0, 1), 30, 15);
        Assert.True(b.Width >= a.Width + 0.3f - 1e-4f, $"{a.Width} to {b.Width}");   // each side out by 15 % of the height, the pointed ends further
        Assert.Equal(a.Height, b.Height, 1e-6f);
        Assert.Equal(0.3f, a.R, 1e-6f);
        Assert.Equal(DoorFace.MaxRound, DoorFace.Fit(hex, new Vector2(0, 1), 80, 0).R, 1e-6f);   // at most just under round ends
        // rounder corners need a wider face to keep the end's corners inside
        Assert.True(DoorFace.Fit(hex, new Vector2(0, 1), 50, 0).Width > a.Width);
    }

    [Fact]
    public void AnEndSeenEdgeOnGetsAnUprightFace()
    {
        // the Piranha's right end as projected on its turned face: a lozenge whose edges all run steeply
        List<Vector2> lozenge = [new(0, 0), new(0.025f, 0.03f), new(0.04f, 0.09f), new(0.03f, 0.115f), new(0.005f, 0.085f), new(-0.01f, 0.025f)];
        var f = DoorFace.Fit(lozenge, new Vector2(0, 1), 30, 15);
        Assert.Equal(1f, MathF.Abs(f.X.Dot(new Vector2(1, 0))), 1e-5f);   // level
        Assert.True(f.Y.Dot(new Vector2(0, 1)) > 0.999f);
        Assert.Equal(0.115f, f.Height, 1e-4f);   // its top and bottom at the end's highest and lowest points
        Assert.All(lozenge, p => Assert.True(f.Inside(f.ToFace(p))));
        Assert.True(f.Width >= 0.05f / 0.115f + 0.3f - 1e-4f, $"{f.Width} heights");   // the end's width and the reach each side
    }

    [Fact]
    public void TheFaceStandsInFrontOfTheHousingAlongTheEyesRays()
    {
        var eye = new Vector3(0, 0, 0);
        // a housing wall across the view 1 m ahead
        List<(Vector3, Vector3, Vector3)> wall = [(new(-1, -1, -1), new(1, -1, -1), new(0, 1, -1))];
        Assert.Equal(1f, DoorFace.Hit(eye, new Vector3(0, 0, -1), new(-1, -1, -1), new(1, -1, -1), new(0, 1, -1))!.Value, 1e-5f);
        Assert.Null(DoorFace.Hit(eye, new Vector3(0, 0, 1), new(-1, -1, -1), new(1, -1, -1), new(0, 1, -1)));
        // a face point 2 cm behind the wall must come 2 cm plus the clearance nearer; one 5 cm in front is 5 cm less the clearance clear
        Assert.Equal(0.023f, DoorFace.Short(eye, new Vector3(0, 0, -1.02f), wall, 0.003f), 1e-4f);
        Assert.Equal(-0.047f, DoorFace.Short(eye, new Vector3(0, 0, -0.95f), wall, 0.003f), 1e-4f);
        Assert.Equal(float.MinValue, DoorFace.Short(eye, new Vector3(5, 0, -1f), wall, 0.003f));   // past the wall's side
    }
}
