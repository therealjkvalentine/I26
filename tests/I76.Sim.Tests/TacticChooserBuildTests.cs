using System.Numerics;
using System.Runtime.CompilerServices;
using I76.Formats.Exe;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The attack-tactic choice by build (<see cref="ScriptHost.TacticStub1997"/>): the 1998 build draws from the weighted lists
/// and strikes a candidate whose preconditions fail; the 1997 CD build's chooser returns dirt_gunner every time, with no draw
/// and no precondition (measured there: T14's lead cop in dirt_gunner from its first attack on, n = 7).
/// </summary>
public class TacticChooserBuildTests
{
    private sealed class Flat : I76.Sim.Terrain.IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public I76.Sim.Terrain.Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    /// <summary>An unarmed car and its target on flat ground with no roads.</summary>
    private static (ScriptHost Host, SimWorld World, Entity Car, Entity Target) Rig(PtEntry[]? table)
    {
        var world = new SimWorld(new Flat(), new I76.Sim.Rng.CrtRand(1));
        Entity Car(string name, float x)
        {
            var v = new Vehicle { Pos = new Double3(x, 0, 0), Flags = VehicleFlags.EngineOn };
            world.Vehicles.Add(v);
            return new Entity(name, [], 1, 0) { Vehicle = v };
        }
        var target = Car("player", 0);
        var car = Car("cop", 60);
        var host = new ScriptHost(world, new List<Entity> { target, car },
            (Objectives)RuntimeHelpers.GetUninitializedObject(typeof(Objectives)), _ => null) { Tactics = table };
        return (host, world, car, target);
    }

    [Fact]
    public void The1997ChooserReturnsDirtGunnerWithoutADraw()
    {
        var (host, world, car, target) = Rig(null);
        host.TacticStub1997 = true;
        uint seed = world.Rand.Seed;
        // the first choice (record 0) and the time-up and rammed lists of any tactic: the same answer, the unarmed car
        // failing dirt_gunner's gun-ready condition notwithstanding
        foreach (var (s, r) in new[] { (Ptable.TacticsInit, 0), (1, 1), (3, 0), (6, 1), (14, 0) })
            Assert.Equal(Ptable.DirtGunner, host.Ptable.ChooseTactic(car, target, s, r));
        Assert.Equal(seed, world.Rand.Seed);
    }

    [Fact]
    public void The1998ChooserStrikesDirtGunnerForACarWithoutAGun()
    {
        if (TacticsTableTests.Installed is not { } table) return;
        var (host, world, car, target) = Rig(table);
        uint seed = world.Rand.Seed;
        var picks = new HashSet<int>();
        for (int i = 0; i < 50; i++) picks.Add(host.Ptable.ChooseTactic(car, target, Ptable.TacticsInit, 0));
        // off road and unarmed: the road tactics and the gun tactics fail their conditions; dirt_miner always passes
        Assert.DoesNotContain(Ptable.DirtGunner, picks);
        Assert.Contains(8, picks);
        Assert.NotEqual(seed, world.Rand.Seed);
    }
}
