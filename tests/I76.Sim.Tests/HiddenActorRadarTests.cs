using I76.Formats.Assembly;
using I76.Formats.Vfs;
using I76.Sim.Fsm;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Session;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// What the script's hide keeps off the radar. hide switches the engine off and nothing else (no flag that hides the
/// object, nothing the renderer reads), and the original's radar test asks for the engine of a car (object class 1)
/// only: a helicopter or a gun emplacement is listed whenever it is not dead. So a hidden car is off the radar and out
/// of the target keys' reach, and a hidden helicopter within 600 m is on it, drawn and can be shot, in the original
/// too (upstream #223: the Nitro Pack's Break In, whose two Hueys wait hidden over their heliport, about 600 m from
/// Hangar Three, until the player has reached it).
/// </summary>
public class HiddenActorRadarTests
{
    private sealed class Flat : IGround
    {
        public (float Height, System.Numerics.Vector3 Normal) Query(double x, double y, double z) => (0, System.Numerics.Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    [Fact]
    public void TheEngineIsACarsTestOnly()
    {
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var me = new Vehicle { Flags = VehicleFlags.EngineOn };
        var car = new Vehicle { Pos = new Double3(0, 0, 300), Flags = 0 };
        var heli = new Vehicle { Pos = new Double3(0, 0, 300), Flags = 0, ObjectClass = 9, Heli = new HeliState() };
        world.Vehicles.Add(me); world.Vehicles.Add(car); world.Vehicles.Add(heli);
        var c = new Combat(world, _ => null);
        Assert.False(c.IsContact(me, car));
        Assert.True(c.IsContact(me, heli));
        car.Flags |= VehicleFlags.EngineOn;
        Assert.True(c.IsContact(me, car));
    }

    private static string? NitroDir =>
        Environment.GetEnvironmentVariable("I76_NITRO_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    [Fact]
    public void BreakInsHiddenHueysAreOnTheRadarAndItsHiddenGuardsAreNot()
    {
        if (NitroDir is null) return;
        var install = new GameInstall(NitroDir);
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var setup = MissionSetup.Build(vfs, new MissionWorld(vfs, asm, install.Resolve("miss16", "p18.msn")!), asm, rand: new CrtRand(1));
        var s = new SimSession(setup);
        var player = setup.World.Player!;
        s.AddDriver(player);
        // the skip key cuts the opening fly-by: both Hueys go straight to the heliport and hide there
        // the guards hide from the first tick, one of them 430 m from the start: never listed
        var skip = new CarInput { Commands = CarCommands.Skip };
        var hueys = setup.Entities.Where(e => e.Vehicle is { IsHeli: true }).ToList();
        var guards = setup.Entities.Where(e => e.Vehicle is { IsHeli: false } v && v != player).ToList();
        int hiddenNear = 0;
        for (int i = 0; i < (int)(10 / s.TickSeconds); i++)
        {
            s.Tick([skip]);
            setup.Combat.Events.Clear();
            foreach (var g in guards.Where(g => g.Order == AiOrder.Hide))
            {
                var d = g.Vehicle!.Pos.ToVector3() - player.Pos.ToVector3();
                if (MathF.Sqrt(d.X * d.X + d.Z * d.Z) <= Combat.ContactRange) hiddenNear++;
                Assert.False(setup.Combat.IsContact(player, g.Vehicle!));
            }
        }
        Assert.Equal(2, hueys.Count);
        Assert.True(hiddenNear > 0);
        Assert.All(hueys, e => Assert.Equal(AiOrder.Hide, e.Order));
        Assert.All(hueys, e => Assert.Equal(0, (int)(e.Vehicle!.Flags & VehicleFlags.EngineOn)));
        // 300 m short of the heliport: both listed, and T picks one
        player.Pos = new Double3(3400, setup.Ground.BareHeight(3400, 48800), 48800);
        player.Vel = default;
        s.Tick([new CarInput()]);
        Assert.All(hueys, e => Assert.True(setup.Combat.IsContact(player, e.Vehicle!)));
        Assert.Contains(setup.Combat.NearestEnemy(player), hueys.Select(e => e.Vehicle));
    }
}
