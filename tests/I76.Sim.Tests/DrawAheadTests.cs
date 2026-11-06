using System.Numerics;
using I76.Sim.Math;
using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>Draw Ahead's extrapolation: the newest tick carried forward by the motion of the last tick (docs/1997-parity.md, round 3).</summary>
public class DrawAheadTests
{
    private const double T = 85.0 / 2048;   // 24 Hz

    private static void Near(Vector3 want, Vector3 got, float eps = 1e-5f) => Assert.True((want - got).Length() < eps, $"{want} vs {got}");

    [Fact]
    public void ACarAtConstantVelocityExtrapolatesExactly()
    {
        var v = new Vector3(3, -0.5f, 25);   // m/s
        var p0 = new Double3(1000, 50, -2000);
        var p1 = p0 + v * (float)T;
        foreach (double ahead in new[] { 0.0, 0.25 * T, 0.5 * T, 0.99 * T, -0.4 * T })
        {
            var (rot, pos) = DrawAhead.Pose(Rows.Identity, p0, Rows.Identity, p1, T, ahead);
            Assert.Equal(p1.X + v.X * ahead, pos.X, 9);
            Assert.Equal(p1.Y + v.Y * ahead, pos.Y, 9);
            Assert.Equal(p1.Z + v.Z * ahead, pos.Z, 9);
            Near(Vector3.UnitZ, rot.Forward);
        }
    }

    [Fact]
    public void AStationaryCarDoesNotMove()
    {
        var r = Rows.Concat(Rows.AboutX(0.1f), Rows.AboutY(0.7f));
        var p = new Double3(12.5, 3, -40);
        var (rot, pos) = DrawAhead.Pose(r, p, r, p, T, 0.6 * T);
        Assert.Equal(p, pos);
        Near(r.Right, rot.Right); Near(r.Up, rot.Up); Near(r.Forward, rot.Forward);
    }

    [Fact]
    public void AConstantYawRateTurnsOnByTheSameRate()
    {
        const float w = 0.9f;   // rad/s
        var r0 = Rows.AboutY(0.3f);
        var r1 = Rows.AboutY(0.3f + w * (float)T);
        var (rot, _) = DrawAhead.Pose(r0, default, r1, default, T, 0.5 * T);
        var want = Rows.AboutY(0.3f + w * 1.5f * (float)T);
        Near(want.Forward, rot.Forward); Near(want.Right, rot.Right); Near(want.Up, rot.Up);
    }

    [Fact]
    public void AnEarlyTickAheadOfTheClockIsDrawnBackToThePresent()
    {
        var v = new Vector3(0, 0, 10);
        var p0 = new Double3(0, 0, 0);
        var p1 = p0 + v * (float)T;
        var (_, pos) = DrawAhead.Pose(Rows.Identity, p0, Rows.Identity, p1, T, -0.75 * T);
        Assert.Equal(p1.Z - 10 * 0.75 * T, pos.Z, 9);
    }

    [Fact]
    public void AJumpIsNotCarriedForwardAndTheReachIsAtMostATick()
    {
        var p0 = new Double3(0, 0, 0);
        var jumped = new Double3(500, 0, 0);
        Assert.Equal(jumped, DrawAhead.Pose(Rows.Identity, p0, Rows.Identity, jumped, T, 0.5 * T).Pos);
        var p1 = new Double3(0, 0, 1);
        Assert.Equal(2, DrawAhead.Pose(Rows.Identity, p0, Rows.Identity, p1, T, 5 * T).Pos.Z, 9);
    }
}
