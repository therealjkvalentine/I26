using System.Numerics;
using System.Runtime.CompilerServices;
using I76.Formats.Exe;
using I76.Sim.Fsm;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// #191, the Nitro Pack's Never Get Out: the intro hides the player's car (engine off, lever P); skipped, it starts the car and
/// orders a goto on it after the camera is let go. The original runs that goto, whose start puts the lever in D; here the
/// player's orders outside a cut-scene are dropped, and the car was handed over in P.
/// </summary>
public class HandOffLeverTests
{
    private sealed class Flat : I76.Sim.Terrain.IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public I76.Sim.Terrain.Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static (ScriptHost Host, Entity Player) World()
    {
        var world = new SimWorld(new Flat(), new I76.Sim.Rng.CrtRand(1));
        var v = new Vehicle { Pos = new Double3(0, 0, 0) };
        world.Vehicles.Add(v);
        world.Player = v;
        var player = new Entity("skeeter", [], 1, 0) { Vehicle = v };
        PtEntry[] blank = [.. Enumerable.Range(0, 34).Select(i => new PtEntry(i, $"state{i}", 0, 0, 0, [], [], []))];
        var host = new ScriptHost(world, [player], (Objectives)RuntimeHelpers.GetUninitializedObject(typeof(Objectives)), _ => null) { Tactics = blank };
        return (host, player);
    }

    private static readonly IReadOnlyList<Vector3> Path = [new(0, 0, 100)];

    [Theory]
    [InlineData(AiOrder.Goto)]
    [InlineData(AiOrder.Evade)]
    [InlineData(AiOrder.Race)]
    [InlineData(AiOrder.Follow)]
    public void ADriveOrderAfterTheCameraIsLetGoTakesTheCarOutOfPark(AiOrder order)
    {
        var (host, player) = World();
        player.Vehicle!.DriveState = 0;   // as the intro's hide left it
        Assert.False(host.View.Active);
        host.Order(player, null, order, Path, 25);
        Assert.Equal(3, player.Vehicle.DriveState);
        Assert.Equal(1f, player.Vehicle.Gear);
        Assert.Null(player.Order);       // the order itself is still the keys'
    }

    [Fact]
    public void ALeverThePlayerChoseIsKept()
    {
        var (host, player) = World();
        player.Vehicle!.DriveState = 1; player.Vehicle.Gear = -1f;   // reverse, by the player's Tab
        host.Order(player, null, AiOrder.Goto, Path, 25);
        Assert.Equal(1, player.Vehicle.DriveState);
        Assert.Equal(-1f, player.Vehicle.Gear);
    }

    [Theory]
    [InlineData(AiOrder.Sit)]
    [InlineData(AiOrder.Guard)]
    [InlineData(AiOrder.Hide)]
    public void AnOrderWithNoStartLeavesPark(AiOrder order)
    {
        var (host, player) = World();
        player.Vehicle!.DriveState = 0;   // sit and guard have no start in the original; hide's own sets P
        host.Order(player, null, order, null, 0);
        Assert.Equal(0, player.Vehicle.DriveState);
    }
}
