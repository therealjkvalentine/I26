using System.Numerics;
using I76.Formats.Bwd2;
using I76.Formats.Vfs;
using I76.Sim.Damage;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// Play Options' Difficulty Level as the original applies it: the player's round damage ×2.0 at Wimp (0), ×1.0 at Champion (1),
/// ×0.75 at Badass (2), truncated; the .45 and the computer's cars always ×1.0; and everyone ×1.0 in a network game, the
/// original's factor being tested off the network only.
/// </summary>
public class DifficultyDamageTests
{
    private static string? InstallDir =>
        Environment.GetEnvironmentVariable("I76_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0f, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static readonly Vector3 Front = new(0, 0.5f, 2);

    /// <summary>The 20 mm cannon (gclight.gdf) where it matters here: 45 a round, ordnance kind 6, the ballistic channel.</summary>
    private static readonly GizmoDef Cannon = Armory.HandgunDef(1) with
    {
        File = "gclight.gdf", Name = "20mm Cannon", Class = 2, Kind = 6, Damage = 45,
        Ordnance = new OrdnanceDef(6, 300f, 0f, 1, 0, ["", "", "", ""], ["", "", "", ""]),
    };

    private sealed record Rig(SimWorld World, Combat Combat, DamageModel Damage, Vehicle Player, Vehicle Ai);

    private static Rig Setup(int difficulty, bool net = false, bool coop = false)
    {
        var world = new SimWorld(new Flat(), new CrtRand(1)) { Coop = coop };
        var player = new Vehicle { Flags = VehicleFlags.EngineOn };
        var ai = new Vehicle { Flags = VehicleFlags.EngineOn };
        world.Vehicles.Add(player); world.Vehicles.Add(ai);
        world.Player = player;
        foreach (var v in world.Vehicles)
        {
            v.Health = new VehicleDamage(v, v == player, net);
            for (int z = 0; z < 4; z++) v.Health.Armour[z] = v.Health.ArmourMax[z] = 500;
        }
        var combat = new Combat(world, _ => null) { Difficulty = difficulty, NetSession = net };
        return new Rig(world, combat, new DamageModel(world, combat), player, ai);
    }

    private static Round Shot(Vehicle shooter, GizmoDef def) =>
        new() { Kind = def.Kind, Shooter = shooter, Def = def, Ord = def.Ordnance!, Damage = (int)def.Damage };

    /// <summary>What one round from <paramref name="shooter"/> takes off the front armour of <paramref name="target"/>.</summary>
    private static int ArmourTaken(Rig r, Vehicle shooter, Vehicle target, GizmoDef def)
    {
        int before = target.Health!.Armour[0];
        r.Damage.VehicleSlot(target, r.Combat.RoundPacket(Shot(shooter, def)), Front);
        return before - target.Health.Armour[0];
    }

    [Theory]
    [InlineData(0, 90)]     // Wimp: 45 × 2
    [InlineData(1, 45)]     // Champion
    [InlineData(2, 33)]     // Badass: 45 × 0.75 = 33.75, truncated
    [InlineData(7, 45)]     // a value out of range reads as Champion
    public void ThePlayersCannonRoundIsScaledByTheDifficulty(int difficulty, int taken)
    {
        var r = Setup(difficulty);
        Assert.Equal(taken, ArmourTaken(r, r.Player, r.Ai, Cannon));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheComputersRoundsAreNeverScaled(int difficulty)
    {
        var r = Setup(difficulty);
        Assert.Equal(45, ArmourTaken(r, r.Ai, r.Player, Cannon));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ANetworkGameIgnoresTheDifficulty(int difficulty)
    {
        var melee = Setup(difficulty, net: true);
        Assert.Equal(45, ArmourTaken(melee, melee.Player, melee.Ai, Cannon));
        // another human's car, as on the server or in a listen host's own game
        var other = new Vehicle { Flags = VehicleFlags.EngineOn };
        melee.World.Vehicles.Add(other);
        melee.World.Humans.Add(other);
        Assert.Equal(1f, melee.Combat.DifficultyFactor(other));
        var coop = Setup(difficulty, coop: true);
        Assert.Equal(45, ArmourTaken(coop, coop.Player, coop.Ai, Cannon));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TheHandgunIsNeverScaled(int difficulty)
    {
        var r = Setup(difficulty);
        var gun = Armory.HandgunDef(1);
        var p = r.Combat.RoundPacket(Shot(r.Player, gun));
        int bits = System.Numerics.BitOperations.PopCount(gun.Ordnance!.Mask & 0xf);
        for (int c = 0; c < 4; c++) if ((gun.Ordnance.Mask & (1u << c)) != 0) Assert.Equal((int)gun.Damage / bits, p.Ch[c]);
    }

    /// <summary>The flamer reads the same factor: the player's ×2.0 / ×0.75 off the network, ×1.0 on it.</summary>
    [Fact]
    public void TheFlameFactorFollowsTheSameRule()
    {
        var wimp = Setup(0);
        Assert.Equal(2f, wimp.Combat.DifficultyFactor(wimp.Player));
        Assert.Equal(1f, wimp.Combat.DifficultyFactor(wimp.Ai));
        var badass = Setup(2);
        Assert.Equal(0.75f, badass.Combat.DifficultyFactor(badass.Player));
        var net = Setup(0, net: true);
        Assert.Equal(1f, net.Combat.DifficultyFactor(net.Player));
    }

    /// <summary>The stand-in above is the game's 20 mm cannon.</summary>
    [Fact]
    public void TheCannonIsTheGames()
    {
        if (InstallDir is null) return;
        var vfs = new AssetVfs(new GameInstall(InstallDir));
        var def = GizmoDef.Read("gclight.gdf", vfs.Read("gclight.gdf"))!;
        Assert.Equal((Cannon.Name, Cannon.Damage, Cannon.Kind, Cannon.Ordnance!.Mask), (def.Name, def.Damage, def.Kind, def.Ordnance!.Mask));
    }
}
