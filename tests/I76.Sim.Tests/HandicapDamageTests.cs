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
/// (−5 ×0.2 … +5 ×5, +n the inverse of −n), truncated as the round's damage always is, and the target's level scales what it takes
/// by the inverse, held to ×⅓..×3; rams and the .45's shot too; level 0 is today's damage exactly; the level stays with the car
/// through a respawn and is saved with it.
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
    [InlineData(5, 225, 15)]     // 45 × 5; 45 × ⅓ (the inverse, 0.2, held to ⅓)
    [InlineData(3, 112, 18)]     // 45 × 2.5 = 112.5; 45 × 0.4
    [InlineData(2, 81, 24)]      // 45 × 1.82; 45 × 0.55 = 24.75
    [InlineData(1, 60, 33)]      // 45 × 1.33; 45 × 0.75 = 33.75
    [InlineData(0, 45, 45)]
    [InlineData(-1, 33, 60)]
    [InlineData(-2, 24, 81)]
    [InlineData(-3, 18, 112)]
    [InlineData(-5, 9, 135)]     // 45 × 0.2; 45 × 3 (the inverse, 5, held to 3)
    public void TheShootersLevelScalesItsRoundAndTheTargetsWhatItTakes(int level, int dealt, int taken)
    {
        var r = Melee();
        r.A.Handicap = level;
        Assert.Equal(dealt, ArmourTaken(r, r.A, r.B, Cannon));
        var q = Melee();
        q.B.Handicap = level;
        Assert.Equal(taken, ArmourTaken(q, q.A, q.B, Cannon));
    }

    /// <summary>A ram is scaled as the rammer's rounds are, and by what the rammed car takes: no way round the level.</summary>
    [Fact]
    public void ARamIsScaledByBothLevels()
    {
        int Ram(int rammer, int rammed)
        {
            var r = Melee();
            (r.A.Handicap, r.B.Handicap) = (rammer, rammed);
            r.A.Vel = new Vector3(0, 0, -30);
            for (int z = 0; z < 4; z++) r.B.Health!.Chassis[z] = 500;
            int before = r.B.Health!.Chassis[0];
            r.Damage.CarContact(r.B, r.A, new Contact { N = Vector3.UnitZ, C = Front });
            return before - r.B.Health.Chassis[0];
        }
        int even = Ram(0, 0);
        Assert.True(even > 20, $"{even}");
        Assert.Equal((int)(even * Handicaps.Factor(-5)), Ram(-5, 0));
        Assert.Equal((int)(even * 3f), Ram(0, -5));
        Assert.Equal((int)((int)(even * Handicaps.Factor(-2)) * Handicaps.Taken(2)), Ram(-2, 2));
    }

    /// <summary>The .45 on a healthy car deals 5 points whatever the round: a level scales those too (a −5 leader's shot 1, on a −5 leader ×3).</summary>
    [Theory]
    [InlineData(0, 0, 6)]       // the 5 points through the armour step
    [InlineData(-5, 0, 1)]      // 5 × 0.2
    [InlineData(0, -5, 18)]     // 15
    [InlineData(5, -5, 90)]     // 5 × 5 × 3 = 75
    public void TheHandgunsShotIsScaledByBothLevels(int shooter, int target, int taken)
    {
        var r = Melee();
        (r.A.Handicap, r.B.Handicap) = (shooter, target);
        int before = r.B.Health!.Armour[0];
        r.Damage.VehicleSlot(r.B, r.Combat.RoundPacket(Shot(r.A, Armory.HandgunDef(1))), Front);
        Assert.Equal(taken, before - r.B.Health.Armour[0]);
    }

    [Fact]
    public void TheTableIsTheDocumentedOne()
    {
        float[] down = [0.2f, 0.3f, 0.4f, 0.55f, 0.75f];
        Assert.Equal([.. down, 1f, .. down.Reverse().Select(f => 1f / f)], Enumerable.Range(Handicaps.Min, 11).Select(Handicaps.Factor), new Near());
        Assert.Equal([3f, 3f, 1f / 0.4f, 1f / 0.55f, 1f / 0.75f, 1f, 0.75f, 0.55f, 0.4f, 1f / 3, 1f / 3],
            Enumerable.Range(Handicaps.Min, 11).Select(Handicaps.Taken), new Near());
        Assert.Equal(5f, Handicaps.Factor(9), 4);        // clamped
        Assert.Equal(0.2f, Handicaps.Factor(-9));
        Assert.Equal(["-5", "-4", "-3", "-2", "-1", "0", "+1", "+2", "+3", "+4", "+5"], Enumerable.Range(Handicaps.Min, 11).Select(Handicaps.Label));
    }

    private sealed class Near : IEqualityComparer<float>
    {
        public bool Equals(float a, float b) => System.Math.Abs(a - b) < 1e-5f;
        public int GetHashCode(float f) => 0;
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
