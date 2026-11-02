using System.Numerics;
using I76.Formats.Bwd2;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The live-round pool per ordnance id: 160 rounds, 40 in a network game for ids 0xe..0x11 (oil keeps 160); a spawn
/// past the cap removes the oldest of that id. Measured in the original: mines plateau at the cap online, fire drops
/// at the cap offline.
/// </summary>
public class OrdnanceCapTests
{
    private const float Dt = 102 / 2048f;

    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static readonly Transform Ident = new([1, 0, 0, 0, 1, 0, 0, 0, 1], [0, 0, 0]);

    /// <summary>One dropper on a still car, fired until <paramref name="drops"/> rounds have left it (inside the dropper's own 2 s); the rounds live 1000 s.</summary>
    private static (Combat C, Round First) Lay(uint kind, bool network, int drops, float speed = 0, float rate = 60, bool unlimited = false)
    {
        var world = new SimWorld(new Flat(), new CrtRand(3));
        var car = new Vehicle { Speed = speed };
        world.Vehicles.Add(car);
        var def = Armory.HandgunDef(1) with
        {
            Class = 6, Tier = 1, Kind = kind, Rate = rate, Burst = 1, Delay = 0, Speed = 0, Life = 1000, Spread = 0, Ammo = 100000,
            Ordnance = new OrdnanceDef(kind, 50, 1, 0, 0, ["", "", "", ""], ["", "", "", ""]),
        };
        var w = new Weapon
        {
            Def = def, Hardpoint = new Hardpoint("hp", 0, 1, 4, Ident), Style = 4, MountKind = 2, BaseRot = Rows.Identity,
            Hp = 45, MaxHp = 45, Damage = 50, Ammo = 100000,
        };
        var a = new Armory();
        a.Weapons.Add(w); a.LoadOrder.Add(w); a.Selected = 0;
        car.Armory = a;
        var c = new Combat(world, _ => null) { NetSession = network, UnlimitedOrdnance = unlimited };
        Round? first = null;
        int n = 0;
        for (int f = 0; n < drops && f < 100000; f++)
        {
            w.Trigger = true;
            c.Frame(Dt);
            n += c.Events.OfType<ShotFired>().Count();
            c.Events.Clear();
            first ??= c.Rounds.FirstOrDefault();
        }
        Assert.Equal(drops, n);
        return (c, first!);
    }

    [Theory]
    [InlineData(0xfu)]
    [InlineData(0x11u)]
    public void OfflineFortyFiveDropsAllLive(uint kind)
    {
        var (c, first) = Lay(kind, false, 45);
        Assert.Equal(45, c.Rounds.Count(r => r.Kind == kind));
        Assert.Contains(first, c.Rounds);
    }

    [Theory]
    [InlineData(0xfu)]
    [InlineData(0x11u)]
    public void OnlineTheFortyFirstDropRemovesTheOldest(uint kind)
    {
        var (c, first) = Lay(kind, true, 45);
        Assert.Equal(40, c.Rounds.Count(r => r.Kind == kind));
        Assert.DoesNotContain(first, c.Rounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OilKeepsAHundredAndSixtyOnlineAndOff(bool network)
    {
        var (c, first) = Lay(0xc, network, 200, speed: 30);
        Assert.Equal(160, c.Rounds.Count(r => r.Kind == 0xc));
        Assert.DoesNotContain(first, c.Rounds);
    }

    [Fact]
    public void FireDropsStopAtAHundredAndSixtyOffline()
    {
        var (c, _) = Lay(0x11, false, 200, speed: 30);
        Assert.Equal(160, c.Rounds.Count(r => r.Kind == 0x11));
    }

    /// <summary>Original Recipe's Live Ordnance, Unlimited: no cap offline; a network game keeps the original's.</summary>
    [Fact]
    public void UnlimitedKeepsEveryDropOffline()
    {
        var (c, first) = Lay(0x11, false, 200, speed: 30, unlimited: true);
        Assert.Equal(200, c.Rounds.Count(r => r.Kind == 0x11));
        Assert.Contains(first, c.Rounds);
    }

    [Fact]
    public void UnlimitedIsNotForANetworkGame()
    {
        var (c, first) = Lay(0xf, true, 45, unlimited: true);
        Assert.Equal(40, c.Rounds.Count(r => r.Kind == 0xf));
        Assert.DoesNotContain(first, c.Rounds);
    }
}
