using System.Numerics;
using I76.Sim.Collision;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// Rough-terrain damage reads the surface type probed on the previous substep (the original's order: the damage call
/// runs before that substep's probe writes the new one), so a car crossing onto rough ground takes its first substep
/// there at the old surface's rate.
/// </summary>
public class RoughSurfaceTests
{
    /// <summary>Flat, surface 0 for z &lt; 0 and 1 beyond.</summary>
    private sealed class Border : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => z < 0 ? 0 : 1;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private sealed class Recorder : IStepDamage
    {
        public readonly List<(int Passed, int Probed)> Calls = [];
        public void Timer(Vehicle v, float dt) { }
        public void TerrainImpact(Vehicle v, Vector3 relVel, Vector3 n, Vector3 cLocal) { }
        public void Landing(Vehicle v, Vector3 impactVel, Vector3 n) { }
        public void FallKill(Vehicle v) { }
        public void Rough(Vehicle v, int surface, float dt) => Calls.Add((surface, v.Surface));
        public void StaticContact(Vehicle v, StaticObject o, Contact rec) { }
    }

    [Fact]
    public void TheFirstSubstepOnNewGroundTakesTheOldSurface()
    {
        var step = new VehicleStep(new Border(), new CrtRand()) { Damage = new Recorder() };
        var rec = (Recorder)step.Damage!;
        var v = new Vehicle { Pos = new Double3(0, 0, -1.5), Vel = new Vector3(0, 0, 20), Speed = 20, Surface = 0 };
        foreach (var (w, x, z) in new[] { (v.FrontRight, 0.8f, 1.4f), (v.FrontLeft, -0.8f, 1.4f), (v.RearRight, 0.8f, -1.3f), (v.RearLeft, -0.8f, -1.3f) })
        {
            w.Grip = 0.5f; w.Rolling = 1f; w.Offset = new Vector3(x, 0.33f, z);
            w.InitSuspension();
        }
        v.P.Wheelbase = 2.7f; v.P.Track = 1.6f; v.P.RideOffset = 1.4f;
        for (int i = 0; i < 6; i++) step.Frame(v, 102 / 2048f);
        Assert.Contains(rec.Calls, c => c.Probed == 0);
        int first = rec.Calls.FindIndex(c => c.Probed == 1);
        Assert.True(first > 0, "the car never reached surface 1");
        Assert.Equal((0, 1), rec.Calls[first]);
        for (int i = 1; i < rec.Calls.Count; i++) Assert.Equal(rec.Calls[i - 1].Probed, rec.Calls[i].Passed);
    }
}
