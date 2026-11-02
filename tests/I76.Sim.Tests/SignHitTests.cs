using System.Numerics;
using I76.Formats.Bwd2;
using I76.Sim.Collision;
using I76.Sim.Damage;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// Rounds against type-4 placements (road signs, posts): the round is stopped by the sign's sphere (0.75 x its radius)
/// and plays its impact there; a bullet leaves the sign standing, an explosive round knocks it off; a knocked-off sign
/// stops nothing. Before, rounds went through them.
/// </summary>
public class SignHitTests
{
    private const float Dt = 102 / 2048f;

    private sealed class Flat(StaticWorld w) : IGround, IHasStatics
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
        public StaticWorld World { get; } = w;
    }

    /// <summary>A sign 20 ahead of a round flying +z at 1 up, and a car 10 behind the sign.</summary>
    private static (Combat C, DamageModel D, StaticObject Sign, Vehicle Behind, Round R) Rig(uint kind, uint mask, float offset = 0)
    {
        var statics = new StaticWorld();
        var sign = new StaticObject { Name = "ssign_1", Type = 4, Centre = new Vector3(offset, 1f, 20), Radius = 0.6f, BoundRadius = 1f, SphereRadius = 0.75f };
        statics.Objects.Add(sign);
        var world = new SimWorld(new Flat(statics), new CrtRand(1));
        var shooter = new Vehicle { Pos = new Double3(100, 0, -100) };
        var behind = new Vehicle { Pos = new Double3(0, 0, 30) };
        world.Vehicles.Add(shooter); world.Vehicles.Add(behind);
        var combat = new Combat(world, _ => null);
        var dm = new DamageModel(world, combat);
        var def = Armory.HandgunDef(1) with
        {
            Kind = kind, Speed = 100, Life = 5, Spread = 0,
            Ordnance = new OrdnanceDef(kind, 100, 0, mask, 0, ["", "", "", ""], ["", "", "", ""]),
        };
        var r = new Round
        {
            Kind = kind, Shooter = shooter, Def = def, Ord = def.Ordnance!, Pos = new(0, 1f, 0), PrevPos = new(0, 1f, 0),
            Rot = Rows.Identity, LocalVel = new(0, 0, 100), Life = 5, Damage = 50, Travelled = 100,
        };
        combat.Rounds.Add(r);
        return (combat, dm, sign, behind, r);
    }

    private static List<Impact> Fly(Combat c, int frames = 10)
    {
        var hits = new List<Impact>();
        for (int i = 0; i < frames && c.Rounds.Count > 0; i++)
        {
            c.Frame(Dt);
            hits.AddRange(c.Events.OfType<Impact>());
            c.Events.Clear();
        }
        return hits;
    }

    [Fact]
    public void ABulletStopsOnTheSignWhichStaysUp()
    {
        var (c, dm, sign, behind, _) = Rig(1, 1);
        var hit = Assert.Single(Fly(c));
        Assert.Same(sign, hit.Victim);
        Assert.InRange(hit.Hit.Z, 19.2f, 19.3f);                  // on the 0.75 sphere's near side
        Assert.Empty(c.Rounds);
        Assert.False(sign.Destroyed);
        Assert.DoesNotContain(dm.Events, e => e is SignKnocked);
        Assert.NotSame(behind, hit.Victim);
    }

    [Fact]
    public void ARocketKnocksTheSignOffAndThenPassesWhereItStood()
    {
        var (c, dm, sign, _, _) = Rig(2, 2);
        var hit = Assert.Single(Fly(c));
        Assert.Same(sign, hit.Victim);
        Assert.True(sign.Destroyed);
        Assert.Contains(dm.Events, e => e is SignKnocked k && k.Object == sign);
        // the next round goes through to the car behind it
        var (c2, _, sign2, behind2, _) = Rig(1, 1);
        sign2.Knock();
        var through = Assert.Single(Fly(c2));
        Assert.Same(behind2, through.Victim);
    }

    [Fact]
    public void ARoundPassingBesideTheSphereMissesIt()
    {
        var (c, _, _, behind, _) = Rig(1, 1, offset: 0.8f);       // the round's line 0.8 from its centre, outside 0.75
        var hit = Assert.Single(Fly(c));
        Assert.Same(behind, hit.Victim);
    }

    [Fact]
    public void TheSphereTestEntersFromOutsideOnly()
    {
        var c = new Vector3(0, 0, 10);
        var h = StaticWorld.RaySphere(c, 1f, Vector3.Zero, new Vector3(0, 0, 20), 1f);
        Assert.NotNull(h);
        Assert.Equal(0.45f, h!.Value.T, 4);
        Assert.Equal(-Vector3.UnitZ, h.Value.N);
        Assert.Null(StaticWorld.RaySphere(c, 1f, c, new Vector3(0, 0, 20), 1f));          // from inside
        Assert.Null(StaticWorld.RaySphere(c, 1f, Vector3.Zero, new Vector3(0, 0, 8), 1f)); // short of it
        Assert.Null(StaticWorld.RaySphere(c, 1f, new Vector3(0, 0, 12), new Vector3(0, 0, 20), 1f)); // past it
    }
}
