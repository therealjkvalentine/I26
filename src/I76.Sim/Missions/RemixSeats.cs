using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>
/// Where a co-op crew is put down at a job's start (docs/remix-missions.md "Roguetrip in co-op"): a job started at a table's place (a
/// place of note, inside or under a structure or in the open) built with a crew of N, each seat's spot checked: not in a structure's solid
/// parts, not on another car, outside every exit's warning square, and whether it stands under the structure the place is in.
/// </summary>
public static class RemixSeats
{
    /// <summary>One seat's spot: where, whether it is under the place's structure, and what is wrong with it (empty: nothing).</summary>
    public sealed record Seat(int Index, Vector2 At, bool Under, float ExitSquare, string Problem);

    /// <summary>The crew of <paramref name="seats"/> at the start of a job begun at node <paramref name="n"/> of table <paramref name="t"/>.</summary>
    public static List<Seat> At(GameInstall install, RemixNodes t, PointNode n, int seats)
    {
        var spec = new RemixSpec { Id = "xseats", Title = "SEATS", Base = t.Base, Start = new PoseSpec { At = n.At, Heading = n.Heading ?? 0, Out = n.Out } };
        spec.Places["home"] = [n.Out ?? n.At];
        spec.Objective.Template = "deliver";
        spec.Objective.Home = new ZoneSpec { Place = "home", Radius = 80 };
        spec.Open.AddRange(n.Open);
        spec.Remove.AddRange(n.Remove);
        if (n.Friend is { } f) spec.Cars["friend"] = new CarSpec { Vcf = f.Vcf, At = f.At, Heading = f.Heading, Team = 1 };
        spec.Text.Objectives.Add("Drive out.");
        spec.Text.Failures.AddRange(["dead", "left"]);
        string dir = Path.Combine(Path.GetTempPath(), "i26-remix-seats-" + Environment.ProcessId);
        var keep = GameInstall.UserMissionDir;
        GameInstall.UserMissionDir = dir;
        try
        {
            RemixBuilder.Build(install, spec).WriteTo(dir);
            var vfs = new AssetVfs(install);
            var asm = new ModelAssembler(vfs);
            var path = install.FindMission(spec.Id)!;
            var coop = new CoopSetup(Enumerable.Repeat<string?>(null, seats).ToList()) { RespawnDelay = 1e6f };
            var setup = MissionSetup.Build(vfs, new MissionWorld(vfs, asm, path), asm, "vppirna1.vcf", rand: new Rng.CrtRand(1), coop: coop);
            var exits = RemixBounds.Exits(path, spec);
            var under = n.Inside is { } ins ? setup.Statics.Objects.Where(o => o.Name.Equals(ins.Object, StringComparison.OrdinalIgnoreCase)).Skip(ins.Nth).FirstOrDefault() : null;
            var humans = setup.Humans.ToList();
            var starts = humans.Select(h => h.Pos).ToList();
            var s = new Session.SimSession(setup);
            foreach (var h in humans) s.AddDriver(h);
            for (int k = 0; k < s.Rate.TicksFor(1f); k++) s.Tick([.. humans.Select(_ => new Session.CarInput())]);
            var settled = humans.Select(h => (h.Pos, h.Rot.Up.Y)).ToList();
            var o = new List<Seat>();
            for (int i = 0; i < humans.Count; i++)
            {
                var h = humans[i];
                var p = new Vector2((float)starts[i].X, (float)starts[i].Z);
                var why = new List<string>();
                float r = SpawnSpace.Radius(h) - 1f;
                // collision-free: standing still for a second at the start it is not pushed out of a wall or another car, nor tipped over
                var (q, up) = settled[i];
                float moved = Vector2.Distance(p, new Vector2((float)q.X, (float)q.Z));
                if (moved > 1f || System.Math.Abs(q.Y - h.Pos.Y) > 0.8 || up < 0.7f) why.Add($"pushed {moved:0.0} m, height {q.Y - h.Pos.Y:+0.0;-0.0} m, upright {up:0.00} in its first second");
                for (int k = 0; k < i; k++)
                {
                    float d = Vector2.Distance(p, new Vector2((float)starts[k].X, (float)starts[k].Z));
                    if (d < r + SpawnSpace.Radius(humans[k]) - 1f) why.Add($"on seat {k} ({d:0.0} m)");
                }
                foreach (var c in setup.World.Vehicles.Where(c => !humans.Contains(c)))
                {
                    float d = Vector2.Distance(p, new Vector2((float)c.Pos.X, (float)c.Pos.Z));
                    if (d < r + SpawnSpace.Radius(c) - 1f) why.Add($"on a placed car ({d:0.0} m)");
                }
                float ex = RemixBounds.ExitDistance(exits, p);
                if (ex < RemixBounds.Warn) why.Add($"{ex:0} m from an exit: inside its warning");
                o.Add(new Seat(i, p, under is not null && SpawnSpace.Under(under, new Vector3(p.X, (float)h.Pos.Y, p.Y)), ex, string.Join("; ", why)));
            }
            return o;
        }
        finally { GameInstall.UserMissionDir = keep; }
    }
}
