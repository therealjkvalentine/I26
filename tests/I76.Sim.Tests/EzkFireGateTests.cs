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
/// The AI's fire gate for the EZK mortar (class 3 subtype 3): a skill gate of two rand()%1000 against 3500 x skill, then
/// the time the target's line passes the muzzle line at a fixed 500 (the muzzle flipped when it points away), kept
/// within 0..0.2 s, the target then within 20 on the ground plane. It used to fall through and never fire.
/// </summary>
public class EzkFireGateTests
{
    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static readonly Transform Ident = new([1, 0, 0, 0, 1, 0, 0, 0, 1], [0, 0, 0]);

    /// <summary>A shooter at the origin facing +z with an EZK on a fixed mount, and a still target.</summary>
    private static (Combat C, Vehicle Shooter, Vehicle Target, Weapon W) Rig(uint tier = 3)
    {
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var shooter = new Vehicle();
        var target = new Vehicle();
        world.Vehicles.Add(shooter); world.Vehicles.Add(target);
        var def = Armory.HandgunDef(1) with { Class = 3, Tier = tier, Kind = 7, Speed = 60, Ammo = 100 };
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

    [Fact]
    public void TheAiFiresAnEzkAtACarOnItsMuzzleLine()
    {
        var (c, shooter, target, w) = Rig();
        target.Pos = new Double3(0, 0, 50);                       // reached in 0.1 s at 500
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));
        target.Pos = new Double3(0, 0, -50);                      // behind: the muzzle line is flipped
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));
        target.Pos = new Double3(15, 0, 50);                      // 15 to the side
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));
    }

    [Fact]
    public void ItHoldsBeyondTheTimeWindowOrTwentyAside()
    {
        var (c, shooter, target, w) = Rig();
        target.Pos = new Double3(0, 0, 150);                      // 0.3 s at 500
        Assert.False(c.AiShouldFire(shooter, target, w, 1, 1));
        target.Pos = new Double3(25, 0, 50);
        Assert.False(c.AiShouldFire(shooter, target, w, 1, 1));
        // a car crossing ahead: 30 aside now, on the line 0.1 s later
        target.Pos = new Double3(-30, 0, 50);
        target.Vel = new Vector3(300, 0, 0);
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));
    }

    [Fact]
    public void TheSkillGateIsTwoDrawsAgainst3500TimesTheSkill()
    {
        var (c, shooter, target, w) = Rig();
        target.Pos = new Double3(0, 0, 50);
        // skill 0.1: the two draws must sum to 350 at most, about 6 % of the time
        int fired = Enumerable.Range(0, 1000).Count(_ => c.AiShouldFire(shooter, target, w, 0.1f, 1));
        Assert.InRange(fired, 30, 100);
        // skill 0.6: 2100 is above the largest sum, 1998
        Assert.All(Enumerable.Range(0, 100), _ => Assert.True(c.AiShouldFire(shooter, target, w, 0.6f, 1)));
    }

    [Fact]
    public void OtherMortarsKeepTheirLandingTest()
    {
        var (c, shooter, target, w) = Rig(tier: 1);
        target.Pos = new Double3(0, 0, 50);                       // the line test would fire; the landing point is 80 out
        Assert.False(c.AiShouldFire(shooter, target, w, 1, 1));
        target.Pos = new Double3(0, 0, 80);
        Assert.True(c.AiShouldFire(shooter, target, w, 1, 1));
    }
}
