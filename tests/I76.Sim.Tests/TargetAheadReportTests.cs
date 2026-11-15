using System.Numerics;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// The I76_TARGET_LOG line for a Q (target ahead) press (<see cref="Combat.FrontalReport"/>): what was picked, or why nothing, and what
/// the arc made of each vehicle ahead. Q's own rules are TargetingTests'.
/// </summary>
public class TargetAheadReportTests(ITestOutputHelper output)
{
    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static (Combat C, Vehicle Me) Setup(params (double X, double Z, int Team)[] others)
    {
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var me = new Vehicle { NetId = 1 };
        world.Vehicles.Add(me);
        world.Player = me;
        var teams = new Dictionary<Vehicle, int> { [me] = 1 };
        int id = 2;
        foreach (var (x, z, t) in others)
        {
            var v = new Vehicle { Pos = new Double3(x, 0, z), NetId = id++ };
            world.Vehicles.Add(v);
            teams[v] = t;
        }
        var c = new Combat(world, _ => null);
        c.Team = v => teams.GetValueOrDefault(v);
        return (c, me);
    }

    private string Report(Combat c, Vehicle me)
    {
        string r = c.FrontalReport(me);
        output.WriteLine(r);
        return r;
    }

    [Fact]
    public void AHelicopterAheadIsPassedOverAndSaysSo()
    {
        var (c, me) = Setup((0, 80, 2));
        c.World.Vehicles[1].Heli = new HeliState();
        string r = Report(c, me);
        Assert.StartsWith("nothing in the 15 degree arc", r);
        Assert.Contains("helicopter#2 team 2 80 m 0 across +1 up: passed over: a helicopter", r);
    }

    [Fact]
    public void ADeadCarNearestInTheArcHoldsThePickAndTheLiveOneBehindIsNamed()
    {
        var (c, me) = Setup((0, 40, 2), (2, 120, 2));
        c.World.Vehicles[1].Flags |= VehicleFlags.Dead;
        Assert.Null(c.FrontalTarget(me));
        string r = Report(c, me);
        Assert.StartsWith("nothing: the arc's nearest, car#2, is not a radar contact (dead)", r);
        Assert.Contains("car#3 team 2 120 m +1 across 0 up: behind the arc's pick", r);
    }

    [Fact]
    public void AnEngineOffCarAndOneOutsideTheConeAreTold()
    {
        var (c, me) = Setup((0, 50, 2), (40, 100, 2));
        c.World.Vehicles[1].Flags &= ~VehicleFlags.EngineOn;
        string r = Report(c, me);
        Assert.StartsWith("nothing: the arc's nearest, car#2, is not a radar contact (engine off)", r);
        Assert.Contains("car#3 team 2 108 m +22 across 0 up: outside the 15 degrees", r);
    }

    [Fact]
    public void APickOnTheOwnSideIsMarked()
    {
        var (c, me) = Setup((0, 60, 1));
        string r = Report(c, me);
        Assert.StartsWith("picked car#2 (on the presser's own side)", r);
        Assert.EndsWith("car#2 team 1 60 m 0 across +1 up: picked", r);
    }

    [Fact]
    public void NothingAheadAtAll()
    {
        var (c, me) = Setup((0, -50, 2));
        Assert.Equal("nothing in the 15 degree arc; within 45 degrees ahead: none", Report(c, me));
    }
}
