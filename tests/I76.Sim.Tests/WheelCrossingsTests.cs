using System.Numerics;
using I76.Sim.Math;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>The bass shaker's wheel crossings: each corner wheel's ground, and a knock as one crosses onto or off a road or a deck.</summary>
public class WheelCrossingsTests
{
    /// <summary>Sand (1) west of x = 20, asphalt (2) from 20 to 40, a dirt road (3) past 40; a deck 1.5 over the ground from z = 100 to 120.</summary>
    private sealed class Strip : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (z >= 100 && z < 120 && y > 1 ? 1.5f : 0f, Vector3.UnitY);
        public int SurfaceId(double x, double z) => x < 20 ? 1 : x < 40 ? 2 : 3;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
        public float BareHeight(double x, double z) => 0f;
    }

    /// <summary>A car facing +x (its wheels 1.5 ahead and behind, 0.8 to each side), at <paramref name="x"/>, moving at <paramref name="speed"/>.</summary>
    private static Vehicle Car(double x, double z, float speed, float y = 0.5f)
    {
        var v = new Vehicle { Pos = new Double3(x, y, z), Rot = new Rows(-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX), Vel = new Vector3(speed, 0, 0) };
        (float side, float along)[] at = [(0.8f, 1.5f), (-0.8f, 1.5f), (0.8f, -1.5f), (-0.8f, -1.5f)];
        int i = 0;
        foreach (var w in v.Wheels) { w.Offset = new Vector3(at[i].side, 0f, at[i].along); w.InitSuspension(); i++; }
        return v;
    }

    [Fact]
    public void The_surface_ids_sort_into_roads_and_the_ground()
    {
        Assert.Equal(GroundKind.OffRoad, WheelCrossings.OfSurface(0));
        Assert.Equal(GroundKind.OffRoad, WheelCrossings.OfSurface(1));
        Assert.Equal(GroundKind.Paved, WheelCrossings.OfSurface(2));
        Assert.Equal(GroundKind.DirtRoad, WheelCrossings.OfSurface(3));
        Assert.Equal(GroundKind.DirtRoad, WheelCrossings.OfSurface(4));
        Assert.Equal(GroundKind.OffRoad, WheelCrossings.OfSurface(5));
        Assert.Equal(GroundKind.Paved, WheelCrossings.OfSurface(6));
        Assert.Equal(GroundKind.DirtRoad, WheelCrossings.OfSurface(7));
        var g = new Strip();
        Assert.Equal(GroundKind.Bridge, WheelCrossings.At(g, 10, 1.6, 110));
        Assert.Equal(GroundKind.OffRoad, WheelCrossings.At(g, 10, 0.1, 50));
    }

    [Fact]
    public void Strengths_are_symmetric_and_strongest_onto_asphalt_or_a_deck()
    {
        foreach (var a in Enum.GetValues<GroundKind>())
            foreach (var b in Enum.GetValues<GroundKind>())
                Assert.Equal(WheelCrossings.Strength(a, b), WheelCrossings.Strength(b, a));
        Assert.Equal(1f, WheelCrossings.Strength(GroundKind.OffRoad, GroundKind.Paved));
        Assert.Equal(1f, WheelCrossings.Strength(GroundKind.Paved, GroundKind.Bridge));
        Assert.Equal(0.5f, WheelCrossings.Strength(GroundKind.OffRoad, GroundKind.DirtRoad));
        Assert.Equal(0.35f, WheelCrossings.Strength(GroundKind.Paved, GroundKind.DirtRoad));
        Assert.Equal(0f, WheelCrossings.Strength(GroundKind.Paved, GroundKind.Paved));
        Assert.Equal(0f, WheelCrossings.Strength(GroundKind.None, GroundKind.Paved));
    }

    [Fact]
    public void Each_axle_knocks_as_it_crosses_onto_the_road_and_off_it()
    {
        var g = new Strip();
        var w = new WheelCrossings();
        var knocks = new List<(double X, float S)>();
        var into = new List<float>();
        // from the sand across the asphalt onto the dirt road, 15 m/s, 20 Hz ticks
        const float dt = 102f / 2048f;
        for (double x = 5; x < 50; x += 15 * dt)
        {
            into.Clear();
            w.Step(g, Car(x, 50, 15), dt, into);
            foreach (var s in into) knocks.Add((x, s));
        }
        // front pair onto the asphalt (car at 18.5), rear pair (21.5), front pair onto the dirt road (38.5), rear (41.5)
        Assert.Equal(8, knocks.Count);
        Assert.All(knocks.Take(4), k => Assert.Equal(1f, k.S));
        Assert.All(knocks.Skip(4), k => Assert.Equal(0.35f, k.S));
        Assert.InRange(knocks[0].X, 18.5, 19.3);
        Assert.InRange(knocks[2].X, 21.5, 22.3);
        Assert.InRange(knocks[4].X, 38.5, 39.3);
        Assert.InRange(knocks[6].X, 41.5, 42.3);
        Assert.Equal([GroundKind.DirtRoad, GroundKind.DirtRoad, GroundKind.DirtRoad, GroundKind.DirtRoad], w.Kinds);
    }

    [Fact]
    public void A_deck_knocks_a_first_look_and_creeping_do_not_and_the_air_holds()
    {
        var g = new Strip();
        var into = new List<float>();
        // the first look at the wheels knocks nothing, wherever they are
        var w = new WheelCrossings();
        w.Step(g, Car(30, 50, 15), 0.05f, into);
        Assert.Empty(into);
        // up onto a deck at 15 m/s: the car's wheels at 1.6 over z = 110 (the deck's), from the sand beside it
        w = new WheelCrossings();
        w.Step(g, Car(10, 90, 15), 0.05f, into);
        w.Step(g, Car(10, 110, 15, 2.1f), 0.05f, into);
        Assert.Equal([1f, 1f, 1f, 1f], into);
        Assert.All(w.Kinds, k => Assert.Equal(GroundKind.Bridge, k));
        // creeping onto the road: the ground changes, no knock
        into.Clear();
        w = new WheelCrossings();
        w.Step(g, Car(10, 50, 1), 0.05f, into);
        w.Step(g, Car(30, 50, 1), 0.05f, into);
        Assert.Empty(into);
        Assert.All(w.Kinds, k => Assert.Equal(GroundKind.Paved, k));
        // in the air the wheels keep their ground: landing on the road from the sand is the springs' jolt, not a knock
        w = new WheelCrossings();
        w.Step(g, Car(10, 50, 15), 0.05f, into);
        var flying = Car(30, 50, 15);
        flying.Flags |= VehicleFlags.Airborne;
        w.Step(g, flying, 0.05f, into);
        Assert.All(w.Kinds, k => Assert.Equal(GroundKind.OffRoad, k));
    }

    [Fact]
    public void A_wheel_along_the_edge_knocks_once_in_its_hold_off()
    {
        var g = new Strip();
        var w = new WheelCrossings();
        var into = new List<float>();
        w.Step(g, Car(10, 50, 15), 0.05f, into);
        // the car jitters across x = 20 every tick: the front wheels flip each time, but knock once per hold-off
        for (int i = 0; i < 10; i++) w.Step(g, Car(i % 2 == 0 ? 19 : 18, 50, 15), 0.05f, into);
        Assert.InRange(into.Count, 2 * 2, 2 * (int)MathF.Ceiling(10 * 0.05f / WheelCrossings.HoldOff));
        w.Reset();
        Assert.All(w.Kinds, k => Assert.Equal(GroundKind.None, k));
    }
}
