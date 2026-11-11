using System.Numerics;
using I76.Game.Sim;
using Xunit;
using Xunit.Abstractions;

namespace I76.Game.Tests;

/// <summary>The side mirrors' aim from the driver's eye (MirrorAim), on the Piranha's geometry: its eye, its door mirrors' glass and its body's sides.</summary>
public class MirrorAimTests(ITestOutputHelper output)
{
    // the Piranha's cockpit in game axes (x right, y up, z forward): the eye, each glass's middle and the art's normal (facing the eye),
    // the body's half width and rear end (each glass turns about its own middle)
    private static readonly Vector3 Eye = new(-0.408f, 1.251f, 0.033f);
    private static readonly Vector3 GlassL = new(-0.882f, 1.117f, 0.424f), GlassR = new(0.886f, 1.116f, 0.422f);
    private static readonly Vector3 ArtL = Vector3.Normalize(new(-0.07f, -0.25f, -0.97f)), ArtR = Vector3.Normalize(new(0.07f, -0.25f, -0.97f));
    private const float HalfWidth = 0.97f, Rear = -2.35f;

    private static float Deg(float r) => r * 180f / MathF.PI;
    private static float Incidence(Vector3 centre, Vector3 normal) => Deg(MathF.Acos(MathF.Abs(Vector3.Dot(Vector3.Normalize(Eye - centre), normal))));

    private static (float Yaw, Vector3 Axis, Quaternion Turn) Solve(bool left, float inward = 0)
    {
        var glass = left ? GlassL : GlassR;
        var cam = glass + new Vector3(left ? -OwnCamera.OutboardMetres : OwnCamera.OutboardMetres, 0, 0);
        float yaw = MirrorAim.YawDegrees(left, cam, HalfWidth, Rear, OwnCamera.FovDegrees, 2 * inward);
        var axis = MirrorAim.Axis(left, yaw, OwnCamera.PitchDegrees);
        return (yaw, axis, MirrorAim.Turn(Eye, glass, left ? ArtL : ArtR, glass, axis));
    }

    [Fact]
    public void TheArtsGlassesFaceTheCarsBackAndTheRightOneIsSeenAlmostEdgeOn()
    {
        // the original's glasses as painted: the right one 79 degrees off its face from the seat, and reflecting the view out to the side
        Assert.InRange(Incidence(GlassL, ArtL), 58f, 62f);
        Assert.InRange(Incidence(GlassR, ArtR), 77f, 81f);
        var r = MirrorAim.Reflect(Eye, GlassR, ArtR);
        Assert.InRange(Deg(MathF.Atan2(r.X, -r.Z)), 80f, 90f);
    }

    [Fact]
    public void ThePictureLooksBackAlongTheSideWithASliverOfTheFlank()
    {
        var (yl, al, _) = Solve(left: true);
        var (yr, ar, _) = Solve(left: false);
        output.WriteLine($"yaw out: left {yl:0.00}, right {yr:0.00}");
        // the inner edge an eighth of the field inside the flank's line: 15 to 18 degrees out for a 40-degree picture
        Assert.InRange(yl, 15f, 18f);
        Assert.InRange(yr, 15f, 18f);
        Assert.Equal(yl, yr, 0.5f);
        Assert.True(al.Z < -0.9f && ar.Z < -0.9f);   // back
        Assert.True(al.X < 0 && ar.X > 0);           // each to its own side
        // more inward: the picture turns twice the glass's extra turn
        Assert.Equal(yr - 6f, Solve(left: false, inward: 3f).Yaw, 0.01f);
    }

    [Fact]
    public void TheTurnedGlassReflectsTheEyesRayIntoThePicture()
    {
        foreach (bool left in new[] { true, false })
        {
            var (_, axis, q) = Solve(left);
            var glass = left ? GlassL : GlassR;
            var pivot = glass;
            var centre = pivot + Vector3.Transform(glass - pivot, q);
            var normal = Vector3.Transform(left ? ArtL : ArtR, q);
            var r = MirrorAim.Reflect(Eye, centre, normal);
            Assert.True(Vector3.Dot(r, axis) > 0.9995f, $"{(left ? "left" : "right")}: reflects {r}, wants {axis}");
            float turned = Deg(2 * MathF.Acos(Math.Clamp(q.W, -1f, 1f)));
            float seen = Incidence(centre, normal);
            output.WriteLine($"{(left ? "left" : "right")}: turned {turned:0.0} degrees, seen {seen:0.0} off its face (art {Incidence(glass, left ? ArtL : ArtR):0.0})");
        }
        // the right glass, twice as far from the eye and further to the side, turns further, and is then seen far more face-on
        float tl = Deg(2 * MathF.Acos(Solve(true).Turn.W)), tr = Deg(2 * MathF.Acos(Solve(false).Turn.W));
        Assert.True(tr > tl + 5f);
        var (_, _, qr) = Solve(false);
        Assert.True(Incidence(GlassR, Vector3.Transform(ArtR, qr)) < 55f);
    }

    [Fact]
    public void TheCcCarsLeftGlassTurnsByTheSameRule()
    {
        // the CC cars' one door mirror (left): square to the car, the body 0.91 a side and ending 2.37 back
        var glass = new Vector3(-0.830f, 1.111f, 0.431f);
        var art = new Vector3(0, 0, -1);
        var cam = glass + new Vector3(-OwnCamera.OutboardMetres, 0, 0);
        float yaw = MirrorAim.YawDegrees(true, cam, 0.91f, -2.37f, OwnCamera.FovDegrees);
        var axis = MirrorAim.Axis(true, yaw, OwnCamera.PitchDegrees);
        var q = MirrorAim.Turn(Eye, glass, art, glass, axis);
        var centre = glass;
        var normal = Vector3.Transform(art, q);
        output.WriteLine($"CC left: yaw out {yaw:0.00}, turned {Deg(2 * MathF.Acos(q.W)):0.0}, seen {Incidence(centre, normal):0.0} off its face (art {Incidence(glass, art):0.0})");
        Assert.InRange(yaw, 15f, 18f);
        Assert.True(Vector3.Dot(MirrorAim.Reflect(Eye, centre, normal), axis) > 0.9995f);
    }

    [Fact]
    public void AGameAxesTurnIsTheSameTurnInGodotsAxes()
    {
        var q = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0.1f));
        var g = MirrorAim.ToGodot(q);
        var v = new Vector3(0.3f, -0.5f, 0.8f);
        var game = Vector3.Transform(v, q);
        var godot = new Godot.Basis(g) * new Godot.Vector3(v.X, v.Y, -v.Z);
        Assert.Equal(game.X, godot.X, 0.0001f);
        Assert.Equal(game.Y, godot.Y, 0.0001f);
        Assert.Equal(-game.Z, godot.Z, 0.0001f);
    }

    [Fact]
    public void TheAimHookIsArtOrTwoExtraTurns()
    {
        Assert.Equal((false, 0f, 0f), MirrorAim.Hook(null));
        Assert.Equal((true, 0f, 0f), MirrorAim.Hook("art"));
        Assert.Equal((false, 0f, 3f), MirrorAim.Hook("0,3"));
        Assert.Equal((false, 0f, 0f), MirrorAim.Hook("3"));
    }
}
