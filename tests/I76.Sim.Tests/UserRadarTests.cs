using System.Numerics;
using I76.Formats.Mission;
using I76.Sim.Fsm;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Save;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// setUserRadar (FSM action 78): a value other than the last one (1 at start) toggles the player's radar. The original's
/// toggle clears the radar's on bit as it disables it and only the disable bit as it enables it, so its first use leaves
/// the radar blank for the rest of the mission.
/// </summary>
public class UserRadarTests
{
    private sealed class Flat : I76.Sim.Terrain.IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public I76.Sim.Terrain.Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private sealed class RadarHost : RecordingHost, IFsmHost
    {
        public void SetUserRadar(int on) => Calls.Add($"radar {on}");
    }

    private static (Combat, Vehicle Me, Vehicle Other, Vehicle Ai) World()
    {
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var me = new Vehicle { Flags = VehicleFlags.EngineOn };
        var other = new Vehicle { Pos = new Double3(0, 0, 50), Flags = VehicleFlags.EngineOn };
        var ai = new Vehicle { Pos = new Double3(30, 0, 0), Flags = VehicleFlags.EngineOn };
        world.Vehicles.AddRange([me, other, ai]);
        world.Player = me;
        return (new Combat(world, _ => null), me, other, ai);
    }

    [Fact]
    public void TheScriptActionReachesTheHostWithItsValue()
    {
        string[] p = ["setUserRadar"];
        (uint, uint)[] code =
        [
            (6, 1), (1, 0), (4, 2), (13, 0), (7, 1),   // setUserRadar(0)
            (10, 6),
            (1, 1), (4, 2), (13, 0), (7, 1),           // setUserRadar(1)
            (10, 10),
        ];
        var host = new RadarHost();
        var vm = new FsmVm(new FsmProgram([.. p], [], [], [], [(0, 0, [])], [], code, true), host, new CrtRand());
        vm.Tick();
        vm.Tick();
        Assert.Equal(["radar 0", "radar 1"], host.Calls);
        Assert.DoesNotContain(host.Logs, l => l.Contains("not yet handled"));
    }

    [Fact]
    public void TheFirstToggleBlanksThePlayersRadarForGood()
    {
        var (c, me, other, ai) = World();
        c.RadarTarget[me] = other;
        c.SetUserRadar(1);                       // the same as the cache: nothing
        Assert.False(c.UserRadarDark);
        Assert.Equal(2, c.Contacts(me).Count);
        c.SetUserRadar(0);
        Assert.True(c.UserRadarDark);
        Assert.Empty(c.Contacts(me));
        Assert.Null(c.RadarTarget[me]);
        Assert.Equal(2, c.Contacts(ai).Count);   // only the player's radar
        c.SetUserRadar(1);                       // re-enabling clears only the disable bit: still blank
        Assert.True(c.UserRadarDark);
        Assert.Empty(c.Contacts(me));
        Assert.Equal(1, c.UserRadarSetting);
    }

    [Fact]
    public void TheRadarsStateSurvivesASnapshot()
    {
        var (c, _, _, _) = World();
        c.SetUserRadar(0);
        c.RadarTarget.Clear();                   // no car references across the two worlds
        var (d, _, _, _) = World();
        var w = new SaveWriter(new SaveRefs());
        c.Save(w);
        d.Load(new SaveReader(w.ToArray(), new SaveRefs()));
        Assert.True(d.UserRadarDark);
        Assert.Equal(0, d.UserRadarSetting);
    }
}
