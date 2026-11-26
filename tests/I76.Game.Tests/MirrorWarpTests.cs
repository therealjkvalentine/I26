using Godot;
using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Mirror Rate's Smooth values (MirrorWarp): the draw schedule, the overscan and the re-projection's sums; Mirror Haze's key. Without the engine.</summary>
public class MirrorWarpTests
{
    private static readonly (float X, float Y) Rear = (MathF.Tan(22.5f * MathF.PI / 180f), MathF.Tan(22.5f * MathF.PI / 180f) / 4f);

    [Fact]
    public void TheSmoothValuesHaveTheirPeriodsAndTheOthersNone()
    {
        Assert.Equal([0, 0, 0, 2, 3, 4], Enum.GetValues<MirrorRate>().Select(MirrorWarp.Period));
        foreach (var r in Enum.GetValues<MirrorRate>()) Assert.Equal(r, MirrorRateRow.Parse(MirrorRateRow.Word(r)));
        Assert.Equal(["Alternate*", "Half", "Full*", "Smooth 1/2*", "Smooth 1/3*", "Smooth 1/4*"], MirrorRateRow.Names);
        Assert.Equal(MirrorRate.Alternate, new ShellSettings().MirrorRate);
    }

    /// <summary>Every mirror draws once a period; at 1/3 and 1/4 never two on a frame; at 1/2 the rear alone, then the two sides.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EachMirrorDrawsOnceAPeriodInTurn(int n)
    {
        for (long start = 0; start < 12; start++)
        {
            var drawn = new int[3];
            for (long f = start; f < start + n; f++)
            {
                int on = 0;
                for (int m = 0; m < 3; m++) if (MirrorWarp.Draws(n, f, m)) { drawn[m]++; on++; }
                Assert.True(on <= (n == 2 ? 2 : 1), $"{on} mirrors on frame {f} at 1/{n}");
                if (n == 2) Assert.False(MirrorWarp.Draws(n, f, 0) && MirrorWarp.Draws(n, f, 1));
            }
            Assert.Equal([1, 1, 1], drawn);
        }
    }

    [Fact]
    public void TheOverscanGrowsWithThePeriodAndIsNoneOutsideSmooth()
    {
        Assert.Equal((1f, 1f), MirrorWarp.Overscan(0));
        Assert.Equal((1024, 256), MirrorWarp.Size(1024, 256, MirrorWarp.Overscan(0)));
        var last = 1f;
        foreach (int n in new[] { 2, 3, 4 })
        {
            var o = MirrorWarp.Overscan(n);
            Assert.True(o.X > last && o.Y > 1f && o.Y < o.X);
            last = o.X;
        }
        Assert.Equal((1178, 275), MirrorWarp.Size(1024, 256, MirrorWarp.Overscan(4)));
    }

    /// <summary>No turn and no overscan: every point where it is; with the overscan, the glass's field inside the render's.</summary>
    [Fact]
    public void WithoutATurnTheMapIsTheOverscansScaleAboutTheMiddle()
    {
        var h = MirrorWarp.Homography(Basis.Identity, Basis.Identity, Rear, Rear);
        foreach (var uv in new[] { new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.3f, 0.8f) }) Near(uv, MirrorWarp.Apply(h, uv));
        var drawn = (Rear.X * 1.15f, Rear.Y * 1.075f);
        var o = MirrorWarp.Homography(Basis.Identity, Basis.Identity, Rear, drawn);
        Near(new Vector2(0.5f, 0.5f), MirrorWarp.Apply(o, new Vector2(0.5f, 0.5f)));
        Near(new Vector2(0.5f + 0.5f / 1.15f, 0.5f - 0.5f / 1.075f), MirrorWarp.Apply(o, new Vector2(1, 0)));
    }

    /// <summary>The homography is the far scene's re-projection: a point a long way off seen now lands where the camera then saw it.</summary>
    [Theory]
    [InlineData(4f, 0f, 0f)]
    [InlineData(-6f, 1.5f, 0f)]
    [InlineData(2f, -1f, 3f)]
    public void ATurnMovesThePictureAsTheFarSceneMoves(float yaw, float pitch, float roll)
    {
        var then = new Transform3D(Basis.FromEuler(new Vector3(0.1f, 0.7f, 0)), new Vector3(10, 2, -30));
        var now = new Transform3D(then.Basis * Basis.FromEuler(new Vector3(Deg(pitch), Deg(yaw), Deg(roll))), then.Origin);
        var drawn = (Rear.X * 1.15f, Rear.Y * 1.075f);
        var h = MirrorWarp.Homography(then.Basis, now.Basis, Rear, drawn);
        foreach (var uv in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.7f) })
            Near(MirrorWarp.Project(then, now, Rear, drawn, uv, 1e5f), MirrorWarp.Apply(h, uv), 1e-4f);
    }

    /// <summary>The camera turned to its left since the draw: what is in the glass's middle now was right of the middle then.</summary>
    [Fact]
    public void ATurnToTheLeftLooksRightOfTheOldMiddle()
    {
        var then = Transform3D.Identity;
        var now = new Transform3D(new Basis(Vector3.Up, Deg(3)), Vector3.Zero);   // Godot: positive about up turns the view (-z) to the left
        var p = MirrorWarp.Apply(MirrorWarp.Homography(then.Basis, now.Basis, Rear, Rear), new Vector2(0.5f, 0.5f));
        Assert.True(p.X < 0.5f, $"{p}");   // to the left of the old middle in the camera's own picture
        Assert.Equal(0.5f, p.Y, 4);
    }

    /// <summary>The shader's depth steps find the moved camera's view of a flat wall behind the car, as the exact sum does.</summary>
    [Fact]
    public void TheDepthStepsFindAMovedCamerasViewOfAWall()
    {
        var then = Transform3D.Identity;
        var now = new Transform3D(new Basis(Vector3.Up, Deg(1.5f)), new Vector3(0.2f, 0, 1.2f));   // 1.2 m away from the wall, turned
        var drawn = (Rear.X * 1.1f, Rear.Y * 1.05f);
        const float wall = 15f;   // a plane 15 m ahead of the camera as it was: its depth then is 15 everywhere
        var h = MirrorWarp.Homography(then.Basis, now.Basis, Rear, drawn);
        var turn = MirrorWarp.Turn(then.Basis, now.Basis);
        var shift = MirrorWarp.Shift(then, now.Origin);
        foreach (var uv in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.15f, 0.3f), new Vector2(0.85f, 0.75f) })
        {
            // the distance along the ray now that reaches the wall
            var d = now.Basis * new Vector3((2 * uv.X - 1) * Rear.X, (1 - 2 * uv.Y) * Rear.Y, -1);
            float t = (now.Origin.Z + wall) / -d.Z;
            var want = MirrorWarp.Project(then, now, Rear, drawn, uv, t);
            var got = MirrorWarp.Iterate(h, turn, shift, Rear, drawn, uv, _ => wall);
            Near(want, got, 1e-4f);
            Assert.True((MirrorWarp.Apply(h, uv) - want).Length() > 1e-3f, "the move shows: the turn alone is off");
        }
    }

    [Fact]
    public void MirrorHazeIsSlightByDefaultAndReadsBack()
    {
        Assert.Equal(MirrorHazeRow.Default, new ShellSettings().MirrorHaze);
        Assert.Equal(1, MirrorHazeRow.Default);
        Assert.Equal(["Off", "Slight*", "More*"], MirrorHazeRow.Names);
        for (int v = 0; v < 3; v++) Assert.Equal(v, MirrorHazeRow.Parse(MirrorHazeRow.Word(v)));
        Assert.Null(MirrorHazeRow.Word(MirrorHazeRow.Default));   // the default is not written: a file from before takes it
        Assert.Equal(MirrorHazeRow.Default, MirrorHazeRow.Parse(""));
        Assert.Equal((0f, 0f), (MirrorHazeRow.Of(0).Veil, MirrorHazeRow.Of(0).Blur));
        Assert.True(MirrorHazeRow.Of(2).Veil > MirrorHazeRow.Of(1).Veil && MirrorHazeRow.Of(1).Veil > 0);
        var to = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.MirrorHaze, new ShellSettings { MirrorHaze = 0 }, to);
        Assert.Equal(0, to.MirrorHaze);
    }

    private static float Deg(float d) => d * MathF.PI / 180f;

    private static void Near(Vector2 want, Vector2 got, float tol = 1e-5f) =>
        Assert.True((want - got).Length() < tol, $"want {want}, got {got}");
}
