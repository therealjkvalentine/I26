using I76.Formats.Assembly;
using I76.Formats.Fsm;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Session;
using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>
/// The headless check of a built remix (docs/remix-missions.md "Checking a build"; i76 remix check, RemixMissionTests): the
/// mission loaded and run as the game runs it, held to what its description and its map say. Every car the template turns on
/// the player exists, is the enemy's (the target keys cycle to it, the radar shows it), stands where the description puts it
/// and on the ground; the cars a mark names (the convoy's lead) start at the mark; once each hostile car is out (woken,
/// teleported in, rolling) it is a radar contact the next-target key reaches from 60 m; the places the map marks are the
/// places the script tests. A tool, not part of the simulation: it reads a session it made itself.
/// </summary>
public static class RemixCheck
{
    /// <summary>Metres a car may stand from where the description parks it, and from the mark that names it.</summary>
    public const float ParkTolerance = 25, MarkTolerance = 60;

    /// <summary>The problems found (empty: the build is sound), each a line naming the car or place.</summary>
    public static List<string> Run(AssetVfs vfs, ModelAssembler asm, string mission, RemixSpec spec, RemixMarks marks, string car = "vppirna1.vcf")
    {
        var bad = new List<string>();
        var path = vfs.Install.FindMission(mission) ?? throw new FileNotFoundException(mission);
        var world = new MissionWorld(vfs, asm, path);
        var setup = MissionSetup.Build(vfs, world, asm, car, rand: new Rng.CrtRand(1));
        if (setup.Script?.Vm is not { } vm) { bad.Add("no script"); return bad; }
        var s = new SimSession(setup);
        if (setup.World.Player is not { } me) { bad.Add("no player"); return bad; }
        s.AddDriver(me);
        var keys = MissionScriptDecompiler.Keys(vm.Actors.Select(a => a.Label));
        Vehicle? CarOf(string name)
        {
            int i = Array.IndexOf(keys, spec.Cars.ContainsKey(name) || spec.Bind.ContainsKey(name) ? RemixBuilder.PathPrefix + name : name);
            return i >= 0 && vm.Actors[i].Handle is Entity { Vehicle: { } v } ? v : null;
        }
        var combat = s.Combat!;
        int myTeam = combat.Team(me);
        var roles = RemixRoles.Hostiles(spec);
        var cars = new Dictionary<string, Vehicle>();
        foreach (var r in roles)
        {
            if (CarOf(r.Car) is not { } v) { bad.Add($"{r.Part} {r.Car}: no car in the built mission"); continue; }
            cars[r.Car] = v;
            int team = combat.Team(v);
            if (team == 0 || team == myTeam) bad.Add($"{r.Part} {r.Car}: on team {team} (the player's is {myTeam}): the target keys skip it");
            if (spec.Cars.TryGetValue(r.Car, out var cs) && cs.At is [var x, var z])
            {
                float d = Planar(v, (float)x, (float)z);
                if (d > ParkTolerance) bad.Add($"{r.Part} {r.Car}: at ({v.Pos.X:0}, {v.Pos.Z:0}), {d:0} m from where it is parked ({x:0}, {z:0})");
            }
        }
        // the cars a mark stands for: the convoy's lead starts at CONVOY
        if (spec.Objective.Convoy is { } cv && cars.TryGetValue(cv.Lead, out var lead))
        {
            var m = marks.Points.FirstOrDefault(p => p.Label.Contains("CONVOY", StringComparison.Ordinal));
            if (m.Label is null) bad.Add($"lead {cv.Lead}: no CONVOY mark on the map");
            else if (Planar(lead, m.At.X, m.At.Y) is var d && d > MarkTolerance) bad.Add($"lead {cv.Lead}: at ({lead.Pos.X:0}, {lead.Pos.Z:0}), {d:0} m from the map's CONVOY mark ({m.At.X:0}, {m.At.Y:0})");
        }
        // the places the map marks are the script's own: each mark of a zone the template tests is that zone's point
        foreach (var (label, zone) in Zones(spec))
        {
            if (!spec.Places.TryGetValue(zone.Place, out var pts) || pts.Count == 0) continue;
            var m = marks.Points.FirstOrDefault(p => p.Label.Split(" / ").Contains(label));
            if (m.Label is null) bad.Add($"{label}: no mark on the map");
            else if (System.Math.Sqrt(System.Math.Pow(m.At.X - pts[0][0], 2) + System.Math.Pow(m.At.Y - pts[0][1], 2)) is var d && d > 160) bad.Add($"{label}: the mark is {d:0} m from the place the script tests");
        }

        // the structures set down before the start: each is a collidable static where the description puts it, none on the start
        foreach (var pr in spec.Props)
        {
            if (pr.At is not [var px, var pz]) continue;
            var st = setup.Statics.Objects.Where(o => o.Name.Equals(pr.Object, StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => (o.Origin.X - px) * (o.Origin.X - px) + (o.Origin.Z - pz) * (o.Origin.Z - pz)).FirstOrDefault();
            if (st is null || System.Math.Abs(st.Origin.X - px) > 3 || System.Math.Abs(st.Origin.Z - pz) > 3) bad.Add($"prop {pr.Object} at ({px:0}, {pz:0}): no structure there in the built mission");
            else if (st.Nodes.Count == 0) bad.Add($"prop {pr.Object} at ({px:0}, {pz:0}): nothing to collide with");
            if (spec.Start?.At is [var sx, var sz] && System.Math.Sqrt((sx - px) * (sx - px) + (sz - pz) * (sz - pz)) < 12) bad.Add($"prop {pr.Object} at ({px:0}, {pz:0}): on the player's start");
        }

        // the places the player must be are clear of the leave-map exits the library still watches (its warning starts at 275 m,
        // it fails the mission at 50 m): a node in a road's end stub failed a job the moment he got there
        var exits = (new I76.Formats.Mission.MissionFile(path).ReadFsmProgram()?.Paths ?? []).Where(p => p.Name.StartsWith("exit", StringComparison.OrdinalIgnoreCase) && !spec.DropExits.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            .SelectMany(p => p.Points.Select(q => (p.Name, new System.Numerics.Vector2(q.X, q.Z)))).ToList();
        foreach (var (label, at) in PlayerPlaces(spec))
            foreach (var (name, ex) in exits)
                if (System.Math.Sqrt(System.Math.Pow(ex.X - at[0], 2) + System.Math.Pow(ex.Y - at[1], 2)) is var d && d < ExitClearance)
                {
                    bad.Add($"{label}: {d:0} m from the exit {name}: the leave-map warning starts at 275 m");
                    break;
                }

        // run it, the player held still out of everybody's way, until each hostile car has been out a while: woken, teleported in,
        // rolling; then each must be a contact the next-target key reaches from 60 m
        var seen = new HashSet<string>();
        var grounded = new HashSet<string>();
        int ticks = (int)(System.Math.Min(180, Horizon(spec)) / s.TickSeconds);
        var doom = new List<(float At, Vehicle Car)>();
        var objectives = setup.Script!.Host.Objectives;
        int walked = 0;
        float stepAt = 0;
        for (int t = 0; t < ticks && s.Script!.Result is null; t++)
        {
            // walk the job as a player would: once an objective step is open (and a few seconds on), teleported to its place
            float now = t * s.TickSeconds;
            int open = Array.FindIndex(objectives.Flags, f => (f & (Objectives.Hidden | Objectives.Succeeded | Objectives.Failed)) == 0) + 1;
            if (open > walked) { walked = open; stepAt = now + 6; }
            if (stepAt > 0 && now >= stepAt) { stepAt = 0; if (StepPlace(spec, walked) is { } sp) Put(setup, me, (float)sp[0], (float)sp[1]); }
            s.Tick([new CarInput()]);
            s.Combat?.Events.Clear();
            (s.Combat?.Damage as Damage.DamageModel)?.Events.Clear();
            // a checked car dies a few seconds later, as the player's kill (the later waves wait for kills)
            for (int k = doom.Count - 1; k >= 0; k--)
                if (now >= doom[k].At) { if (setup.Entities.FirstOrDefault(e => e.Vehicle == doom[k].Car) is { Dead: false } de) { de.KilledByPlayer = true; s.Script!.Host.Destroy(de); } doom.RemoveAt(k); }
            if (t % 10 != 0) continue;
            foreach (var (name, v) in cars)
            {
                if (seen.Contains(name) || !Weapons.Combat.IsLive(v)) continue;
                if (Weapons.Combat.IsDead(v)) { seen.Add(name); continue; }
                if (!grounded.Contains(name))
                {
                    float g = setup.Ground.BareHeight((float)v.Pos.X, (float)v.Pos.Z);
                    if (System.Math.Abs((float)v.Pos.Y - g) > 8) bad.Add($"{Part(roles, name)} {name}: out at height {v.Pos.Y:0.0}, the ground is {g:0.0}");
                    grounded.Add(name);
                }
                // the target keys from 60 m: the next-target cycle must come round to it
                var keep = me.Pos;
                var kv = me.Vel;
                me.Pos = new I76.Sim.Math.Double3(v.Pos.X + 60, v.Pos.Y, v.Pos.Z);
                me.Vel = default;
                bool contact = combat.Contacts(me).Contains(v), reached = false;
                combat.RadarTarget[me] = null;
                for (int k = 0; k < 24 && !reached; k++) reached = (combat.RadarTarget[me] = combat.NextTarget(me)) == v;
                combat.RadarTarget[me] = null;
                me.Pos = keep; me.Vel = kv;
                if (!contact) bad.Add($"{Part(roles, name)} {name}: out, but no radar contact from 60 m");
                else if (!reached) bad.Add($"{Part(roles, name)} {name}: a radar contact the next-target key never selects (team {combat.Team(v)})");
                seen.Add(name);
                doom.Add((now + 3, v));
            }
        }
        // (a mission over early, the player dead or the job done, leaves the rest unseen: only a running one says so)
        foreach (var r in roles)
            if (s.Script!.Result is null && cars.ContainsKey(r.Car) && !seen.Contains(r.Car) && !Expected(spec, r)) bad.Add($"{r.Part} {r.Car}: never came out in {s.Script.Host.Now:0} s ({string.Join(", ", objectives.Flags.Select(Objectives.Describe))})");
        return bad;
    }

    /// <summary>A car's hit points (armour and chassis, all four sides) and its guns' damage a second (damage x rate, bursts and their pauses counted).</summary>
    public static (double Hp, double Dps) Stats(Vehicle v)
    {
        double hp = v.Build is { } b ? b.Armour.Sum() + b.Chassis.Sum() : 0;
        double dps = 0;
        foreach (var w in v.Armory?.Weapons ?? [])
        {
            var d = w.Def;
            if (d.Rate <= 0) continue;
            double burst = d.Burst > 0 && d.Delay > 0 ? d.Burst / (d.Burst + d.Rate * d.Delay) : 1;
            dps += d.Damage * d.Rate * burst;
        }
        return (hp, dps);
    }

    /// <summary>A car's strength against the player's: sqrt(hp x dps), each over the player's (1: a match for him).</summary>
    public static double Strength(Vehicle v, Vehicle player)
    {
        var (hp, dps) = Stats(v);
        var (php, pdps) = Stats(player);
        return php <= 0 || pdps <= 0 ? 0 : System.Math.Sqrt(hp / php * (dps / pdps));
    }

    /// <summary>
    /// A built remix's challenge (RemixChallenge) with its cars' measured strengths (the player's car the Piranha, as the
    /// calibration), and each hostile car's strength by name.
    /// </summary>
    public static (RemixChallenge.Parts Parts, Dictionary<string, double> Cars) Score(AssetVfs vfs, ModelAssembler asm, string mission, RemixSpec spec)
    {
        var path = vfs.Install.FindMission(mission) ?? throw new FileNotFoundException(mission);
        var setup = MissionSetup.Build(vfs, new MissionWorld(vfs, asm, path), asm, "vppirna1.vcf", rand: new Rng.CrtRand(1));
        var vm = setup.Script!.Vm!;
        var me = setup.World.Player!;
        var keys = MissionScriptDecompiler.Keys(vm.Actors.Select(a => a.Label));
        Vehicle? CarOf(string name)
        {
            int i = Array.IndexOf(keys, spec.Cars.ContainsKey(name) || spec.Bind.ContainsKey(name) ? RemixBuilder.PathPrefix + name : name);
            return i >= 0 && vm.Actors[i].Handle is Entity { Vehicle: { } v } ? v : null;
        }
        var cars = new Dictionary<string, double>();
        foreach (var r in RemixRoles.Hostiles(spec)) if (CarOf(r.Car) is { } v) cars[r.Car] = Strength(v, me);
        var parts = RemixChallenge.Score(spec, n => cars.GetValueOrDefault(n), n => CarOf(n) is { } v ? [v.Pos.X, v.Pos.Z] : null);
        return (parts, cars);
    }

    private static string Part(List<RemixRoles.Role> roles, string name) => roles.First(r => r.Car == name).Part;

    /// <summary>Whether a car may stay parked through the check (a sleeper woken only by the player near it, which the walk need not pass).</summary>
    private static bool Expected(RemixSpec spec, RemixRoles.Role r) => r.Part == "sleeper";

    private static float Planar(Vehicle v, float x, float z) => MathF.Sqrt((float)((v.Pos.X - x) * (v.Pos.X - x) + (v.Pos.Z - z) * (v.Pos.Z - z)));

    private static void Put(MissionSetup setup, Vehicle me, float x, float z)
    {
        me.Pos = new I76.Sim.Math.Double3(x, setup.Ground.BareHeight(x, z) + 1, z);
        me.Vel = default;
    }

    /// <summary>The zones a template tests, by the mark the map gives them.</summary>
    private static IEnumerable<(string Label, ZoneSpec Zone)> Zones(RemixSpec spec)
    {
        var o = spec.Objective;
        if (o.Arrive is { } a) yield return ("DEFEND", a);
        if (o.Pickup is { } p) yield return ("PICKUP", p);
        if (o.Home is { } h) yield return ("HOME", h);
        if (o.Hideout is { } hd) yield return ("HIDEOUT", hd);
        if (o.Convoy?.Exit is { } e) yield return ("EXIT", e);
        if (o.Finish is { } f) yield return (RemixMarks.FinishLabel(spec), f);
    }

    /// <summary>The seconds the check runs: long enough for every wave, pursuit and reinforcement to come out.</summary>
    private static float Horizon(RemixSpec spec)
    {
        var o = spec.Objective;
        float h = 40;
        foreach (var w in o.Waves) h = System.Math.Max(h, w.AtSeconds + 40);
        if (o.Pursuit is { } p) h = System.Math.Max(h, p.Delay + 60);
        foreach (var r in o.Reinforce) h = System.Math.Max(h, r.Delay + 90);
        return h + 30;
    }

    /// <summary>Metres a place the player must reach keeps from a leave-map exit.</summary>
    public const float ExitClearance = 300;

    /// <summary>The start and every zone the player must reach, by the mark the map gives them.</summary>
    private static IEnumerable<(string Label, double[] At)> PlayerPlaces(RemixSpec spec)
    {
        if (spec.Start?.At is { Length: 2 } st) yield return ("START", st);
        foreach (var (label, z) in Zones(spec))
            if (label != "EXIT" && spec.Places.TryGetValue(z.Place, out var pts) && pts.Count > 0) yield return (label, pts[0]);
    }

    /// <summary>Where objective step k takes the player (null: nowhere in particular: shake a tail, catch a convoy).</summary>
    private static double[]? StepPlace(RemixSpec spec, int k)
    {
        var o = spec.Objective;
        double[]? P(ZoneSpec? z) => z is not null && spec.Places.TryGetValue(z.Place, out var pts) && pts.Count > 0 ? pts[0] : null;
        if (k == RemixTemplates.FinishObjective(spec)) return P(o.Finish);
        return o.Template switch
        {
            "deliver" => k == 1 && o.Pickup is not null ? P(o.Pickup) : P(o.Home),
            "tail" => k >= 2 ? P(o.Hideout) : null,
            "defend" => P(o.Arrive),
            _ => null,
        };
    }
}
