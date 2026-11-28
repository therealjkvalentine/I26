using System.Globalization;
using I76.Formats.Mission;

namespace I76.Formats.Remix;

/// <summary>
/// A civilian car of a generated job (docs/remix-missions.md "Civilian traffic"): a car of the description's (nobody's side) that, after
/// its delay (hidden at the map's lot) and no human is within <see cref="RemixTraffic.Unseen"/> of the route's start, is put there and
/// drives its route (a place: the route's points, in the way it drives) at its speed and, at the route's end, waits until no
/// human is within <see cref="RemixTraffic.Unseen"/> of it or of the route's start, then is put back at the start and drives it again: the
/// stock trip's own traffic loop (T02's "normal" cars: sit, a timer, goto a path at 30, teleport back out of the player's sight).
/// </summary>
public sealed class TrafficSpec
{
    public string Car { get; set; } = "";
    public string Route { get; set; } = "";
    public int Speed { get; set; } = 30;
    public int Delay { get; set; }
}

/// <summary>
/// Civilian traffic for generated jobs (docs/remix-missions.md "Civilian traffic"). The stock trip runs civilians on T02, T03 and T06
/// (the scripts' "normal" actors: the base game's nor*.vcf cars, no guns, nobody's side), each held a few seconds, then driven along a
/// road path at 30 (a bus at 20) with setAvoid 0, setSkill 1 and setAgg 1, and teleported back to its start when the player is not near;
/// the trip neither fails nor scolds a player who kills one (S04's scenario has Groove remark on it: gdsgc78, then gdsgc77). A job
/// draws traffic on its own SplitMix64 stream from its seed, so the rest of the roll is unchanged.
/// </summary>
public static class RemixTraffic
{
    /// <summary>The base game's civilian cars (the stock trip's normal actors' files, and norpony; norlepr and norroyl name bodies the base game lacks), and the slow ones (a bus, a truck).</summary>
    public static readonly string[] Cars = ["norclyd", "norpony", "norstag", "norvan1", "norvan2", "norvike"];
    public static readonly string[] Slow = ["norbus", "normarx"];

    /// <summary>At most this many civilians a job, and hostile and civilian cars together at most this many.</summary>
    public const int MaxCars = 4, MaxTotal = RemixDirector.MaxCars + 2;

    /// <summary>Metres a route's start is from the player's start and every place of the job; and how far every human must be from a car and its route's start before it is put back.</summary>
    public const double Clear = 400, Unseen = 400;

    /// <summary>The lot's row the civilians wait hidden on (the job's own cars take the rows before it: 14 cars, six a row).</summary>
    public const int LotRow = 4;

    /// <summary>
    /// The node tables' routes that are paved road for traffic (each point of the route every 10 m within 22 m of a road, 85 % of
    /// them on paved road, the route 1200 m or longer): written by i76 remix traffic --table from a stock install, held to it by a test.
    /// </summary>
    public static readonly Dictionary<string, string[]> Paved = new(StringComparer.OrdinalIgnoreCase)
    {
        ["t02"] = ["crossroad_west"],
        ["t03"] = ["highway_down"],
        ["t05"] = ["road_380"],
        ["t08"] = ["into_plains", "down_214"],
        ["t10"] = ["east_380", "north_road_down"],
        ["t13"] = ["south_on_285", "to_the_east_road"],
        ["t14"] = ["west_to_north"],
    };

    /// <summary>The chance a job has traffic, by its template: higher where telling a civilian from the target matters.</summary>
    public static double Chance(string template) => template switch
    {
        "intercept" or "ambush" or "escort" or "tail" => 0.8,
        "deliver" or "raid" => 0.6,
        _ => 0.45,
    };

    /// <summary>
    /// A job's traffic, or none: on a map with paved routes whose ends are <see cref="Clear"/> from the player's start and every place,
    /// with the template's <see cref="Chance"/> (less with one such route than with three); two to four cars, each on one of those routes
    /// (either way along it), spaced 35 s apart at 20-28 (a bus or truck at 16; the stock trip's goto at 30, its bus at 20), never more than <see cref="MaxCars"/> nor the job's cars
    /// past <see cref="MaxTotal"/>. Adds the cars (nobody's side), their routes as places and a note. Deterministic from the seed.
    /// </summary>
    public static void Plan(RemixSpec spec, RemixNodes map, int seed)
    {
        if (!Paved.TryGetValue(map.Base, out var names)) return;
        var r = new Mix(unchecked(seed * 0x2545F4914F6CDD1DL + 0x5DEECE66DL));
        var avoid = spec.Places.Values.Where(p => p.Count is 1 or 2).Select(p => p[0]).Concat(spec.Start is { } st ? [st.At] : []).ToList();
        bool Far(double[] p) => avoid.All(a => Dist(a, p) >= Clear);
        var routes = new List<List<double[]>>();
        var roads = new List<string>();
        foreach (var rt in map.Routes.Where(x => names.Contains(x.Name) && x.Points.Count >= 2).OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            // both ends clear: a car comes on and leaves (and waits to be put back) out of the player's way
            if (!Far(rt.Points[0]) || !Far(rt.Points[^1])) continue;
            routes.Add([.. rt.Points]);
            routes.Add([.. Enumerable.Reverse(rt.Points)]);
            // the road's number when the route's name has one (east_380, south_on_285), for the note
            if (System.Text.RegularExpressions.Regex.Match(rt.Name, @"\d{3}") is { Success: true } num && !roads.Contains(num.Value)) roads.Add(num.Value);
        }
        double chance = Chance(spec.Objective.Template) * Math.Min(1, 0.6 + 0.2 * routes.Count);
        int draw = r.Next(1000);
        if (routes.Count == 0 || draw >= chance * 1000) return;
        int hostiles = RemixRoles.Hostiles(spec).Count;
        int n = Math.Min(Math.Min(2 + r.Next(3), MaxCars), MaxTotal - hostiles);
        if (n <= 0) return;
        routes = [.. routes.OrderBy(_ => r.Next(1 << 30))];
        spec.Traffic = [];
        for (int k = 0; k < n; k++)
        {
            var pts = routes[k % routes.Count];
            string place = $"civ_route{k % routes.Count + 1}";
            spec.Places[place] = pts;
            bool slow = r.Next(5) == 0;
            string car = $"civ{k + 1}";
            spec.Cars[car] = new CarSpec
            {
                // hidden at the map's lot (a row past the job's own cars) until its delay, as a reinforcement waits
                Vcf = slow ? Slow[r.Next(Slow.Length)] : Cars[r.Next(Cars.Length)], At = [map.Lot[0] + 12 * k, map.Lot[1] + 12 * LotRow], Team = 0,
            };
            spec.Traffic.Add(new TrafficSpec { Car = car, Route = place, Speed = slow ? 16 : 20 + r.Next(9), Delay = 8 + 35 * (k / Math.Max(1, routes.Count)) + 12 * (k % Math.Max(1, routes.Count)) + r.Next(10) });
        }
        spec.Text.Notes.Add($"Civilians on {(roads.Count == 1 ? roads[0] : "the roads")}: pick your shots.");
    }

    /// <summary>
    /// A built job's traffic held to the map (i76 remix check; RemixTrafficTests): each route paved (as <see cref="PavedShare"/> measures it),
    /// its ends <see cref="Clear"/> from the player's start and every place, the counts within the caps, every car a civilian file.
    /// </summary>
    public static List<string> Check(RemixSpec spec, MissionFile msn)
    {
        var bad = new List<string>();
        if (spec.Traffic is not { Count: > 0 } traffic) return bad;
        if (traffic.Count > MaxCars) bad.Add($"traffic: {traffic.Count} cars, at most {MaxCars}");
        if (traffic.Count + RemixRoles.Hostiles(spec).Count > MaxTotal) bad.Add($"traffic: {traffic.Count} civilians and {RemixRoles.Hostiles(spec).Count} hostile cars, at most {MaxTotal}");
        var avoid = spec.Places.Where(p => !p.Key.StartsWith("civ_", StringComparison.Ordinal) && p.Value.Count is 1 or 2).Select(p => (p.Key, At: p.Value[0]))
            .Concat(spec.Start is { } st ? [("start", st.At)] : []).ToList();
        var roads = Roads(msn);
        foreach (var t in traffic)
        {
            if (!spec.Cars.TryGetValue(t.Car, out var c)) { bad.Add($"traffic {t.Car}: no such car"); continue; }
            if (!Cars.Contains(c.Vcf) && !Slow.Contains(c.Vcf)) bad.Add($"traffic {t.Car}: {c.Vcf} is not a civilian car");
            if (c.Team is not 0) bad.Add($"traffic {t.Car}: on side {c.Team}, not nobody's");
            if (!spec.Places.TryGetValue(t.Route, out var pts) || pts.Count < 2) { bad.Add($"traffic {t.Car}: no route {t.Route}"); continue; }
            foreach (var (key, at) in avoid)
                foreach (var end in (double[][])[pts[0], pts[^1]])
                    if (Dist(at, end) < Clear) bad.Add($"traffic {t.Car}: its route's end is {Dist(at, end):0} m from {key}");
            double paved = PavedShare(roads, pts);
            if (paved < 0.85) bad.Add($"traffic {t.Car}: {100 * paved:0} % of its route on paved road");
            if (t.Speed is < 15 or > 40) bad.Add($"traffic {t.Car}: speed {t.Speed}");
        }
        return bad;
    }

    /// <summary>The roads' centre lines every 10 m, with each sample's kind (the segment's kind mod 3: 0 paved, 1 dirt, 2 a wash or planks).</summary>
    public static List<(double X, double Z, int Kind)> Roads(MissionFile msn)
    {
        var o = new List<(double, double, int)>();
        foreach (var r in msn.RoadSegments())
        {
            // (a road point holds its two edges: the centre line is between them, as RemixNodeCheck reads it)
            var c = r.Points.Select(q => q.Raw.Length >= 24 ? (X: (double)(Bin.F32(q.Raw, 0) + Bin.F32(q.Raw, 12)) / 2, Z: (double)(Bin.F32(q.Raw, 8) + Bin.F32(q.Raw, 20)) / 2) : (X: (double)q.X, Z: (double)q.Z)).ToList();
            for (int i = 0; i + 1 < c.Count; i++)
            {
                double len = Math.Sqrt((c[i + 1].X - c[i].X) * (c[i + 1].X - c[i].X) + (c[i + 1].Z - c[i].Z) * (c[i + 1].Z - c[i].Z));
                int n = Math.Max(1, (int)(len / 10));
                for (int k = 0; k < n; k++) o.Add((c[i].X + (c[i + 1].X - c[i].X) * k / n, c[i].Z + (c[i + 1].Z - c[i].Z) * k / n, (int)(r.Kind % 3)));
            }
        }
        return o;
    }

    /// <summary>The share of a route's points every 10 m whose nearest road within 22 m is paved (0 for a point off the roads).</summary>
    public static double PavedShare(List<(double X, double Z, int Kind)> roads, List<double[]> pts)
    {
        int all = 0, paved = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            double len = Dist(pts[i], pts[i + 1]);
            int n = Math.Max(1, (int)(len / 10));
            for (int k = 0; k < n; k++)
            {
                double x = pts[i][0] + (pts[i + 1][0] - pts[i][0]) * k / n, z = pts[i][1] + (pts[i + 1][1] - pts[i][1]) * k / n;
                all++;
                double best = 22 * 22;
                int kind = -1;
                foreach (var s in roads)
                {
                    double d = (s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z);
                    if (d <= best) { best = d; kind = s.Kind; }
                }
                if (kind == 0) paved++;
            }
        }
        return all == 0 ? 0 : (double)paved / all;
    }

    /// <summary>A route's length in metres.</summary>
    public static double Length(List<double[]> pts) => Enumerable.Range(0, Math.Max(0, pts.Count - 1)).Sum(i => Dist(pts[i], pts[i + 1]));

    private static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));

    private sealed class Mix(long seed)
    {
        private ulong _s = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL);

        public int Next(int n)
        {
            ulong z = _s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z % (ulong)Math.Max(1, n));
        }
    }

    /// <summary>The civilian kills' radio (S04's own: Groove on the first, then on any after), the clips by stem.</summary>
    public static readonly string[] KillClips = ["gdsgc78", "gdsgc77"];

    internal static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
}
