using I76.Formats.Mission;
using I76.Formats.Vfs;

namespace I76.Formats.Remix;

/// <summary>
/// A job's pacing (docs/remix-missions.md "Pacing"): the drives it asks of the player, leg by leg (the start to the first contact,
/// then from each event to the next), timed on the map's road network at a normal driving speed (<see cref="RoadSpeed"/>; a leg
/// that leaves the road drives its off-road part at <see cref="FieldSpeed"/>, and may cut across country when that is quicker),
/// and held to the job's declared budget (<see cref="PacingSpec"/>). The owner's guideline: one to two minutes of driving between
/// events on average; a deliberately long leg (an escort's slow drive, a race against a timer) three to four. Reads the install's
/// roads; writes nothing.
/// </summary>
public static class RemixPacing
{
    /// <summary>Metres a second on a road (about 50 mph: a car driven at a normal pace, not flat out).</summary>
    public const double RoadSpeed = 22;
    /// <summary>Metres a second off the road.</summary>
    public const double FieldSpeed = 12;

    /// <summary>A leg: its name, from and to, the speed it is driven at (null: the road and field speeds), and whether it is a declared long one.</summary>
    public sealed record Leg(string Name, double[] From, double[] To, double? Speed = null, bool Long = false, double Extra = 0);

    /// <summary>A leg measured: its road distance and seconds.</summary>
    public sealed record Timed(Leg Leg, double Metres, double Seconds);

    /// <summary>A job's legs measured, the first contact's seconds, and the problems against its budget.</summary>
    public sealed record Report(List<Timed> Legs, double FirstContact, double Mean, List<string> Problems)
    {
        public override string ToString() =>
            $"first contact {FirstContact:0} s, mean {Mean:0} s: " + string.Join(", ", Legs.Select(l => $"{l.Leg.Name} {l.Seconds:0} s ({l.Metres / 1000:0.0} km{(l.Leg.Long ? ", long" : "")})"));
    }

    /// <summary>
    /// The drives a job asks for, in order: to the first event, then to each next one. The first leg ends at the first contact
    /// (a tail's pursuit comes to the player: its leg is the pursuit's delay and run in).
    /// </summary>
    public static List<Leg> Legs(RemixSpec spec)
    {
        var o = spec.Objective;
        var legs = new List<Leg>();
        double[]? P(ZoneSpec? z) => z is not null && spec.Places.TryGetValue(z.Place, out var pts) && pts.Count > 0 ? pts[0] : null;
        double[]? start = spec.Start?.At is { Length: 2 } s ? s : null;
        if (start is null) return legs;
        void Add(string name, double[]? from, double[]? to, double? speed = null, bool isLong = false, double extra = 0)
        {
            if (from is not null && to is not null) legs.Add(new Leg(name, from, to, speed, isLong || spec.Pacing.LongLegs.Contains(name, StringComparer.Ordinal), extra));
        }
        switch (o.Template)
        {
            case "deliver":
                if (P(o.Pickup) is { } pk) { Add("pickup", start, pk); Add("home", pk, P(o.Home)); }
                else Add("home", start, P(o.Home));
                break;
            case "tail":
                // the pursuit is put in behind the player: its contact is its delay and a short run in; then the hideout, then the safe house
                legs.Add(new Leg("tail", start, start, Extra: (o.Pursuit?.Delay ?? 0) + 20));
                Add("hideout", start, P(o.Hideout));
                Add("safe house", P(o.Hideout), P(o.Finish));
                break;
            case "intercept":
                if (o.Convoy is { } cv)
                {
                    var park = cv.Lead is { } l && spec.Cars.TryGetValue(l, out var lc) ? lc.At : null;
                    Add("convoy", start, park);
                    Add("back", P(cv.Exit) is { } ex && park is not null ? [(park[0] + ex[0]) / 2, (park[1] + ex[1]) / 2] : park, P(o.Finish));
                }
                break;
            case "defend":
                Add("defend", start, P(o.Arrive));
                break;
            case "ambush":
                // the convoy comes to him: its delay and its run along the route to where it passes nearest him; then away
                if (o.Convoy is { } am && spec.Places.TryGetValue(am.Route, out var rpts) && rpts.Count > 1)
                {
                    int near = 0;
                    for (int i = 1; i < rpts.Count; i++) if (Dist2(rpts[i], start) < Dist2(rpts[near], start)) near = i;
                    double run = 0;
                    for (int i = 1; i <= near; i++) run += System.Math.Sqrt(Dist2(rpts[i], rpts[i - 1]));
                    legs.Add(new Leg("ambush", start, start, Extra: am.Delay + run / System.Math.Max(1, am.Speed)));
                    Add("away", rpts[near], P(o.Finish));
                }
                break;
            case "escort":
                if (o.Escort is { } e && spec.Places.TryGetValue(e.Route, out var route) && route.Count > 1)
                {
                    // the escorted car's pace, and the ambush where it waits on the way
                    double v = System.Math.Max(5, e.Speed);
                    var ambush = o.Sleepers.Select(sl => spec.Cars.TryGetValue(sl.Car, out var c) ? c.At : null).OfType<double[]>().FirstOrDefault();
                    if (ambush is not null) Add("ambush", start, ambush, v, true);
                    Add("halfway", start, P(e.Mid), v, true);
                    Add("goal", P(e.Mid), P(e.Goal), v, true);
                }
                break;
            case "raid":
                Add("target", start, P(o.Arrive));
                Add("escape", P(o.Arrive), P(o.Escape), null, true);
                break;
        }
        return legs;
    }

    /// <summary>Measures a job's legs on its base map's roads and holds them to its budget.</summary>
    public static Report Measure(GameInstall install, RemixSpec spec) => Measure(RoadsOf(install, spec.Base), spec);

    public static Report Measure(Roads roads, RemixSpec spec)
    {
        var timed = new List<Timed>();
        foreach (var l in Legs(spec))
        {
            double m = roads.Drive(l.From, l.To, out double secs);
            if (l.Speed is { } v) secs = roads.Metres(l.From, l.To) / v;   // driven at the escorted car's pace along the road
            timed.Add(new Timed(l, m, secs + l.Extra));
        }
        var b = spec.Pacing;
        var bad = new List<string>();
        double first = timed.Count > 0 ? timed[0].Seconds : 0;
        // a car parked on the way (a sleeper woken as the player passes, a guard) can be met before the first leg's end
        if (spec.Start?.At is { Length: 2 } st && spec.Objective.Template != "escort")
            foreach (var sl in spec.Objective.Sleepers)
                if (spec.Cars.TryGetValue(sl.Car, out var sc) && sc.At is { Length: 2 })
                {
                    roads.Drive(st, sc.At, out double ss);
                    first = System.Math.Min(first, System.Math.Max(0, ss - sl.Wake / RoadSpeed));
                }
        var counted = timed.Where(t => t.Leg.Name != "tail").ToList();
        double mean = counted.Count > 0 ? counted.Average(t => t.Seconds) : 0;
        if (first > b.First) bad.Add($"pacing: the first contact is {first:0} s of driving from the start (budget {b.First} s)");
        if (mean > b.Mean) bad.Add($"pacing: {mean:0} s of driving between events on average (budget {b.Mean} s)");
        foreach (var t in counted)
        {
            int max = t.Leg.Long ? b.Long : b.Leg;
            if (t.Seconds > max) bad.Add($"pacing: the {t.Leg.Name} leg is {t.Seconds:0} s of driving ({t.Metres / 1000:0.0} km; budget {max} s{(t.Leg.Long ? ", a long leg" : "")})");
        }
        return new Report(timed, first, mean, bad);
    }

    private static double Dist2(double[] a, double[] b) => (a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]);

    private static readonly Dictionary<string, Roads> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A map's road network (kept per base for the process: the check measures hundreds of jobs on sixteen maps).</summary>
    public static Roads RoadsOf(GameInstall install, string baseMission)
    {
        lock (Cache)
        {
            string key = (install.Root ?? "") + "|" + baseMission;
            if (Cache.TryGetValue(key, out var r)) return r;
            var path = install.FindMission(baseMission) ?? throw new RemixError($"no mission '{baseMission}' in the install");
            return Cache[key] = Roads.From(new MissionFile(path));
        }
    }

    /// <summary>
    /// A map's roads as a graph: each road's centre line sampled every 10 m, its samples joined in order, and samples of different
    /// roads within 30 m of each other joined (a junction). A point off the road joins its nearest sample across country.
    /// </summary>
    public sealed class Roads
    {
        private readonly List<(double X, double Z)> _pts = [];
        private readonly List<List<(int To, double D)>> _adj = [];
        private readonly Dictionary<(int, int), List<int>> _grid = [];
        private const double Grid = 60;

        public int Count => _pts.Count;

        public static Roads From(MissionFile msn)
        {
            var r = new Roads();
            foreach (var seg in msn.RoadSegments())
            {
                var c = seg.Points.Select(q => q.Raw.Length >= 24 ? (X: (double)(Bin.F32(q.Raw, 0) + Bin.F32(q.Raw, 12)) / 2, Z: (double)(Bin.F32(q.Raw, 8) + Bin.F32(q.Raw, 20)) / 2) : (X: (double)q.X, Z: (double)q.Z)).ToList();
                int prev = -1;
                for (int i = 0; i + 1 < c.Count; i++)
                {
                    double len = Dist(c[i], c[i + 1]);
                    int n = System.Math.Max(1, (int)(len / 10));
                    for (int k = i == 0 ? 0 : 1; k <= n; k++)
                    {
                        int id = r.Add((c[i].X + (c[i + 1].X - c[i].X) * k / n, c[i].Z + (c[i + 1].Z - c[i].Z) * k / n));
                        if (prev >= 0) r.Link(prev, id);
                        prev = id;
                    }
                }
            }
            // junctions: samples of different roads (or far apart along one) within 30 m
            for (int i = 0; i < r._pts.Count; i++)
                foreach (int j in r.Near(r._pts[i], 30))
                    if (j > i && !r._adj[i].Any(a => a.To == j)) r.Link(i, j);
            return r;
        }

        private int Add((double X, double Z) p)
        {
            _pts.Add(p);
            _adj.Add([]);
            var key = ((int)System.Math.Floor(p.X / Grid), (int)System.Math.Floor(p.Z / Grid));
            if (!_grid.TryGetValue(key, out var l)) _grid[key] = l = [];
            l.Add(_pts.Count - 1);
            return _pts.Count - 1;
        }

        private void Link(int a, int b)
        {
            double d = Dist(_pts[a], _pts[b]);
            _adj[a].Add((b, d));
            _adj[b].Add((a, d));
        }

        private IEnumerable<int> Near((double X, double Z) p, double r)
        {
            int gx = (int)System.Math.Floor(p.X / Grid), gz = (int)System.Math.Floor(p.Z / Grid), k = (int)System.Math.Ceiling(r / Grid);
            for (int dx = -k; dx <= k; dx++)
                for (int dz = -k; dz <= k; dz++)
                    if (_grid.TryGetValue((gx + dx, gz + dz), out var l))
                        foreach (int i in l) if (Dist(_pts[i], p) <= r) yield return i;
        }

        /// <summary>The nearest road sample to a point and its distance (searching outward; -1 when the map has no roads).</summary>
        public (int I, double D) Nearest(double[] at)
        {
            var p = (at[0], at[1]);
            for (double r = 100; r <= 6400; r *= 2)
            {
                int best = -1;
                double bd = double.MaxValue;
                foreach (int i in Near(p, r)) { double d = Dist(_pts[i], p); if (d < bd) { bd = d; best = i; } }
                if (best >= 0) return (best, bd);
            }
            return (-1, double.MaxValue);
        }

        /// <summary>Metres along the roads from one point to another (each joined to its nearest road across country), or straight across when quicker to drive.</summary>
        public double Metres(double[] a, double[] b) => Drive(a, b, out _, metresOnly: true);

        /// <summary>
        /// The quicker of the road route (the points joined to their nearest road samples at the field speed) and straight across
        /// country at the field speed: its metres, and its seconds.
        /// </summary>
        public double Drive(double[] a, double[] b, out double seconds, bool metresOnly = false)
        {
            double straight = Dist((a[0], a[1]), (b[0], b[1]));
            var (ia, da) = Nearest(a);
            var (ib, db) = Nearest(b);
            double roadM = ia < 0 || ib < 0 ? double.PositiveInfinity : Path(ia, ib);
            double roadS = da / FieldSpeed + roadM / RoadSpeed + db / FieldSpeed, fieldS = straight / FieldSpeed;
            if (roadS <= fieldS) { seconds = roadS; return da + roadM + db; }
            seconds = fieldS;
            return straight;
        }

        private readonly Dictionary<(int, int), double> _memo = [];

        private double Path(int a, int b)
        {
            if (a == b) return 0;
            if (_memo.TryGetValue((a, b), out var m)) return m;
            var dist = new Dictionary<int, double> { [a] = 0 };
            var pq = new PriorityQueue<int, double>();
            pq.Enqueue(a, 0);
            while (pq.TryDequeue(out int u, out double du))
            {
                if (u == b) break;
                if (du > dist[u]) continue;
                foreach (var (v, w) in _adj[u])
                {
                    double nd = du + w;
                    if (nd < dist.GetValueOrDefault(v, double.PositiveInfinity)) { dist[v] = nd; pq.Enqueue(v, nd); }
                }
            }
            double r = dist.GetValueOrDefault(b, double.PositiveInfinity);
            _memo[(a, b)] = _memo[(b, a)] = r;
            return r;
        }

        private static double Dist((double X, double Z) a, (double X, double Z) b) => System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
    }
}

/// <summary>
/// A job's pacing budget (docs/remix-missions.md "Pacing"), in seconds of driving at a normal pace: the first contact from the
/// start, the average between events, any one leg, and a leg declared long (an escort's slow drive, a race against a timer).
/// </summary>
public sealed class PacingSpec
{
    public int First { get; set; } = 120;
    public int Mean { get; set; } = 120;
    public int Leg { get; set; } = 150;
    public int Long { get; set; } = 240;
    /// <summary>Legs (by name: pickup, home, hideout, safe house, convoy, back, defend, ambush, halfway, goal, target, escape) allowed the long budget.</summary>
    public List<string> LongLegs { get; set; } = [];
}
