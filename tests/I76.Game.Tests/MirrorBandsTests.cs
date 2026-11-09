using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The side-view mirrors' bands of the one mirror render (MirrorBands): the shares follow the projection, the rear mirror keeps its
/// 45 degrees and its pixels, the three bands tile, and the mirrors stay inside the window's view.
/// </summary>
public class MirrorBandsTests
{
    [Fact]
    public void TheRearMirrorsShareIsTheProjectionsNotHalf()
    {
        // 45 of 90 degrees is tan(22.5°)/tan(45°) of the width, not 50 %; 42 of 90 would be tan(21°)/tan(45°)
        Assert.Equal(0.4142f, MirrorBands.RearShare(90f), 0.0005f);
        Assert.Equal(0.3839f, MirrorBands.Share(90f, 42f), 0.0005f);
        Assert.Equal(1f, MirrorBands.RearShare(45f), 0.0001f);   // the rear mirror alone
        var (from, to) = MirrorBands.Rear(90f);
        Assert.Equal(0.2929f, from, 0.0005f);
        Assert.Equal(0.7071f, to, 0.0005f);
    }

    [Fact]
    public void TheFieldStaysWithinTheOriginalsSetUp()
    {
        Assert.Equal(45f, MirrorBands.Fov(30f));
        Assert.Equal(135f, MirrorBands.Fov(200f));
        Assert.Equal(90f, MirrorBands.Fov(MirrorBands.DefaultFov));
    }

    [Theory]
    [InlineData(90f, 2472)]
    [InlineData(45f, 1024)]
    [InlineData(120f, 4280)]
    public void TheRenderKeepsTheRearMirrorsPixelsPerDegree(float fov, int width)
    {
        Assert.Equal(width, MirrorBands.Width(fov, 1024));
        // the rear band is as many pixels wide as the rear mirror alone, to the rounding
        Assert.InRange(MirrorBands.Width(fov, 1024) * MirrorBands.RearShare(fov), 1020f, 1028f);
    }

    [Fact]
    public void TheThreeBandsTileTheRender()
    {
        var rear = MirrorBands.Rear(90f);
        var left = MirrorBands.Side(90f, 1f, 0f, left: true);
        var right = MirrorBands.Side(90f, 1f, 0f, left: false);
        Assert.Equal(0f, left.From, 0.0001f);
        Assert.Equal(rear.From, left.To);
        Assert.Equal(rear.To, right.From);
        Assert.Equal(1f, right.To, 0.0001f);
        // a share of the outer band is taken from the rear's edge outward
        var half = MirrorBands.Side(90f, 0.5f, 0f, left: true);
        Assert.Equal(rear.From, half.To);
        Assert.Equal(rear.From / 2, half.From, 0.0001f);
    }

    [Fact]
    public void ABandsAnglesAreItsEdgesOffTheAxis()
    {
        const float deg = MathF.PI / 180f;
        var rear = MirrorBands.Angles(MirrorBands.Rear(90f), 90f);
        Assert.Equal(-22.5f * deg, rear.From, 0.001f);
        Assert.Equal(22.5f * deg, rear.To, 0.001f);
        var left = MirrorBands.Angles(MirrorBands.Side(90f, 1f, 0f, left: true), 90f);
        Assert.Equal(-45f * deg, left.From, 0.001f);
        Assert.Equal(-22.5f * deg, left.To, 0.001f);
    }

    [Fact]
    public void ASideMirrorShowsTheRearsEdgeAgainByTheOverlap()
    {
        const float deg = MathF.PI / 180f;
        // 6 degrees in from the rear's edge: the left mirror's inner edge sees 16.5 degrees off the axis, its outer edge still 45
        var left = MirrorBands.Angles(MirrorBands.Side(90f, 1f, 6f, left: true), 90f);
        Assert.Equal(-45f * deg, left.From, 0.001f);
        Assert.Equal(-16.5f * deg, left.To, 0.001f);
        var right = MirrorBands.Angles(MirrorBands.Side(90f, 1f, 6f, left: false), 90f);
        Assert.Equal(16.5f * deg, right.From, 0.001f);
        Assert.Equal(45f * deg, right.To, 0.001f);
        // the overlap is the rear band's own columns: a side band's inner edge lies inside the rear's
        var rear = MirrorBands.Rear(90f);
        Assert.True(MirrorBands.Side(90f, 1f, 6f, left: true).To > rear.From);
        // the overlap is kept under the rear's half angle
        Assert.Equal(MirrorBands.Side(90f, 1f, 20f, left: true).To, MirrorBands.Side(90f, 1f, 50f, left: true).To);
    }

    [Theory]
    [InlineData(45f, 60.8f, 45f)]    // 21:9 at the Monitor field: as asked
    [InlineData(45f, 53f, 43f)]      // 16:9: kept 10 degrees inside
    [InlineData(45f, 45f, 35f)]      // 4:3
    [InlineData(45f, 20f, 15f)]      // never nearer the middle than 15
    public void ASideMirrorStaysInsideTheWindowsView(float wanted, float half, float yaw) => Assert.Equal(yaw, MirrorBands.Yaw(wanted, half), 0.01f);

    [Fact]
    public void TheHalfFieldAcrossFollowsTheWindowsShape()
    {
        // the original's 73.7 degrees of height: 45 across on 4:3, about 60.8 on 21:9
        Assert.Equal(45f, MirrorBands.HalfFovAcross(73.74f, 4f / 3f), 0.05f);
        Assert.Equal(60.8f, MirrorBands.HalfFovAcross(73.74f, 3440f / 1440f), 0.1f);
    }
}
