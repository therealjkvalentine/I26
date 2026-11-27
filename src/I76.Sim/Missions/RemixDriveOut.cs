using I76.Formats.Assembly;
using I76.Formats.Vfs;
using I76.Sim.Session;
using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>
/// The drive-out test of a place a job starts the player inside or sets a pickup in (docs/remix-missions.md "Places"): the built
/// mission loaded, the player's car where the description puts it, then driven at full throttle, steered toward the place's way
/// out (a point outside the building), for some seconds. The place passes when the car has come out (within reach of the way
/// out, or that far from where it started), stayed on its wheels and on the ground (never lifted onto a wall), and was not held
/// in place by a collider (it moved). A tool, not part of the simulation: it runs a session it made itself.
/// </summary>
public static class RemixDriveOut
{
    public sealed record Result(bool Out, double Moved, double ToWayOut, bool Flipped, double Rise, string Track)
    {
        public bool Passed => Out && !Flipped && Rise < 3.5 && ToWayOut <= 14;
        public override string ToString() =>
            $"{(Passed ? "out" : "STUCK")}: moved {Moved:0} m, {ToWayOut:0} m from the way out, {(Flipped ? "on its roof, " : "")}highest {Rise:0.0} m over the ground; {Track}";
    }

    /// <summary>Drives the built mission's player car from its start toward the way out (x, z) for the seconds given.</summary>
    public static Result Run(AssetVfs vfs, ModelAssembler asm, string mission, double[] wayOut, float seconds = 14, string car = "vppirna1.vcf",
        double[]? from = null, double heading = 0)
    {
        var path = vfs.Install.FindMission(mission) ?? throw new FileNotFoundException(mission);
        var setup = MissionSetup.Build(vfs, new MissionWorld(vfs, asm, path), asm, car, rand: new Rng.CrtRand(1));
        var s = new SimSession(setup);
        var me = setup.World.Player ?? throw new InvalidOperationException("no player");
        s.AddDriver(me);
        if (from is [var fx, var fz])
        {
            // put where the place is, facing its way out, on the ground
            me.Pos = new Math.Double3(fx, setup.Ground.BareHeight((float)fx, (float)fz) + 1, fz);
            me.Rot = Math.Rows.AboutY((float)(heading * System.Math.PI / 180));
            me.Vel = default;
        }
        double x0 = me.Pos.X, z0 = me.Pos.Z, rise = 0;
        bool flipped = false;
        var track = new List<string>();
        int ticks = (int)(seconds / s.TickSeconds);
        for (int t = 0; t < ticks; t++)
        {
            // steer toward the way out: the bearing to it against the car's facing
            var f = me.Rot.Forward;
            double want = I76.Formats.DetMath.Atan2(wayOut[0] - me.Pos.X, wayOut[1] - me.Pos.Z), have = I76.Formats.DetMath.Atan2(f.X, f.Z);
            double err = want - have;
            while (err > System.Math.PI) err -= 2 * System.Math.PI;
            while (err < -System.Math.PI) err += 2 * System.Math.PI;
            // the game's +X is to the car's right: a positive error (the way out clockwise of the facing) steers right
            short steer = (short)System.Math.Clamp(-err * 3 * short.MaxValue, -short.MaxValue, short.MaxValue);
            var input = new CarInput { Buttons = CarButtons.Analog, Throttle = t < (int)(1 / s.TickSeconds) ? (short)(short.MaxValue / 2) : short.MaxValue, Steer = steer };
            s.Tick([input]);
            s.Combat?.Events.Clear();
            (s.Combat?.Damage as Damage.DamageModel)?.Events.Clear();
            if (t > (int)(1 / s.TickSeconds))
            {
                double g = setup.Ground.BareHeight((float)me.Pos.X, (float)me.Pos.Z);
                rise = System.Math.Max(rise, me.Pos.Y - g);
                if (me.Rot.Up.Y < 0.3f) flipped = true;
            }
            if (t % (int)(2 / s.TickSeconds) == 0) track.Add($"({me.Pos.X:0},{me.Pos.Z:0})");
            if (Planar(me, wayOut) < 12) break;
        }
        double moved = System.Math.Sqrt((me.Pos.X - x0) * (me.Pos.X - x0) + (me.Pos.Z - z0) * (me.Pos.Z - z0));
        double toOut = Planar(me, wayOut);
        double need = System.Math.Sqrt((wayOut[0] - x0) * (wayOut[0] - x0) + (wayOut[1] - z0) * (wayOut[1] - z0));
        return new Result(toOut <= 14, moved, toOut, flipped, rise, string.Join(" ", track));
    }

    /// <summary>
    /// Every place of a built job inside or under a structure (its start, its pickup in a barn), driven out of: a problem line for
    /// each that the car cannot leave, or leaves on its roof or over a wall.
    /// </summary>
    public static List<string> Check(AssetVfs vfs, ModelAssembler asm, I76.Formats.Remix.RemixSpec spec)
    {
        var bad = new List<string>();
        foreach (var p in spec.Inside)
        {
            if (p.Out is not [_, _] || p.At is not [_, _]) { bad.Add($"inside {p.Name}: no way out given"); continue; }
            var r = Run(vfs, asm, spec.Id, p.Out, 14, from: p.At, heading: p.Heading);
            if (!r.Passed) bad.Add($"inside {p.Name} ({p.Called}): the car can't drive out: {r}");
        }
        return bad;
    }

    /// <summary>
    /// A node table's place inside a structure driven out of: a job of its own (the player started there facing its way out, its
    /// doors off and wall sections out, its friendly car parked) built into a scratch folder and driven.
    /// </summary>
    public static Result Node(GameInstall install, I76.Formats.Remix.RemixNodes t, I76.Formats.Remix.PointNode n)
    {
        var spec = new I76.Formats.Remix.RemixSpec { Id = "xinside", Title = "INSIDE", Base = t.Base, Start = new I76.Formats.Remix.PoseSpec { At = n.At, Heading = n.Heading ?? 0, Out = n.Out } };
        spec.Places["home"] = [n.Out ?? n.At];
        spec.Objective.Template = "deliver";
        spec.Objective.Home = new I76.Formats.Remix.ZoneSpec { Place = "home", Radius = 80 };
        spec.Open.AddRange(n.Open);
        spec.Remove.AddRange(n.Remove);
        if (n.Friend is { } f) spec.Cars["friend"] = new I76.Formats.Remix.CarSpec { Vcf = f.Vcf, At = f.At, Heading = f.Heading, Team = 1 };
        spec.Text.Objectives.Add("Drive out.");
        spec.Text.Failures.AddRange(["dead", "left"]);
        string dir = Path.Combine(Path.GetTempPath(), "i26-remix-inside-" + Environment.ProcessId);
        var keep = GameInstall.UserMissionDir;
        GameInstall.UserMissionDir = dir;
        try
        {
            I76.Formats.Remix.RemixBuilder.Build(install, spec).WriteTo(dir);
            var vfs = new AssetVfs(install);
            return Run(vfs, new ModelAssembler(vfs), spec.Id, n.Out ?? n.At, 14);
        }
        finally { GameInstall.UserMissionDir = keep; }
    }

    private static double Planar(Vehicle v, double[] p) => System.Math.Sqrt((v.Pos.X - p[0]) * (v.Pos.X - p[0]) + (v.Pos.Z - p[1]) * (v.Pos.Z - p[1]));
}
