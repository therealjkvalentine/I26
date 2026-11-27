using Godot;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The turned door-mirror glass kept within the art-placed housing's end as the driver sees it (DoorFace.FitTurned): the ring's top
/// no higher than the end's top, its bottom no lower than its bottom, every corner of the end's painted glass still under the ring,
/// its shape kept; the arm and the body further off do not count.
/// </summary>
public class MirrorTurnedFitTests
{
    private const int Share = 30;
    private const float Margin = 0.004f, Tuck = 0.05f, Hair = 0.004f;
    private static readonly float Over = 2 * MirrorRim.OuterShare, RingW = MirrorRim.RingWidth(Over);
    private static readonly Vector2 Up = new(0, 1);

    /// <summary>A six-sided outline with a corner at its top and bottom (as the Piranha's slanted end's highest corner), radius <paramref name="r"/> about (cx, cy), stretched <paramref name="w"/> across.</summary>
    private static List<Vector2> Hexagon(float cx, float cy, float r, float w = 1.3f) =>
        GlassRound.Hull([.. Enumerable.Range(0, 6).Select(i => new Vector2(cx + w * r * MathF.Cos(MathF.PI / 2 + i * MathF.PI / 3), cy + r * MathF.Sin(MathF.PI / 2 + i * MathF.PI / 3)))]);

    [Fact]
    public void ARingStandingPastTheEndIsBroughtWithinItsHeightAndStillCoversItsCorners()
    {
        var end = Hexagon(0.7f, 0.5f, 0.5f);
        var glass = Hexagon(0.7f, 0.56f, 0.5f);   // the turned head's glass: as large, standing higher
        var before = DoorFace.TurnedRing(glass, glass, Up, Share, Over, Margin, RingW);
        Assert.True(before.Hi > 1.0f + 0.05f);
        var fit = DoorFace.FitTurned(glass, glass, end, end, Up, Share, Over, Margin, RingW, Tuck);
        var after = DoorFace.TurnedRing(fit.Hull, fit.Cover, Up, Share, Over, Margin, RingW);
        Assert.True(fit.Top > 0.05f);
        Assert.True(after.Hi <= 1.0f + Hair, $"top {after.Hi}");
        Assert.True(after.Lo >= 0.0f - Hair, $"bottom {after.Lo}");
        // the top and bottom corners a hair past the rounded ring at most (it cannot reach a sharp corner at its own height), the others under it
        foreach (var q in end) Assert.True(GlassRound.Distance(after.Core, q) - after.Reach <= 0.02f, $"corner {q} past the ring by {GlassRound.Distance(after.Core, q) - after.Reach}");
        foreach (var q in end.Where(q => q.Y is > 0.1f and < 0.9f)) Assert.True(GlassRound.Distance(after.Core, q) - after.Reach <= Hair, $"side corner {q} past the ring");
        // the shape kept: at most a little wider across for its height
        float W(List<Vector2> p) => p.Max(q => q.X) - p.Min(q => q.X);
        float H(List<Vector2> p) => p.Max(q => q.Y) - p.Min(q => q.Y);
        Assert.InRange(W(fit.Hull) / H(fit.Hull) / (W(glass) / H(glass)), 0.999f, 1.17f);
    }

    [Fact]
    public void TheArmBelowDoesNotCount()
    {
        var end = Hexagon(0.7f, 0.5f, 0.5f);
        var glass = Hexagon(0.7f, 0.56f, 0.5f);
        var a = DoorFace.FitTurned(glass, glass, end, end, Up, Share, Over, Margin, RingW, Tuck);
        var b = DoorFace.FitTurned(glass, glass, [.. end, new(1.6f, -0.8f), new(1.9f, -0.9f)], end, Up, Share, Over, Margin, RingW, Tuck);
        Assert.Equal((a.Scale, a.Wide), (b.Scale, b.Wide));
        Assert.Equal(a.Shift, b.Shift);
    }

    [Fact]
    public void ARingWithinTheEndIsLeftAlone()
    {
        var glass = Hexagon(0.7f, 0.5f, 0.4f);
        var end = Hexagon(0.7f, 0.5f, 0.5f);
        var fit = DoorFace.FitTurned(glass, glass, end, end, Up, Share, Over, Margin, RingW, Tuck);
        Assert.Equal((1f, 1f, Vector2.Zero), (fit.Scale, fit.Wide, fit.Shift));
        Assert.Equal(glass, fit.Hull);
    }
}
