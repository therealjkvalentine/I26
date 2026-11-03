using System.Numerics;
using I76.Formats.Bwd2;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The AI's dropper (class 6) fire gate predicts the target in a straight line at its arrival time; the mortar (class 3,
/// subtypes other than the EZK) predicts it with the target predictor: the gate's time + 0.09 s, along the turning
/// circle when the target turns (ai.md, the target predictor's callers). The dropper's cases are measured in the
/// original (retail 1.0, emulated Pentium, 2 boots).
/// </summary>
public class AiDropAndMortarGateTests
{
    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static readonly Transform Ident = new([1, 0, 0, 0, 1, 0, 0, 0, 1], [0, 0, 0]);

    /// <summary>A shooter at the origin facing +z with one weapon of the class on a fixed mount, and a target.</summary>
    private static (Combat C, Vehicle Shooter, Vehicle Target, Weapon W) Rig(uint cls, uint tier)
    {
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var shooter = new Vehicle();
        var target = new Vehicle();
        world.Vehicles.Add(shooter); world.Vehicles.Add(target);
        var def = Armory.HandgunDef(1) with { Class = cls, Tier = tier, Speed = 60, Ammo = 100 };
        var w = new Weapon
        {
            Def = def, Hardpoint = new Hardpoint("hp", 0, 1, 1, Ident), Style = 1, MountKind = 1, BaseRot = Rows.Identity,
            Hp = 45, MaxHp = 45, Damage = (int)def.Damage, Ammo = 100,
        };
        var a = new Armory();
        a.Weapons.Add(w); a.LoadOrder.Add(w); a.Selected = 0;
        shooter.Armory = a;
        return (new Combat(world, _ => null), shooter, target, w);
    }

    private static void Drive(Vehicle v, Vector3 at, float speed, float yawRate = 0)
    {
        v.Pos = Double3.From(at);
        v.Vel = new Vector3(0, 0, speed); v.Speed = speed;
        v.AngVel = new Vector3(0, yawRate, 0);
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(120f)]
    public void ADropperDropsOnAPursuerStraightBehindAtAnySpeed(float behind)
    {
        var (c, shooter, target, w) = Rig(6, 0);
        Drive(shooter, Vector3.Zero, 20);
        foreach (float v in new[] { 20f, 27f, 29f, 32f })
        {
            Drive(target, new Vector3(0, 0, -behind), v);
            Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));
        }
    }

    [Theory]
    [InlineData(40f)]
    [InlineData(50f)]
    [InlineData(60f)]
    public void ADropperDropsOnAFastPursuerStraightOn(float speed)
    {
        // measured: the original dropped under a pursuer coming straight on at 40, 50 and 60 m/s
        var (c, shooter, target, w) = Rig(6, 0);
        Drive(shooter, Vector3.Zero, 20);
        Drive(target, new Vector3(0, 0, -60), speed);
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));
    }

    [Fact]
    public void ADropperDropsOnAPursuer2Point3MAsideButNot2Point8()
    {
        // measured: dropped on at 2.3 m of lateral offset, not at 2.8 (the 2.5 m radius)
        var (c, shooter, target, w) = Rig(6, 0);
        Drive(shooter, Vector3.Zero, 20);
        Drive(target, new Vector3(2.3f, 0, -60), 40);
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));
        Drive(target, new Vector3(2.8f, 0, -60), 40);
        Assert.False(c.AiShouldFire(shooter, target, w, 1, 1));
    }

    [Fact]
    public void ADropperPredictsATurningPursuerInAStraightLine()
    {
        var (c, shooter, target, w) = Rig(6, 0);
        Drive(shooter, Vector3.Zero, 20);
        // 20 m/s turning left at 0.2 rad/s from behind and to the right, its circle running through the dropper: in a
        // straight line it passes 27 m aside, so no drop, whichever way and however fast it turns
        Drive(target, new Vector3(27.3455f, 0, -68.7119f), 20, 0.2f);
        Assert.False(c.AiShouldFire(shooter, target, w, 1, 1));
        Drive(target, new Vector3(-27.3455f, 0, -68.7119f), 20, -0.2f);
        Assert.False(c.AiShouldFire(shooter, target, w, 1, 1));
        // turning, but its straight line runs over the spot: dropped on
        Drive(target, new Vector3(0, 0, -60), 30, 0.2f);
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));
    }

    [Fact]
    public void AMortarLeadsTheTargetByItsTimePlusTheFixedLead()
    {
        // subtype 0: 0.3 s; the round lands 80 ahead of the still shooter, and a target crossing at 20 m/s is
        // predicted 0.39 s on (7.8 m), the release radius 20
        var (c, shooter, target, w) = Rig(3, 0);
        target.Pos = new Double3(-27, 0, 80); target.Vel = new Vector3(20, 0, 0); target.Speed = 20;
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));     // predicted 19.2 from the landing point
        target.Pos = new Double3(-28.5f, 0, 80);
        Assert.False(c.AiShouldFire(shooter, target, w, 1, 1));    // 20.7
        // subtype 2: 0.4 s (0.49 for the target, 9.8 m), radius sqrt(150) = 12.25
        (c, shooter, target, w) = Rig(3, 2);
        target.Pos = new Double3(-21.5f, 0, 80); target.Vel = new Vector3(20, 0, 0); target.Speed = 20;
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));     // 11.7
        target.Pos = new Double3(-22.5f, 0, 80);
        Assert.False(c.AiShouldFire(shooter, target, w, 1, 1));    // 12.7
    }
}
