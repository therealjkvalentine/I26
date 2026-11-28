namespace I76.Formats.Remix;

/// <summary>
/// Holds a rolled job's spoken lines to the job (docs/remix-missions.md "Spoken lines"): that no line chosen names a place, road,
/// count or thing the job does not have. It reads the built description itself (its zones, cars, counts, deadlines), not the
/// director's facts, and also scans each line's text for the words that name something (a place, a road, a direction, a count),
/// so a line whose needs were written too loosely is caught as well.
/// </summary>
public static class RemixVoiceCheck
{
    /// <summary>Words a line may say only when the job has what they name: the word, and the needs any one of which allows it.</summary>
    private static readonly (string Word, string[] Allowed)[] Words =
    [
        ("truck stop", ["place:truck stop", "defended:truck stop"]), ("oil field", ["place:oil field"]), ("water tower", ["place:water tower"]),
        ("bridge", ["place:bridge"]), ("county line", ["place:county line", "exit:county line"]), ("tatum", ["place:tatum"]),
        ("warehouse", ["place:warehouse", "defended:warehouse"]), ("hideout", ["place:hideout"]), ("rendezvous", ["place:rendezvous"]),
        ("380", ["road:380:west"]), ("385", []), ("west", ["road:380:west", "from:west"]), ("east", ["from:east"]), ("north", []), ("south", []),
        ("convoy", ["convoy"]), ("police", ["police"]), ("two minutes", ["left:120"]), ("to go", ["count:1/3", "count:2/3", "count:3/5"]),
        ("airfield", []), ("hangar", []), ("depot", []), ("skeeter", []), ("vixen", []), ("civilians", ["civilians"]), ("tanker", []), ("soldier", []),
        ("riders", []), ("escort", []), ("monkey wrench", []), ("got away", []),
    ];

    /// <summary>Every problem with a job's spoken lines; none when they are all true of it.</summary>
    public static List<string> Check(RemixSpec spec, RemixNodes map)
    {
        var bad = new List<string>();
        var o = spec.Objective;
        foreach (var ch in spec.Voice)
        {
            if (ch.Line is null) continue;
            if (RemixVoice.ById(ch.Line) is not { } line) { bad.Add($"{ch.Moment}: no line {ch.Line}"); continue; }
            void Bad(string why) => bad.Add($"{ch.Moment} {line.Id} \"{line.Text}\": {why}");
            if (line.Unused is not null) Bad("an unused line said");
            if (!line.Moments.Contains(ch.Moment)) Bad("not a line for this moment");
            foreach (var need in line.Needs) if (!ch.Facts.Contains(need)) Bad($"needs {need}, not a fact of the moment");
            // what the description itself says
            double[]? at = Zone(spec, ch.Moment);
            foreach (var need in line.Needs)
            {
                if (need.StartsWith("place:", StringComparison.Ordinal))
                {
                    string tag = need[6..];
                    if (at is null) { Bad($"{need}: the moment has no place"); continue; }
                    bool ok = tag switch
                    {
                        "hideout" => o.Template == "tail" && ch.Moment == "lost" || map.Of("hideout").Any(n => Near(n.At, at, 60)),
                        "rendezvous" => (map.Of("home").Concat(map.Of("hideout")).Any(n => Near(n.At, at, 60))) && !(spec.Start is { } st && Near(st.At, at, 150)),
                        _ => Tagged(map, tag).Any(p => Near(p, at, 60)),
                    };
                    if (!ok) Bad($"{need}: its place is not that");
                }
                else if (need.StartsWith("defended:", StringComparison.Ordinal))
                {
                    if (o.Template != "defend" || at is null || !Tagged(map, need[9..]).Any(p => Near(p, at, 60))) Bad($"{need}: not what is defended");
                }
                else if (need.StartsWith("count:", StringComparison.Ordinal))
                {
                    int n = int.Parse(need[(need.IndexOf('/') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
                    int group = o.Template == "raid" ? o.Targets.Count : o.Template is "intercept" or "ambush" ? o.Convoy?.Escorts.Count ?? 0 : -1;
                    if (group != n) Bad($"{need}: the group is {group}");
                }
                else if (need.StartsWith("exit:", StringComparison.Ordinal))
                {
                    var ex = o.Convoy?.Exit is { } z ? Point(spec, z.Place) : null;
                    if (ex is null || !Tagged(map, need[5..]).Any(p => Near(p, ex, 400))) Bad($"{need}: the convoy's way out is not there");
                }
                else switch (need)
                {
                    case "convoy" or "lead" when o.Template is not ("intercept" or "ambush"): Bad("no convoy"); break;
                    case "lead:truck" when !(o.Convoy is { } cv && spec.Cars.TryGetValue(cv.Lead, out var lc) && lc.Vcf.StartsWith("vxmilk", StringComparison.OrdinalIgnoreCase)): Bad("the lead is not a truck"); break;
                    case "police" when !(o.Pursuit is { } pu && pu.Cars.All(c => spec.Cars.TryGetValue(c, out var cc) && RemixDirector.Police.Contains(cc.Vcf))): Bad("not the police"); break;
                    case "more" when !o.Reinforce.Any(r => r.Cars.All(c => spec.Cars.TryGetValue(c, out var cc) && RemixDirector.Police.Contains(cc.Vcf))): Bad("no more police come"); break;
                    case "deadline" when o.Deadline <= 0: Bad("no deadline"); break;
                    case "civilians" when spec.Traffic is not { Count: > 0 }: Bad("no civilians on the roads"); break;
                    case "left:120" when Clock(spec) < 180: Bad($"a clock of {Clock(spec)} s"); break;
                    case "left:45" when Clock(spec) < 90: Bad($"a clock of {Clock(spec)} s"); break;
                    case "clear" when !(ch.Moment == "lost" && o.Template == "tail" || ch.Moment == "done" && (o.FinishClear > 0 || o.Template == "deliver" && o.Clear > 0)): Bad("nothing makes the moment clear"); break;
                    case "road:380:west":
                    {
                        var on = Tagged(map, "on 380").ToList();
                        if (spec.Start is not { } s || at is null || !on.Any(p => Near(p, s.At, 60)) || !on.Any(p => Near(p, at, 60)) || RemixVoice.EastWest(s.At, at) != "west")
                            if (ch.Moment != "contact") Bad("not west on 380 from the start");
                        break;
                    }
                }
            }
            // the words of the line
            string text = line.Text.ToLowerInvariant();
            foreach (var (word, allowed) in Words)
                if (text.Contains(word, StringComparison.Ordinal) && !allowed.Any(line.Needs.Contains)) Bad($"says \"{word}\" with nothing to make it true");
            if (text.Contains("truck", StringComparison.Ordinal) && text.Replace("truck stop", "").Contains("truck", StringComparison.Ordinal) && !line.Needs.Contains("lead:truck")) Bad("says \"truck\" with no truck");
        }
        return bad;
    }

    /// <summary>The place a moment points to in the description, or null.</summary>
    private static double[]? Zone(RemixSpec spec, string moment)
    {
        var o = spec.Objective;
        string? place = (moment, o.Template) switch
        {
            ("go", "deliver") => o.Pickup?.Place,
            ("go", "raid") or ("go", "defend") or ("arrive", "defend") => o.Arrive?.Place,
            ("return", "deliver") => o.Home?.Place,
            ("return", "raid") => o.Escape?.Place,
            ("return", _) => o.Finish?.Place,
            ("lost", "tail") => o.Hideout?.Place,
            ("next", "tail") => o.Finish?.Place,
            ("next", "escort") => o.Escort?.Goal?.Place,
            _ => null,
        };
        if (moment == "hold") return spec.Start?.At;
        return place is null ? null : Point(spec, place);
    }

    private static double[]? Point(RemixSpec spec, string place) => spec.Places.TryGetValue(place, out var p) && p.Count > 0 ? p[0] : null;

    /// <summary>The longest clock of the job: its deadline, the escape's seconds, or the arrival's.</summary>
    private static int Clock(RemixSpec spec) => Math.Max(spec.Objective.Deadline, Math.Max(spec.Objective.Escape?.Seconds ?? 0, spec.Objective.Arrive?.Seconds ?? 0));

    /// <summary>Every point of the table whose place is tagged so (nodes, starts, clusters, routes' ends).</summary>
    private static IEnumerable<double[]> Tagged(RemixNodes map, string tag)
    {
        bool Is(string name) => RemixVoice.Tags(map.Base, name).Contains(tag);
        foreach (var n in map.Nodes) if (Is(n.Name)) yield return n.At;
        foreach (var s in map.Starts) if (Is(s.Name)) yield return s.At;
        foreach (var c in map.Clusters) if (Is(c.Name)) yield return c.At;
        foreach (var r in map.Routes) if (Is(r.Name) && r.Points.Count > 0) yield return r.Points[^1];
    }

    private static bool Near(double[] a, double[] b, double r) => (a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) <= r * r;
}
