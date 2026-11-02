using System.Numerics;
using I76.Sim.Damage;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The attacker recorder (the scripts' isShot / isAttacked / whoAttacked, the AI's attacker memory): a blast's splash
/// goes straight to the class damage slot in the original and records nothing; a round's direct hit does.
/// </summary>
public class SplashRecordTests
{
    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    [Fact]
    public void ASplashRecordsNoAttackerAndADirectHitDoes()
    {
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var victim = new Vehicle { Pos = new Double3(0, 0, 0) };
        var shooter = new Vehicle { Pos = new Double3(0, 0, 50) };
        world.Vehicles.Add(victim); world.Vehicles.Add(shooter);
        var combat = new Combat(world, _ => null);
        var dm = new DamageModel(world, combat);
        var recorded = new List<(Vehicle Victim, Vehicle Attacker, DamageKind Kind)>();
        dm.Recorded += (v, a, k, _) => recorded.Add((v, a, k));
        combat.Splash(new Vector3(2, 0, 0), 10, 20, shooter);
        Assert.Empty(recorded);
        var round = new DamagePacket { Attacker = shooter, Mask = 1, Kind = DamageKind.Round };
        round.Ch[0] = 5;
        dm.Apply(victim, round, new Vector3(0, 0.5f, 2));
        Assert.Equal((victim, shooter, DamageKind.Round), Assert.Single(recorded));
    }
}
