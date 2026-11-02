using System.Numerics;
using I76.Formats.Bwd2;
using I76.Sim.Damage;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// A network game's handicaps (docs/multiplayer.md "Handicaps"): the shooter's level scales what its rounds deal, by the table
/// (+5 ×2.2 … −5 ×0.3), truncated as the round's damage always is; level 0 is today's damage exactly; the level stays with the
/// car through a respawn and is saved with it.
/// </summary>
public class HandicapDamageTests
{
    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0f, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static readonly Vector3 Front = new(0, 0.5f, 2);

    /// <summary>The 20 mm cannon's numbers (gclight.gdf): 45 a round, ordnance kind 6, the ballistic channel.</summary>
    private static readonly GizmoDef Cannon = Armory.HandgunDef(1) with
    {
        File = "gclight.gdf", Name = "20mm Cannon", Class = 2, Kind = 6, Damage = 45,
        Ordnance = new OrdnanceDef(6, 300f, 0f, 1, 0, ["", "", "", ""], ["", "", "", ""]),
    };

    private sealed record Rig(SimWorld World, Combat Combat, DamageModel Damage, Vehicle A, Vehicle B);

    /// <summary>Two human cars of a network melee, 500 armour a zone.</summary>
    private static Rig Melee()
    {
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var a = new Vehicle { Flags = VehicleFlags.EngineOn };
        var b = new Vehicle { Flags = VehicleFlags.EngineOn };
        world.Vehicles.Add(a); world.Vehicles.Add(b);
        world.Player = a;
        world.Humans.Add(b);
        foreach (var v in world.Vehicles)
        {
            v.Health = new VehicleDamage(v, true, true);
            for (int z = 0; z < 4; z++) v.Health.Armour[z] = v.Health.ArmourMax[z] = 500;
        }
        var combat = new Combat(world, _ => null) { NetSession = true };
        return new Rig(world, combat, new DamageModel(world, combat), a, b);
    }

    private static Round Shot(Vehicle shooter, GizmoDef def) =>
        new() { Kind = def.Kind, Shooter = shooter, Def = def, Ord = def.Ordnance!, Damage = (int)def.Damage };

    private static int ArmourTaken(Rig r, Vehicle shooter, Vehicle target, GizmoDef def)
    {
        int before = target.Health!.Armour[0];
        r.Damage.VehicleSlot(target, r.Combat.RoundPacket(Shot(shooter, def)), Front);
        return before - target.Health.Armour[0];
    }

    [Theory]
    [InlineData(3, 72)]      // 45 × 1.6
    [InlineData(2, 63)]      // 45 × 1.4
    [InlineData(1, 54)]      // 45 × 1.2
    [InlineData(0, 45)]
    [InlineData(-1, 38)]     // 45 × 0.85 = 38.25, truncated
    [InlineData(-2, 31)]     // 45 × 0.7 = 31.5
    [InlineData(-3, 24)]     // 45 × 0.55 = 24.75
    public void TheShootersLevelScalesItsRound(int level, int taken)
    {
        var r = Melee();
        r.A.Handicap = level;
        Assert.Equal(taken, ArmourTaken(r, r.A, r.B, Cannon));
        // the target's own level changes nothing it takes
        var q = Melee();
        q.B.Handicap = level;
        Assert.Equal(45, ArmourTaken(q, q.A, q.B, Cannon));
    }

    [Fact]
    public void TheTableIsTheDocumentedOne()
    {
        Assert.Equal([0.3f, 0.4f, 0.55f, 0.7f, 0.85f, 1f, 1.2f, 1.4f, 1.6f, 1.9f, 2.2f], Enumerable.Range(Handicaps.Min, 11).Select(Handicaps.Factor));
        Assert.Equal(2.2f, Handicaps.Factor(9));        // clamped
        Assert.Equal(0.3f, Handicaps.Factor(-9));
        Assert.Equal(["-5", "-4", "-3", "-2", "-1", "0", "+1", "+2", "+3", "+4", "+5"], Enumerable.Range(Handicaps.Min, 11).Select(Handicaps.Label));
    }

    /// <summary>Level 0 is today's packet, channel for channel, for every stock handgun and the cannon.</summary>
    [Fact]
    public void LevelZeroIsTodaysDamage()
    {
        var r = Melee();
        foreach (var def in new[] { Cannon, Armory.HandgunDef(0), Armory.HandgunDef(1) })
        {
            var p = r.Combat.RoundPacket(Shot(r.A, def));
            int bits = System.Numerics.BitOperations.PopCount(def.Ordnance!.Mask & 0xf);
            for (int c = 0; c < 4; c++)
                Assert.Equal((def.Ordnance.Mask & (1u << c)) != 0 ? (int)def.Damage / bits : 0, p.Ch[c]);
        }
    }

    /// <summary>The level is the player's: a respawn's rebuild keeps it, and the car's savestate carries it.</summary>
    [Fact]
    public void TheLevelStaysThroughARespawnAndIsSaved()
    {
        var v = new Vehicle { Handicap = -2, NetId = 7 };
        MeleeGame.Rebuild(v, new Vehicle(), new CrtRand(1));
        Assert.Equal(-2, v.Handicap);
        Assert.Equal(7, v.NetId);
        var w = new I76.Sim.Save.SaveWriter(new I76.Sim.Save.SaveRefs());
        I76.Sim.Save.VehicleSave.Write(w, v);
        var back = new Vehicle();
        I76.Sim.Save.VehicleSave.Read(new I76.Sim.Save.SaveReader(w.ToArray(), new I76.Sim.Save.SaveRefs()), back);
        Assert.Equal(-2, back.Handicap);
    }
}
