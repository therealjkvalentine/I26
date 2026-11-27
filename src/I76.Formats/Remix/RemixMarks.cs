using System.Globalization;
using System.Numerics;
using System.Text;
using I76.Formats.Vfs;

namespace I76.Formats.Remix;

/// <summary>
/// What the port's map of a remix marks (docs/remix-missions.md): the base mission's own map art (WRLD +0x79) is a drawing of
/// the stock mission with its marks painted in, so a remix's map is the port's pen sketch of the ground (ArenaSketch) with
/// these marks: the start, the template's places (the zone to defend, the pickup and home, the hideout, the convoy and its
/// exit) and a route drawn as a line. Written beside the mission as STEM.marks, one mark a line: "MARK label x z" or
/// "LINE label x z x z ...", world x and z.
/// </summary>
public sealed class RemixMarks
{
    public List<(string Label, Vector2 At)> Points { get; } = [];
    public List<(string Label, Vector2[] Points)> Lines { get; } = [];
    /// <summary>Lines for the notepad and the map's foot: where the places are, in words (the description's text.notes).</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Every point the marks name, the region the sketch is drawn round.</summary>
    public IEnumerable<Vector2> All => Points.Select(p => p.At).Concat(Lines.SelectMany(l => l.Points));

    /// <summary>The marks of a description: <paramref name="place"/> gives a place's points, <paramref name="car"/> where a car stands (null: gone).</summary>
    public static RemixMarks Of(RemixSpec spec, Vector2 start, Func<string, IReadOnlyList<Vector2>> place, Func<string, Vector2?> car)
    {
        var m = new RemixMarks();
        var o = spec.Objective;
        void Zone(ZoneSpec? z, string label)
        {
            if (z is not null && place(z.Place) is [var at, ..]) m.Add(label, at);
        }
        m.Add("START", start);
        m.Notes.AddRange(spec.Text.Notes);
        switch (o.Template)
        {
            case "defend":
                if (o.Arrive is { } a) Zone(a, "DEFEND");
                else if (o.Defend.Count > 0 && car(o.Defend[0]) is { } d) m.Add("DEFEND", d);
                break;
            case "deliver":
                Zone(o.Pickup, "PICKUP");
                Zone(o.Home, "HOME");
                break;
            case "tail":
                Zone(o.Hideout, "HIDEOUT");
                break;
            case "intercept" when o.Convoy is { } cv:
                if (place(cv.Route) is { Count: > 1 } route) m.Lines.Add(("ROUTE", [.. route]));
                if (car(cv.Lead) is { } lead) m.Add("CONVOY", lead);
                Zone(cv.Exit, "EXIT");
                break;
            case "escort" when o.Escort is { } es:
                if (place(es.Route) is { Count: > 1 } eroute) m.Lines.Add(("ROUTE", [.. eroute]));
                if (car(es.Car) is { } van) m.Add("ESCORT", van);
                Zone(es.Mid, "HALFWAY");
                Zone(es.Goal, "GOAL");
                break;
            case "raid":
                if (o.Arrive is { } ra) Zone(ra, "TARGET");
                else if (o.Targets.Count > 0 && car(o.Targets[0]) is { } t) m.Add("TARGET", t);
                Zone(o.Escape, "ESCAPE");
                break;
        }
        if (o.Finish is { } fin) Zone(fin, FinishLabel(spec));
        return m;
    }

    /// <summary>What the map calls a job's finish: the safe house after a tail's hideout, home after anything else.</summary>
    public static string FinishLabel(RemixSpec spec) => spec.Objective.Template == "tail" ? "SAFE HOUSE" : "HOME";

    /// <summary>A mark; one at (nearly) the place of an earlier one joins its label ("START / HOME").</summary>
    public void Add(string label, Vector2 at)
    {
        int i = Points.FindIndex(p => Vector2.Distance(p.At, at) < 150);
        if (i >= 0) Points[i] = (Points[i].Label + " / " + label, Points[i].At);
        else Points.Add((label, at));
    }

    public string ToText()
    {
        var inv = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        foreach (var (l, at) in Points) b.Append(inv, $"MARK {l.Replace(' ', '_')} {at.X:0.#} {at.Y:0.#}\n");
        foreach (var (l, pts) in Lines) b.Append(inv, $"LINE {l.Replace(' ', '_')} {string.Join(" ", pts.Select(p => string.Create(inv, $"{p.X:0.#} {p.Y:0.#}")))}\n");
        foreach (var n in Notes) b.Append($"NOTE {n.Replace('\n', ' ')}\n");
        return b.ToString();
    }

    public static RemixMarks Parse(string text)
    {
        var m = new RemixMarks();
        var inv = CultureInfo.InvariantCulture;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("NOTE ", StringComparison.Ordinal)) { m.Notes.Add(line[5..]); continue; }
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 4) continue;
            var n = f.Skip(2).Select(s => float.TryParse(s, NumberStyles.Float, inv, out var v) ? v : float.NaN).ToArray();
            if (n.Any(float.IsNaN) || n.Length % 2 != 0) continue;
            var pts = Enumerable.Range(0, n.Length / 2).Select(i => new Vector2(n[2 * i], n[2 * i + 1])).ToArray();
            if (f[0] == "MARK") m.Points.Add((f[1].Replace('_', ' '), pts[0]));
            else if (f[0] == "LINE" && pts.Length > 1) m.Lines.Add((f[1].Replace('_', ' '), pts));
        }
        return m;
    }

    /// <summary>A generated mission's marks (STEM.marks in GameInstall.UserMissionDir), or null: not a remix, or built before marks.</summary>
    public static RemixMarks? For(string stem)
    {
        if (GameInstall.InUserMissionDir(stem + ".marks") is not { } p) return null;
        try { return Parse(File.ReadAllText(p)); }
        catch (IOException) { return null; }
    }

    /// <summary>A generated mission's build stamp (STEM.remix: the generator's version, the description's and the base's hashes), or "".</summary>
    public static string StampOf(string stem)
    {
        if (GameInstall.InUserMissionDir(Path.GetFileNameWithoutExtension(stem) + ".remix") is not { } p) return "";
        try { return File.ReadAllText(p).Trim(); }
        catch (IOException) { return ""; }
    }

    /// <summary>Whether a mission stem names a generated remix (its stamp is in GameInstall.UserMissionDir).</summary>
    public static bool IsRemix(string? stem) =>
        !string.IsNullOrEmpty(stem) && GameInstall.InUserMissionDir(Path.GetFileNameWithoutExtension(stem) + ".remix") is not null;
}
