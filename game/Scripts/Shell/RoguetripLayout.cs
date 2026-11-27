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
        public float Radius => Selected ? 12 : 8;
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

    /// <summary>The gap between a mark and its word.</summary>
    private const float Gap = 3;

    /// <summary>The region's box on the sheet, and the column of offers, the chosen job's map and its notes beside it.</summary>
    public static readonly Box RegionBox = new(28, 76, 304, 334), ColumnBox = new(344, 76, 260, 346);
    /// <summary>The sheet inside its double rule: no word may leave it.</summary>
    public static readonly Box Sheet = new(26, 42, 588, 392);
    /// <summary>The shell fonts: the region's, the job map's and the notes' words (the small sans, 10 px), the offers' (the small hand, 12 px).</summary>
    public const int MapFont = 0x47, TextFont = 0x44;
    public const float OfferLine = 13, NoteLine = 11;

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
    public static RegionPlan Region(RemixRegion reg, Box box, IReadOnlyList<string> offerMaps, int sel, IReadOnlyList<string> visited, string here,
        Func<string, float> measure, float textH, bool backdrop = false)
    {
        var plan = new RegionPlan();
        (float X, float Y) At(double[] latLon)
        {
            var (x, y) = reg.Project(latLon, box.W, box.H);
            return (box.X + (float)x, box.Y + (float)y);
        }
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
        // the trip's maps: small rings (filled where the run has been)
        foreach (var (map, at) in reg.Maps)
        {
            var p = At(at);
            // (under an offer's ring its number stands for it)
            if (!new Box(p.X, p.Y, 0, 0).Within(box) || offerMaps.Contains(map, StringComparer.OrdinalIgnoreCase)) continue;
            plan.Dots.Add(new Dot(p.X, p.Y, visited.Contains(map, StringComparer.OrdinalIgnoreCase) ? "visited" : "map"));
            plan.Small.Add(new Box(p.X - 3, p.Y - 3, 6, 6));
        }
        if (backdrop) return plan;
        var towns = new List<(RegionLabel Town, float X, float Y)>();
        foreach (var t in reg.Towns)
        {
            var p = At(t.At);
            if (!new Box(p.X - 1, p.Y - 1, 3, 3).Within(box)) continue;
            towns.Add((t, p.X, p.Y));
            // (a town under an offer's ring keeps its name but not its dot: the ring's number is there)
            if (plan.Offers.Any(o => MathF.Abs(o.X - p.X) < o.Radius && MathF.Abs(o.Y - p.Y) < o.Radius)) continue;
            plan.Dots.Add(new Dot(p.X, p.Y, "town"));
            plan.Small.Add(new Box(p.X - 1.5f, p.Y - 1.5f, 3, 3));
        }
        // the words, the most needed first: the towns the region names, the highways' numbers, the states, then the quiet towns
        foreach (var (t, x, y) in towns.Where(t => !t.Town.Quiet)) PlaceAt(plan, box, t.Name, "town", x, y, measure(t.Name), textH, needClear: false);
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
        foreach (var (t, x, y) in towns.Where(t => t.Town.Quiet)) PlaceAt(plan, box, t.Name, "quiet", x, y, measure(t.Name), textH, needClear: true);
        return plan;
    }

    /// <summary>A word beside a point: right, left, above, below, then the corners (and the same again further out).</summary>
    private static void PlaceAt(RegionPlan plan, Box box, string text, string kind, float x, float y, float w, float h, bool needClear)
    {
        var cands = new List<Box>();
        foreach (float g in new[] { Gap, Gap + 5, Gap + 11, Gap + 17 })
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
        Choose(plan, box, text, kind, cands, needClear);
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
    private static void Choose(RegionPlan plan, Box box, string text, string kind, List<Box> cands, bool needClear)
    {
        Box? best = null;
        int bestCost = int.MaxValue;
        for (int i = 0; i < cands.Count; i++)
        {
            var c = cands[i];
            if (!c.Within(box) || plan.Labels.Any(l => l.Rect.Grow(1).Intersects(c)) || plan.Marks.Any(m => m.Intersects(c))) continue;
            int crossings = plan.Segments.Count(s => Crosses(s, c.Grow(-0.5f))), small = plan.Small.Count(m => m.Intersects(c));
            if (needClear && crossings + small > 0) continue;
            int ink = 0;
            if (plan.Inked is { } inkedAt)
                for (int y = (int)c.Y; y < (int)c.Bottom; y++) for (int x = (int)c.X; x < (int)c.Right; x++) if (inkedAt(x, y)) ink++;
            int cost = small * 10000 + crossings * 100 + ink * 8 + i;
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

    // ---- the offers' column ----

    /// <summary>An offer's box and its lines (each at its top left).</summary>
    public sealed record OfferBlock(Box Box, List<(string Text, float X, float Y)> Lines, int TitleLines);

    public sealed record ColumnPlan(List<OfferBlock> Offers, Box Map, List<(string Text, float X, float Y)> Notes, List<string> Overflow);

    /// <summary>The smallest the chosen job's map may be squeezed to.</summary>
    public const float MinMapH = 56;

    /// <summary>
    /// The column: each offer's title and words wrapped whole (no word cut, the challenge number whole) inside its box, the boxes one
    /// under the other from the column's top; the chosen job's notes wrapped whole at the column's foot; the job's map between.
    /// Anything that still does not fit is named in Overflow (the test holds it empty).
    /// </summary>
    public static ColumnPlan Column(Box col, IReadOnlyList<(string Title, string Words)> offers, IReadOnlyList<string> notes, Func<string, float> measure, float lineH, float noteH, Func<string, float>? noteMeasure = null, float wordH = 0)
    {
        // the offers' titles in the measure's font, their words and the notes in the note font (wordH a line)
        noteMeasure ??= measure;
        if (wordH <= 0) wordH = noteH;
        var blocks = new List<OfferBlock>();
        var overflow = new List<string>();
        float y = col.Y;
        const float Pad = 12;
        float inner = col.W - 2 * Pad;
        foreach (var (title, words) in offers)
        {
            var lines = new List<(string, float, float)>();
            var t = Wrap(title, inner, measure);
            var f = Wrap(words, inner, noteMeasure);
            float ly = y + 4;
            foreach (var s in t) { lines.Add((s, col.X + Pad, ly)); ly += lineH; }
            ly += 2;
            foreach (var s in f) { lines.Add((s, col.X + Pad, ly)); ly += wordH; }
            var b = new Box(col.X, y, col.W, ly + 3 - y);
            blocks.Add(new OfferBlock(b, lines, t.Count));
            y = b.Bottom + 4;
            foreach (var s in t) if (measure(s) > inner + 0.01f) overflow.Add(s);
            foreach (var s in f) if (noteMeasure(s) > inner + 0.01f) overflow.Add(s);
        }
        var noteLines = notes.SelectMany(n => Wrap(n, col.W, noteMeasure)).ToList();
        float notesTop = col.Bottom - noteLines.Count * noteH;
        var placedNotes = noteLines.Select((s, i) => (s, col.X, notesTop + i * noteH)).ToList();
        foreach (var s in noteLines) if (noteMeasure(s) > col.W + 0.01f) overflow.Add(s);
        var map = new Box(col.X, y + 2, col.W, notesTop - 4 - (y + 2));
        if (map.H < MinMapH) overflow.Add($"(the map: {map.H:0} px high)");
        return new ColumnPlan(blocks, map, placedNotes, overflow);
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

    /// <summary>The inverse of <see cref="RemixRegion.Project"/>: the latitude and longitude at (x, y) of a box w x h.</summary>
    public static (double Lat, double Lon) Unproject(RemixRegion reg, double x, double y, double w, double h)
    {
        var b = reg.Bounds;
        double mid = (b.North + b.South) / 2 * Math.PI / 180;
        double k = I76.Formats.DetMath.Cos(mid);
        double rw = (b.East - b.West) * k, rh = b.North - b.South;
        double s = Math.Min(w / rw, h / rh);
        double ox = (w - rw * s) / 2, oy = (h - rh * s) / 2;
        return (b.North - (y - oy) / s, b.West + (x - ox) / (k * s));
    }
}
