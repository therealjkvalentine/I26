using System.Numerics;
using I76.Formats.Bwd2;
using I76.Formats.Vfs;
using I76.Sim.Damage;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>What killed a car rides on its death event with a seed, for the modern look's deaths; the original's death is unchanged.</summary>
public class DeathCauseTests
{
    private static string? InstallDir =>
        Environment.GetEnvironmentVariable("I76_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0f, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static (SimWorld World, DamageModel Damage, Vehicle A, Vehicle B)? Setup()
    {
        if (InstallDir is null) return null;
        var vfs = new AssetVfs(new GameInstall(InstallDir));
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var a = VehicleFactory.Create(vfs, "vppirna1.vcf");
        var b = VehicleFactory.Create(vfs, "vppirna1.vcf");
        b.Pos = new Double3(20, 0, 0);
        world.Vehicles.Add(a); world.Vehicles.Add(b);
        world.Player = a;
        foreach (var v in world.Vehicles) v.Health = VehicleDamage.Create(v, v.Build!, v == world.Player, world.Rand);
        var combat = new Combat(world, n => vfs.TryRead(n) is { } d ? EffectDef.Read(n, d) : null);
        return (world, new DamageModel(world, combat), a, b);
    }

    [Theory]
    [InlineData(0, DeathCause.Wheel)]
    [InlineData(5, DeathCause.Wheel)]
    [InlineData(6, DeathCause.Driver)]
    [InlineData(7, DeathCause.Engine)]
    [InlineData(8, DeathCause.Suspension)]
    [InlineData(9, DeathCause.Brakes)]
    [InlineData(10, DeathCause.Weapon)]
    [InlineData(16, DeathCause.Weapon)]
    [InlineData(17, DeathCause.Special)]
    [InlineData(19, DeathCause.Special)]
    [InlineData(-1, DeathCause.Destroyed)]
    public void TheKillRollsSlotNamesTheCause(int slot, DeathCause cause) => Assert.Equal(cause, DamageModel.CauseOf(slot));

    /// <summary>A round from the front on a car whose only part is a dead engine: the kill roll on the engine.</summary>
    [Fact]
    public void ARoundThatKillsOnADeadEngineIsAnEngineDeath()
    {
        if (Setup() is not var (_, damage, _, b)) return;
        var h = b.Health!;
        for (int i = 0; i < h.Slots.Length; i++) if (i != 7) h.Slots[i] = null;
        h.Engine!.Hp = 0;
        for (int z = 0; z < 4; z++) h.Armour[z] = 0;
        var p = new DamagePacket { Mask = 2, Kind = DamageKind.Round };
        p.Ch[1] = 50;
        damage.Apply(b, p, new Vector3(0, 0.5f, 2));
        Assert.True(DamageModel.IsDead(b));
        var k = Assert.Single(damage.Events.OfType<VehicleKilled>());
        Assert.Equal(DeathCause.Engine, k.Cause);
        Assert.False(k.DriverKilled);
    }

    [Fact]
    public void DestroysNameTheirCause()
    {
        if (Setup() is not var (world, damage, a, b)) return;
        damage.Destroy(a, DeathCause.SelfDestruct);
        damage.FallKill(b);
        var ks = damage.Events.OfType<VehicleKilled>().ToList();
        Assert.Equal([DeathCause.SelfDestruct, DeathCause.Fall], ks.Select(k => k.Cause));
        if (Setup() is not var (_, d2, c, e)) return;
        ((IStepDamage)d2).RollKill(c);
        d2.Destroy(e);
        Assert.Equal([DeathCause.Rollover, DeathCause.Destroyed], d2.Events.OfType<VehicleKilled>().Select(k => k.Cause));
    }

    /// <summary>The seed is read from rand()'s state, never drawn from it: the original's sequence of draws is untouched.</summary>
    [Fact]
    public void TheSeedLeavesRandAlone()
    {
        var world = new SimWorld(new Flat(), new CrtRand(12345));
        world.Vehicles.Add(new Vehicle()); world.Vehicles.Add(new Vehicle());
        var damage = new DamageModel(world, new Combat(world, _ => null));
        uint before = world.Rand.Seed;
        ushort s0 = damage.DeathSeed(world.Vehicles[0]), s1 = damage.DeathSeed(world.Vehicles[1]);
        Assert.Equal(before, world.Rand.Seed);
        Assert.Equal(s0, damage.DeathSeed(world.Vehicles[0]));
        Assert.NotEqual(s0, s1);
    }
}
