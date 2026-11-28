using I76.Formats.Remix;

namespace I76.Game.Shell;

/// <summary>
/// Where everything on ROGUETRIP's choice screen goes (docs/remix-missions.md "The choice map"), worked out apart from the drawing so
/// a test can hold it: the region's lines, towns, road numbers and the offers' rings in the region box, every word placed beside its
/// mark where it collides with no other word or mark and crosses as few lines as it can (the four sides of the mark, then the
/// corners; a word with no clear place is left out, the least important first); the offers' words wrapped whole in their column;
/// the chosen job's map marks lettered clear of each other. Shell pixels (640 x 480). No engine calls.
/// </summary>
public static class RoguetripLayout
{
    public readonly record struct Box(float X, float Y, float W, float H)
    {
        public float Right => X + W;
        public float Bottom => Y + H;
        public bool Intersects(Box o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
        public bool Within(Box o) => X >= o.X - 0.01f && Y >= o.Y - 0.01f && Right <= o.Right + 0.01f && Bottom <= o.Bottom + 0.01f;
        public Box Grow(float d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);
    }

    /// <summary>What a line on the region is: a highway, the state line, the run's road so far, the way to the chosen offer.</summary>
    public enum Ink { Highway, State, Run, Approach }

    public sealed record Segment(float X0, float Y0, float X1, float Y1, Ink Kind);

    /// <summary>A word set down: its text, the box its cell fills (Frame.Text's top left and the font's height), what it names.</summary>
    public sealed record Placed(string Text, Box Rect, string Kind);

    /// <summary>A mark: a town's dot, a trip map's ring ("map"), one the run has been to ("visited").</summary>
    public sealed record Dot(float X, float Y, string Kind);

    public sealed record OfferRing(int Index, float X, float Y, bool Selected)
    {
        public float Radius => Selected ? OuterRing : Ring;
    }

    public sealed class RegionPlan
    {
        public List<Segment> Segments { get; } = [];
        public List<Dot> Dots { get; } = [];
        public List<OfferRing> Offers { get; } = [];
        public List<Placed> Labels { get; } = [];
        /// <summary>Words with no clear place, left out (a quiet town's name, most often).</summary>
        public List<string> Dropped { get; } = [];
        /// <summary>The marks' own boxes: no word may lie on one.</summary>
        public List<Box> Marks { get; } = [];
        /// <summary>The small marks (a trip map's ring, a town's dot): a word goes on one only where it has nowhere else (it is drawn over it).</summary>
        public List<Box> Small { get; } = [];
        /// <summary>A sketch under the words (the job map's): a word on its ink costs as a crossed line does, a pixel at a time.</summary>
        public Func<int, int, bool>? Inked { get; init; }
    }

    /// <summary>How far a word keeps from another town's dot or a trip map's ring where it can (so "HOBBS" does not run on into SEMINOLE).</summary>
    private const float SmallClear = 4;

    /// <summary>The gap between a mark and its word.</summary>
    private const float Gap = 3;

    /// <summary>The sheet inside its double rule: no word may leave it. The map fills it, edge to edge.</summary>
    public static readonly Box Sheet = new(26, 42, 588, 392);
    public static readonly Box MapBox = Sheet;
    /// <summary>The shown offer's scrap, laid over the map's east side (the trip's maps all lie west of it: the view is centred on them in the rest).</summary>
    public static readonly Box ScrapBox = new(334, 46, 274, 382);
    /// <summary>An offer's ring and the chosen one's outer ring, shell pixels.</summary>
    public const float Ring = 10, OuterRing = 14;

    /// <summary>
    /// The map's view of the region: the region's latitudes fill the sheet's height; a degree of longitude is a degree of latitude
    /// shortened by the cosine of the middle latitude; the trip's maps are centred in the part of the sheet left of the scrap. The
    /// rest of the sheet shows more of the country east and west (under the scrap, the atlas or the paper).
    /// </summary>
    public sealed class RegionView
    {
        public Box Map { get; }
        private readonly double _north, _scale, _k, _lon0, _x0;

        public RegionView(RemixRegion reg, Box map, float leftOf)
        {
            Map = map;
            var b = reg.Bounds;
            _north = b.North;
            _scale = map.H / (b.North - b.South);
            _k = I76.Formats.DetMath.Cos((b.North + b.South) / 2 * Math.PI / 180);
            double west = reg.Maps.Values.Min(m => m[1]), east = reg.Maps.Values.Max(m => m[1]);
            _lon0 = (west + east) / 2;
            _x0 = (map.X + leftOf) / 2;
        }

        /// <summary>The sheet's point for a latitude and longitude.</summary>
        public (float X, float Y) Project(double[] latLon) => ((float)(_x0 + (latLon[1] - _lon0) * _k * _scale), (float)(Map.Y + (_north - latLon[0]) * _scale));

        /// <summary>The latitude and longitude at a sheet point.</summary>
        public (double Lat, double Lon) Unproject(double x, double y) => (_north - (y - Map.Y) / _scale, _lon0 + (x - _x0) / (_k * _scale));
    }

    private static RegionView? _view;
    /// <summary>The choice screen's view (the built-in region, the sheet, the scrap's left edge).</summary>
    public static RegionView View => _view ??= new RegionView(RemixRegion.Builtin(), MapBox, ScrapBox.X);
    /// <summary>The shell fonts: the region's, the job map's and the notes' words (the small sans, 10 px), the offers' (the small hand, 12 px).</summary>
    public const int MapFont = 0x47, TextFont = 0x44;
    public const float OfferLine = 13, NoteLine = 13;
    /// <summary>The printed words' capital height on the map and the scrap (Arial Bold, as the stock forms print their labels), in their cell.</summary>
    public const int MapCap = 7;
    /// <summary>The printed words' cells: the small sans's measure scaled (Arial Bold's capitals at MapCap fill about that), and their height.</summary>
    public const float MapScale = 1f, MapCell = 10;

    /// <summary>Where the player's road atlas is: none, being read and fitted, unreadable, or ready to draw.</summary>
    public enum AtlasState { Absent, Loading, Failed, Ready }

    /// <summary>The atlas replaces the region's own lines, towns and words only once it can be drawn: absent, loading or failed, the drawn map shows.</summary>
    public static bool AtlasShown(AtlasState state) => state == AtlasState.Ready;

    /// <summary>
    /// The chosen job's map as the screen draws it (box pixels, w x h): the whole map's sketch (ArenaSketch.Whole) and its marks'
    /// words placed (<see cref="JobMarks"/>). Null without the map's terrain.
    /// </summary>
    public static (I76.Formats.Mission.ArenaSketch Sketch, MarkPlan Marks)? JobMap(I76.Formats.Vfs.GameInstall install, RemixSpec spec, int w, int h, Func<string, float> measure, float textH)
    {
        if (install.FindMission(spec.Base) is not { } path || spec.Start?.At is not [var sx, var sz]) return null;
        var msn = new I76.Formats.Mission.MissionFile(path);
        var marks = RemixMarks.Of(spec, new System.Numerics.Vector2((float)sx, (float)sz),
            n => spec.Places.TryGetValue(n, out var pts) ? [.. pts.Select(q => new System.Numerics.Vector2((float)q[0], (float)q[1]))] : [],
            n => spec.Cars.TryGetValue(n, out var c) && c.At is [var cx, var cz] ? new System.Numerics.Vector2((float)cx, (float)cz) : null);
        var exits = (msn.ReadFsmProgram()?.Paths ?? []).Where(p => p.Name.StartsWith("exit", StringComparison.OrdinalIgnoreCase)).SelectMany(p => p.Points).Select(p => new System.Numerics.Vector2(p.X, p.Z)).ToList();
        if (I76.Formats.Assembly.MissionWorld.FindTerrain(install, msn) is not { } ter
            || I76.Formats.Mission.ArenaSketch.Whole(msn, I76.Formats.Mission.TerrainTiles.Load(msn, ter), [.. marks.Points.Select(p => p.At)], [.. marks.Lines.Select(l => l.Points)], exits, w, h) is not { } sk)
            return null;
        var at = marks.Points.Select(p => { var u = sk.InBox(p.At); return (p.Label, u.X * w, u.Y * h); }).ToList();
        // the words inside the sketch's box (its ink layer), never on another's ring or word, as little on its ink as they can
        return (sk, JobMarks(at, new Box(0, 0, w, h), measure, textH, inked: sk.Inked));
    }

    /// <summary>
    /// The region in <paramref name="box"/>: the lines clipped to it, the towns' dots, the trip maps' rings, the offers' rings (two
    /// on one map side by side), and the words. With <paramref name="backdrop"/> (a road atlas under it) the region's own lines,
    /// towns and words are left to the atlas: only the trip's maps, the run's road and the offers are drawn.
    /// </summary>
    public static RegionPlan Region(RemixRegion reg, RegionView view, IReadOnlyList<string> offerMaps, int sel, IReadOnlyList<string> visited, string here,
        Func<string, float> measure, float textH, bool backdrop = false, Box? cover = null)
    {
        var plan = new RegionPlan();
        var box = view.Map;
        (float X, float Y) At(double[] latLon) => view.Project(latLon);
        // (the scrap over the map: no word goes under it, and no town under it is named)
        bool Covered(float x, float y) => cover is { } cv && new Box(x, y, 0, 0).Within(cv.Grow(2));
        void Line((float X, float Y) p, (float X, float Y) q, Ink kind)
        {
            if (Clip(box, ref p, ref q)) plan.Segments.Add(new Segment(p.X, p.Y, q.X, q.Y, kind));
        }
        if (!backdrop)
        {
            foreach (var l in reg.Lines)
                for (int i = 0; i + 1 < l.Count; i++) Line(At(l[i]), At(l[i + 1]), Ink.State);
            foreach (var h in reg.Highways)
            {
                var pts = reg.Route(h).Select(At).ToList();
                for (int i = 0; i + 1 < pts.Count; i++) Line(pts[i], pts[i + 1], Ink.Highway);
            }
        }
        for (int i = 0; i + 1 < visited.Count; i++)
            if (reg.Maps.TryGetValue(visited[i], out var a) && reg.Maps.TryGetValue(visited[i + 1], out var c)) Line(At(a), At(c), Ink.Run);
        // the offers: rings, the chosen one's way from where the run is
        for (int i = 0; i < offerMaps.Count; i++)
        {
            if (!reg.Maps.TryGetValue(offerMaps[i], out var at)) continue;
            var p = At(at);
            int same = offerMaps.Count(m => m == offerMaps[i]);
            if (same > 1) p = (p.X + (offerMaps.Take(i).Count(m => m == offerMaps[i]) - (same - 1) / 2f) * 20, p.Y);
            plan.Offers.Add(new OfferRing(i, p.X, p.Y, i == sel));
        }
        if (here.Length > 0 && reg.Maps.TryGetValue(here, out var hh) && plan.Offers.FirstOrDefault(o => o.Selected) is { } chosen)
            Line(At(hh), (chosen.X, chosen.Y), Ink.Approach);
        foreach (var o in plan.Offers) plan.Marks.Add(new Box(o.X - o.Radius - 1, o.Y - o.Radius - 1, 2 * o.Radius + 2, 2 * o.Radius + 2));
        if (cover is { } c0) plan.Marks.Add(c0.Grow(3));
        // the trip's maps: small rings (filled where the run has been)
        foreach (var (map, at) in reg.Maps)
        {
            var p = At(at);
            // (under an offer's ring its number stands for it)
            if (!new Box(p.X, p.Y, 0, 0).Within(box) || Covered(p.X, p.Y) || offerMaps.Contains(map, StringComparer.OrdinalIgnoreCase)) continue;
            plan.Dots.Add(new Dot(p.X, p.Y, visited.Contains(map, StringComparer.OrdinalIgnoreCase) ? "visited" : "map"));
            plan.Small.Add(new Box(p.X - 3, p.Y - 3, 6, 6));
        }
        if (backdrop) return plan;
        var towns = new List<(RegionLabel Town, float X, float Y)>();
        foreach (var t in reg.Towns)
        {
            var p = At(t.At);
            if (!new Box(p.X - 1, p.Y - 1, 3, 3).Within(box) || Covered(p.X, p.Y)) continue;
            towns.Add((t, p.X, p.Y));
            // (a town under an offer's ring keeps its name but not its dot: the ring's number is there)
            if (plan.Offers.Any(o => MathF.Abs(o.X - p.X) < o.Radius && MathF.Abs(o.Y - p.Y) < o.Radius)) continue;
            plan.Dots.Add(new Dot(p.X, p.Y, "town"));
            plan.Small.Add(new Box(p.X - 1.5f, p.Y - 1.5f, 3, 3));
        }
        // the words, the most needed first: the towns the region names, the highways' numbers, the states, then the quiet towns
        foreach (var (t, x, y) in towns.Where(t => !t.Town.Quiet)) PlaceAt(plan, box, Printed(t.Name), "town", x, y, measure(Printed(t.Name)), textH, needClear: false);
        foreach (var h in reg.Highways) PlaceRoad(plan, box, h.Name, reg.Route(h).Select(At).ToList(), measure(h.Name), textH);
        foreach (var s in reg.States)
        {
            var p = At(s.At);
            float w = measure(s.Name);
            var cands = new List<Box>();
            foreach (var (dx, dy) in new[] { (0, 0), (0, -10), (0, 10), (-16, 0), (16, 0), (-16, -10), (16, 10), (0, -20), (0, 20) })
                cands.Add(new Box(p.X - w / 2 + dx, p.Y - textH / 2 + dy, w, textH));
            Choose(plan, box, s.Name, "state", cands, needClear: false);
        }
        foreach (var (t, x, y) in towns.Where(t => t.Town.Quiet)) PlaceAt(plan, box, Printed(t.Name), "quiet", x, y, measure(Printed(t.Name)), textH, needClear: true);
        return plan;
    }

    /// <summary>A town's name as the map prints it: in capitals, as the stock forms print their labels.</summary>
    public static string Printed(string name) => name.ToUpperInvariant();

    /// <summary>A word beside a point: right, left, above, below, then the corners (and the same again further out).</summary>
    private static void PlaceAt(RegionPlan plan, Box box, string text, string kind, float x, float y, float w, float h, bool needClear)
    {
        var cands = new List<Box>();
        foreach (float g in new[] { Gap, Gap + 5, Gap + 11, Gap + 17, Gap + 24 })
        {
            cands.Add(new Box(x + g + 1, y - h / 2, w, h));
            cands.Add(new Box(x - g - 1 - w, y - h / 2, w, h));
            cands.Add(new Box(x - w / 2, y - g - h, w, h));
            cands.Add(new Box(x - w / 2, y + g, w, h));
            cands.Add(new Box(x + 2, y - g - h, w, h));
            cands.Add(new Box(x + 2, y + g, w, h));
            cands.Add(new Box(x - 2 - w, y - g - h, w, h));
            cands.Add(new Box(x - 2 - w, y + g, w, h));
            // beside it, raised or lowered by half the word
            cands.Add(new Box(x + g + 1, y - h, w, h));
            cands.Add(new Box(x + g + 1, y, w, h));
            cands.Add(new Box(x - g - 1 - w, y - h, w, h));
            cands.Add(new Box(x - g - 1 - w, y, w, h));
        }
        Choose(plan, box, text, kind, cands, needClear, (x, y));
    }

    /// <summary>A highway's number beside its line, part way along one of its stretches, on either side.</summary>
    private static void PlaceRoad(RegionPlan plan, Box box, string text, List<(float X, float Y)> pts, float w, float h)
    {
        var cands = new List<Box>();
        float[] ts = [0.5f, 0.4f, 0.6f, 0.3f, 0.7f, 0.2f, 0.8f];
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var (p, q) = (pts[i], pts[i + 1]);
            if (!Clip(box, ref p, ref q)) continue;
            float dx = q.X - p.X, dy = q.Y - p.Y, len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < w) continue;
            float nx = -dy / len, ny = dx / len;
            float half = MathF.Abs(nx) * w / 2 + MathF.Abs(ny) * h / 2;
            foreach (float t in ts)
                foreach (int side in new[] { 1, -1, 3, -3 })
                {
                    float off = Math.Sign(side) * (Gap + half + (Math.Abs(side) > 1 ? 6 : 0));
                    float cx = p.X + dx * t + nx * off, cy = p.Y + dy * t + ny * off;
                    cands.Add(new Box(cx - w / 2, cy - h / 2, w, h));
                }
        }
        Choose(plan, box, text, "road", cands, needClear: false);
    }

    /// <summary>
    /// The first candidate crossing the fewest lines that lies inside the box and on no word or mark; none: the word is left out
    /// (and so is one that needs a clear place and has none).
    /// </summary>
    private static void Choose(RegionPlan plan, Box box, string text, string kind, List<Box> cands, bool needClear, (float X, float Y)? own = null)
    {
        Box? best = null;
        int bestCost = int.MaxValue;
        bool Own(Box m) => own is { } o && new Box(o.X, o.Y, 0, 0).Within(m);
        for (int i = 0; i < cands.Count; i++)
        {
            var c = cands[i];
            if (!c.Within(box) || plan.Labels.Any(l => l.Rect.Grow(2.5f).Intersects(c)) || plan.Marks.Any(m => m.Intersects(c))) continue;
            int crossings = plan.Segments.Count(s => Crosses(s, c.Grow(-0.5f))), small = plan.Small.Count(m => m.Intersects(c) && !Own(m)),
                near = plan.Small.Count(m => m.Grow(SmallClear).Intersects(c) && !Own(m));
            if (needClear && crossings + small > 0) continue;
            int ink = 0;
            if (plan.Inked is { } inkedAt)
                for (int y = (int)c.Y; y < (int)c.Bottom; y++) for (int x = (int)c.X; x < (int)c.Right; x++) if (inkedAt(x, y)) ink++;
            int cost = small * 10000 + near * 150 + crossings * 100 + ink * 8 + i;
            if (cost < bestCost) { bestCost = cost; best = c; }
        }
        if (best is { } b) plan.Labels.Add(new Placed(text, b, kind));
        else plan.Dropped.Add(text);
    }

    /// <summary>Whether a segment passes through a box (Liang-Barsky).</summary>
    public static bool Crosses(Segment s, Box b)
    {
        (float X, float Y) p = (s.X0, s.Y0), q = (s.X1, s.Y1);
        return Clip(b, ref p, ref q);
    }

    /// <summary>A segment cut to a box; false when none of it is inside.</summary>
    public static bool Clip(Box b, ref (float X, float Y) p, ref (float X, float Y) q)
    {
        float t0 = 0, t1 = 1, dx = q.X - p.X, dy = q.Y - p.Y;
        bool Edge(float pp, float qq)
        {
            if (pp == 0) return qq >= 0;
            float r = qq / pp;
            if (pp < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
        if (!Edge(-dx, p.X - b.X) || !Edge(dx, b.Right - p.X) || !Edge(-dy, p.Y - b.Y) || !Edge(dy, b.Bottom - p.Y)) return false;
        var a = p;
        p = (a.X + t0 * dx, a.Y + t0 * dy);
        q = (a.X + t1 * dx, a.Y + t1 * dy);
        return true;
    }

    // ---- the offer on its scrap ----

    /// <summary>The offer's scrap: its lines (each at its top left), the "1 of 3" (top right), the box its job map gets, and what did not fit.</summary>
    public sealed record ScrapPlan(Box Scrap, List<(string Text, float X, float Y)> Lines, (string Text, float X, float Y) Count, Box Map, List<string> Overflow);

    /// <summary>The smallest the job's map on the scrap may be squeezed to.</summary>
    public const float MinMapH = 96;
    private const float ScrapPadX = 14, ScrapPadTop = 12, ScrapPadFoot = 10;

    /// <summary>
    /// The shown offer on its scrap, in the form's hand, every word whole: at the top the run's line (beside the "1 of 3"), the offer's
    /// title (its number, map, job and challenge) and its words; at the foot the help line; above it the job's notes and its other
    /// objectives; the job's map between them, as tall as is left. Anything that still does not fit is named in Overflow.
    /// </summary>
    public static ScrapPlan Scrap(Box scrap, string run, string count, string title, string words, IReadOnlyList<string> notes, string help, Func<string, float> measure, float lineH)
    {
        var lines = new List<(string, float, float)>();
        var overflow = new List<string>();
        float x = scrap.X + ScrapPadX, inner = scrap.W - 2 * ScrapPadX, cw = measure(count);
        float y = scrap.Y + ScrapPadTop;
        void Put(IEnumerable<string> ls, float w)
        {
            foreach (var l in ls) { lines.Add((l, x, y)); if (measure(l) > w + 0.01f) overflow.Add(l); y += lineH; }
        }
        // the run's line beside the count, then a gap, the title, the words
        var runLines = Wrap(run, inner - cw - 8, measure);
        Put(runLines.Take(1), inner - cw - 8);
        Put(Wrap(string.Join(" ", runLines.Skip(1)), inner, measure), inner);
        y += 5;
        int titleAt = lines.Count;
        Put(Wrap(title, inner, measure), inner);
        y += 2;
        Put(Wrap(words, inner, measure), inner);
        float top = y + 4;
        // from the foot up: the help, a gap, the notes
        var foot = new List<string>();
        foreach (var n in notes) foot.AddRange(Wrap(n, inner, measure));
        var helpLines = Wrap(help, inner, measure);
        float footTop = scrap.Bottom - ScrapPadFoot - helpLines.Count * lineH - 5 - foot.Count * lineH;
        y = footTop;
        Put(foot, inner);
        y += 5;
        Put(helpLines, inner);
        var map = new Box(scrap.X + ScrapPadX - 4, top, scrap.W - 2 * ScrapPadX + 8, footTop - 4 - top);
        if (map.H < MinMapH) overflow.Add($"(the map: {map.H:0} px high)");
        return new ScrapPlan(scrap, lines, (count, scrap.Right - ScrapPadX - cw, scrap.Y + ScrapPadTop), map, overflow);
    }

    /// <summary>Words broken at spaces to a width; a single word wider than it stands alone (never cut).</summary>
    public static List<string> Wrap(string text, float w, Func<string, float> measure)
    {
        var o = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && measure(next) > w) { o.Add(line); line = word; }
            else line = next;
        }
        if (line.Length > 0) o.Add(line);
        return o;
    }

    // ---- the chosen job's map ----

    public sealed record MarkPlan(List<(float X, float Y, string Label)> Rings, List<Placed> Labels, List<string> Dropped);

    /// <summary>
    /// The job's marks on its small map (box pixels): marks within a ring's width of each other share one ring and one word ("CONVOY /
    /// EXIT"), each word beside its ring clear of the others' rings and words, inside <paramref name="bounds"/>.
    /// </summary>
    public static MarkPlan JobMarks(IReadOnlyList<(string Label, float X, float Y)> marks, Box bounds, Func<string, float> measure, float textH, float ring = 4, Func<int, int, bool>? inked = null)
    {
        var rings = new List<(float X, float Y, List<string> Labels)>();
        foreach (var (label, x, y) in marks)
        {
            int near = rings.FindIndex(r => MathF.Abs(r.X - x) < ring * 2 && MathF.Abs(r.Y - y) < ring * 2);
            var words = label.Split(" / ");
            if (near >= 0) { foreach (var w in words) if (!rings[near].Labels.Contains(w)) rings[near].Labels.Add(w); }
            else rings.Add((x, y, [.. words]));
        }
        var plan = new RegionPlan { Inked = inked };
        foreach (var r in rings) plan.Marks.Add(new Box(r.X - ring - 1, r.Y - ring - 1, 2 * ring + 2, 2 * ring + 2));
        // the crowded ones first: they have the fewest places left
        var order = rings.Select((r, i) => (r, i, crowd: rings.Count(o => MathF.Abs(o.X - r.X) + MathF.Abs(o.Y - r.Y) < 40))).OrderByDescending(x => x.crowd).ThenBy(x => x.i);
        foreach (var (r, _, _) in order)
        {
            // the shared ring's words together ("START / HOME") when short; long or with no place as one, each on its own beside the ring
            string joined = string.Join(" / ", r.Labels);
            if (r.Labels.Count <= 2 || measure(joined) <= 90) Place(r.X, r.Y, joined); else plan.Dropped.Add(joined);
            if (r.Labels.Count > 1 && plan.Dropped.Remove(string.Join(" / ", r.Labels)))
                foreach (var word in r.Labels) Place(r.X, r.Y, word);
        }
        return new MarkPlan([.. rings.Select(r => (r.X, r.Y, string.Join(" / ", r.Labels)))], plan.Labels, plan.Dropped);

        void Place(float rx, float ry, string text)
        {
            var r = (X: rx, Y: ry);
            float w = measure(text);
            var cands = new List<Box>();
            foreach (float g in new[] { ring + 2, ring + 8, ring + 16 })
            {
                cands.Add(new Box(r.X + g, r.Y - textH / 2, w, textH));
                cands.Add(new Box(r.X - g - w, r.Y - textH / 2, w, textH));
                cands.Add(new Box(r.X - w / 2, r.Y - g - textH, w, textH));
                cands.Add(new Box(r.X - w / 2, r.Y + g, w, textH));
                cands.Add(new Box(r.X + g * 0.7f, r.Y - g * 0.7f - textH, w, textH));
                cands.Add(new Box(r.X + g * 0.7f, r.Y + g * 0.7f, w, textH));
                cands.Add(new Box(r.X - g * 0.7f - w, r.Y - g * 0.7f - textH, w, textH));
                cands.Add(new Box(r.X - g * 0.7f - w, r.Y + g * 0.7f, w, textH));
            }
            Choose(plan, bounds, text, "mark", cands, needClear: false);
        }
    }

    // ---- an atlas under the region (optional, the player's own picture) ----

    /// <summary>
    /// A picture registered to the region by control points (latitude, longitude, picture x, picture y: one a line, # comments):
    /// an affine fit by least squares, three points or more. Null when there are too few or they are in a line.
    /// </summary>
    public sealed class AtlasFit
    {
        private readonly double[] _x, _y;
        public int Points { get; }
        /// <summary>The fit's misses at its own points, in picture pixels: the median and the largest.</summary>
        public double MedianMiss { get; }
        public double MaxMiss { get; }

        private AtlasFit(double[] x, double[] y, int n, double median, double max) { _x = x; _y = y; Points = n; MedianMiss = median; MaxMiss = max; }

        public (double X, double Y) ToPicture(double lat, double lon) => (_x[0] + _x[1] * lon + _x[2] * lat, _y[0] + _y[1] * lon + _y[2] * lat);

        public static AtlasFit? Parse(string text)
        {
            var pts = new List<double[]>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Split('#')[0].Trim();
                if (line.Length == 0) continue;
                var f = line.Split((char[])[' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 4) continue;
                var v = new double[4];
                bool ok = true;
                for (int i = 0; i < 4; i++) ok &= double.TryParse(f[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]);
                if (ok) pts.Add(v);
            }
            if (pts.Count < 3) return null;
            var x = Solve(pts, 2);
            var y = Solve(pts, 3);
            if (x is null || y is null) return null;
            var miss = pts.Select(p => Math.Sqrt(Math.Pow(x[0] + x[1] * p[1] + x[2] * p[0] - p[2], 2) + Math.Pow(y[0] + y[1] * p[1] + y[2] * p[0] - p[3], 2))).OrderBy(d => d).ToList();
            return new AtlasFit(x, y, pts.Count, miss[miss.Count / 2], miss[^1]);
        }

        /// <summary>Least squares for v = a + b lon + c lat (normal equations, 3 x 3).</summary>
        private static double[]? Solve(List<double[]> pts, int col)
        {
            var m = new double[3, 4];
            foreach (var p in pts)
            {
                double[] r = [1, p[1], p[0]];
                for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) m[i, j] += r[i] * r[j]; m[i, 3] += r[i] * p[col]; }
            }
            for (int c = 0; c < 3; c++)
            {
                int piv = c;
                for (int r = c + 1; r < 3; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
                if (Math.Abs(m[piv, c]) < 1e-12) return null;
                for (int j = 0; j < 4; j++) (m[c, j], m[piv, j]) = (m[piv, j], m[c, j]);
                for (int r = 0; r < 3; r++)
                {
                    if (r == c) continue;
                    double k = m[r, c] / m[c, c];
                    for (int j = 0; j < 4; j++) m[r, j] -= k * m[c, j];
                }
            }
            return [m[0, 3] / m[0, 0], m[1, 3] / m[1, 1], m[2, 3] / m[2, 2]];
        }
    }

}
