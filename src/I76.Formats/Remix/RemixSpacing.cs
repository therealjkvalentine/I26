using System.Globalization;

namespace I76.Formats.Remix;

/// <summary>
/// How far apart a job's marks are (docs/remix-missions.md "Spacing"): the start, the places the player must reach, a convoy's lead and
/// its way out. Round five's pacing holds the drives to a most; this holds them to a least, so a job is not over in seconds and its
/// marks are told apart on the map. A place the job sends the player back to (a home or finish at the start) is the start by design.
/// </summary>
public static class RemixSpacing
{
    /// <summary>The least straight-line distance between two different marks.</summary>
    public const double MinPair = 350;
    /// <summary>The least distance from the start to the first event (round five's start rule: 350 m and more).</summary>
    public const double MinFirst = 350;
    /// <summary>A convoy's road may not pass within this of the start ahead of its lead: the convoy would drive into the player at once.</summary>
    public const double MinFromRoute = 250;

    public sealed record Report(double MinPair, string Pair, double StartFirst, double FromRoute, List<string> Problems)
    {
        public bool Packed => Problems.Count > 0;
        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"spacing: closest marks {MinPair:0} m ({Pair}), start to first {StartFirst:0} m{(double.IsInfinity(FromRoute) ? "" : $", start to the convoy's road {FromRoute:0} m")}{(Problems.Count > 0 ? " PACKED: " + string.Join("; ", Problems) : "")}");
    }

    /// <summary>The marks of a job: label and point (the start first).</summary>
    public static List<(string Label, double[] At)> Marks(RemixSpec spec)
    {
        var o = spec.Objective;
        var m = new List<(string, double[])>();
        if (spec.Start?.At is { Length: 2 } st) m.Add(("START", st));
        void Z(string label, ZoneSpec? z) { if (z is not null && spec.Places.TryGetValue(z.Place, out var p) && p.Count > 0) m.Add((label, p[0])); }
        Z(o.Template == "defend" ? "DEFEND" : "TARGET", o.Arrive);
        Z("PICKUP", o.Pickup);
        Z("HOME", o.Home);
        Z("HIDEOUT", o.Hideout);
        Z("EXIT", o.Convoy?.Exit);
        Z("HALFWAY", o.Escort?.Mid);
        Z("GOAL", o.Escort?.Goal);
        Z("ESCAPE", o.Escape);
        Z("FINISH", o.Finish);
        if (o.Convoy is { } c && spec.Cars.TryGetValue(c.Lead, out var lead) && lead.At is { Length: 2 } la) m.Add(("CONVOY", la));
        return m;
    }

    private static double D(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));

    /// <summary>
    /// Marks near each other by design: a place the job sends the player back to (a home, an escape, the finish at or near the start),
    /// and the finish (the last objective: away to a place near where the job ended, or the goal itself).
    /// </summary>
    private static bool Return(string a, string b) => a == "FINISH" || b == "FINISH" || (a == "START" && b is "HOME" or "ESCAPE") || (b == "START" && a is "HOME" or "ESCAPE");

    public static Report Of(RemixSpec spec)
    {
        var marks = Marks(spec);
        var problems = new List<string>();
        double min = double.PositiveInfinity;
        string pair = "";
        for (int i = 0; i < marks.Count; i++)
            for (int j = i + 1; j < marks.Count; j++)
            {
                if (Return(marks[i].Label, marks[j].Label)) continue;
                double d = D(marks[i].At, marks[j].At);
                if (d < min) { min = d; pair = $"{marks[i].Label}-{marks[j].Label}"; }
            }
        if (min < MinPair) problems.Add(string.Create(CultureInfo.InvariantCulture, $"{pair} {min:0} m apart (at least {MinPair:0})"));
        double first = double.PositiveInfinity;
        if (spec.Start?.At is { Length: 2 } st)
        {
            var others = marks.Where(x => x.Label is not ("START" or "HOME" or "FINISH" or "ESCAPE" or "EXIT")).ToList();
            if (others.Count > 0) first = others.Min(x => D(st, x.At));
            if (first < MinFirst) problems.Add(string.Create(CultureInfo.InvariantCulture, $"the first event {first:0} m from the start (at least {MinFirst:0})"));
        }
        double fromRoute = double.PositiveInfinity;
        if (spec.Objective.Convoy is { } cv && spec.Start?.At is { Length: 2 } s0 && spec.Places.TryGetValue(cv.Route, out var route) && route.Count > 1)
        {
            // the convoy's road from its lead on: where it would pass the start (the lead parks at its first point)
            fromRoute = FromRoute(s0, route);
            if (spec.Objective.Template == "intercept" && fromRoute < MinFromRoute)
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"the start on the convoy's road ({fromRoute:0} m from it, at least {MinFromRoute:0}): it drives into the player"));
        }
        return new Report(min, pair, first, fromRoute, problems);
    }


    /// <summary>The distance from a point to a road of points (infinity for a road of fewer than two: a stock path, unknown here).</summary>
    public static double FromRoute(double[] p, IReadOnlyList<double[]> route)
    {
        double d = double.PositiveInfinity;
        for (int k = 1; k < route.Count; k++) d = Math.Min(d, Segment(p, route[k - 1], route[k]));
        return d;
    }
    private static double Segment(double[] p, double[] a, double[] b)
    {
        double vx = b[0] - a[0], vz = b[1] - a[1], l = vx * vx + vz * vz;
        double t = l <= 0 ? 0 : Math.Clamp(((p[0] - a[0]) * vx + (p[1] - a[1]) * vz) / l, 0, 1);
        double x = a[0] + t * vx - p[0], z = a[1] + t * vz - p[1];
        return Math.Sqrt(x * x + z * z);
    }
}
