using System.Numerics;
using I76.Formats.Bwd2;
using I76.Sim.Ai;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The turret's lead: the target predictor (time + 0.09 s, along the turning circle when the target turns) at the
/// closest-approach time of a round leaving along the barrel at the gun's aim speed plus the carrier's velocity.
/// Measured in the original (retail 1.0, emulated Pentium, 2 boots): a crossing target at 50 m was led by 0.4305 s at
/// 15 m/s and 0.4376 s at 30 m/s (lead distance / speed), the .30 and .50 cal both aiming at 150.
/// </summary>
public class TurretLeadTests
{
    private const float Dt = 102 / 2048f;

    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static readonly Transform Ident = new([1, 0, 0, 0, 1, 0, 0, 0, 1], [0, 0, 0]);

    /// <summary>A still car with a healthy turret aiming at 150 and its radar on <paramref name="target"/>, the barrel let settle.</summary>
    private static Weapon Settle(Vehicle target)
    {
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var shooter = new Vehicle { Flags = VehicleFlags.EngineOn };
        world.Vehicles.Add(shooter); world.Vehicles.Add(target);
        var def = Armory.HandgunDef(1) with { Class = 2, Tier = 0, Kind = 1, Speed = 150, Ammo = 0 };
        var w = new Weapon
        {
            Def = def, Hardpoint = new Hardpoint("hp", 0, 1, 3, Ident), Style = 3, MountKind = 4, BaseRot = Rows.Identity,
            Hp = 45, MaxHp = 45, Damage = 15, Ammo = 0,
        };
        var a = new Armory();
        a.Weapons.Add(w); a.LoadOrder.Add(w); a.Selected = 0;
        shooter.Armory = a;
        var c = new Combat(world, _ => null);
        c.RadarTarget[shooter] = target;
        for (int i = 0; i < 200; i++) c.Frame(Dt);
        return w;
    }

    private static Vehicle Crossing(float speed, float yawRate = 0, float heading = MathF.PI / 2)
    {
        var rot = Rows.AboutY(heading);
        return new Vehicle
        {
            Pos = new Double3(0, 0, 50), Rot = rot, Vel = rot.Forward * speed, Speed = speed, Gear = 1,
            AngVel = new Vector3(0, yawRate, 0), Flags = VehicleFlags.EngineOn,
        };
    }

    [Theory]
    [InlineData(15f, 0.428f)]
    [InlineData(30f, 0.436f)]
    public void ACrossingTargetIsLedByTheClosestApproachPlusTheFixedLead(float speed, float lead)
    {
        var target = Crossing(speed);
        var w = Settle(target);
        float lateral = 50 * MathF.Tan(w.AimYaw);   // the target crosses 50 ahead of the muzzle
        Assert.Equal(lead, lateral / speed, 0.005f);
    }

    [Fact]
    public void ATurningTargetIsLedAlongItsCircle()
    {
        var target = Crossing(20, 1f, MathF.PI);   // coming head on, turning: its circle takes it about 1.8 m aside
        var w = Settle(target);
        var barrel = w.MuzzleRot.Forward;
        float t = Combat.TurretLeadTime(w.MuzzlePos, barrel * 150, target.Pos.ToVector3(), target.Vel);
        var arc = TargetPredictor.Predict(target, t) + new Vector3(0, 0.8f, 0) - w.MuzzlePos;
        var line = target.Pos.ToVector3() + target.Vel * (t + TargetPredictor.Lead) + new Vector3(0, 0.8f, 0) - w.MuzzlePos;
        Assert.Equal(MathF.Atan2(arc.X, arc.Z), w.AimYaw, 0.005f);
        Assert.True(MathF.Abs(MathF.Atan2(line.X, line.Z) - w.AimYaw) > 0.02f, $"aim {w.AimYaw} on the straight line");
    }
}
