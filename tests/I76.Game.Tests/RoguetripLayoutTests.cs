using I76.Formats.Remix;
using I76.Formats.Shell;
using I76.Formats.Tests;
using I76.Game.Shell;
using Xunit;
using Xunit.Abstractions;
using L = I76.Game.Shell.RoguetripLayout;

namespace I76.Game.Tests;

/// <summary>
/// ROGUETRIP's choice screen lays out its words without overlap or truncation (docs/remix-missions.md "The choice map"): over rolled
/// runs on every map, every word on the map inside the sheet and on no other word or mark (the towns the region names, the highway
/// numbers and the states all placed), the shown offer's scrap over none of the rings or the route, its words wrapped whole on it (the
/// challenge number whole) clear of its job map, the job map's words clear of each other and its rings. Measured with the shell fonts the screen sets them in.
/// </summary>
public class RoguetripLayoutTests(ITestOutputHelper output)
{
    private static ShellDatabase Db() => ShellDatabase.Open(TestInstall.Install.FindFile("DATABASE.MW2")!);

    private static void NoOverlap(IReadOnlyList<L.Placed> labels, IEnumerable<L.Box> marks, L.Box box, string what)
    {
        var m = marks.ToList();
        for (int i = 0; i < labels.Count; i++)
        {
            Assert.True(labels[i].Rect.Within(box), $"{what}: '{labels[i].Text}' leaves its box ({labels[i].Rect})");
            for (int j = i + 1; j < labels.Count; j++)
                Assert.False(labels[i].Rect.Intersects(labels[j].Rect), $"{what}: '{labels[i].Text}' on '{labels[j].Text}'");
            foreach (var k in m) Assert.False(labels[i].Rect.Intersects(k), $"{what}: '{labels[i].Text}' on a mark at {k}");
        }
    }

    /// <summary>The states a choice screen is drawn in: new runs, and runs a job or two in on every map.</summary>
    private static IEnumerable<RemixRun> Runs()
    {
        for (int n = 1; n <= 40; n++) yield return RemixRun.Start(n * 7919, "vppirna1.vcf", "", "", 0);
        int k = 0;
        foreach (var t in RemixNodes.Builtin().Where(t => !t.Nitro))
        {
            var run = RemixRun.Start(1000 + k++, "vppirna1.vcf", "", "", 0);
            var prev = t.Neighbours.FirstOrDefault() ?? t.Base;
            run.Legs.Add(new RunLeg { Map = prev, Template = "raid", Won = true, Outcome = "won" });
            run.Legs.Add(new RunLeg { Map = t.Base, Template = "deliver", Won = true, Outcome = "won" });
            run.Map = t.Base;
            run.Won = 2;
            yield return run;
        }
    }

    /// <summary>The notes and objectives the scrap shows for an offer (RoguetripScreen.Notes).</summary>
    private static List<string> Notes(RemixSpec spec) =>
        [.. spec.Text.Objectives.Skip(1).Where(o => !o.StartsWith("(hidden)", StringComparison.Ordinal)), .. spec.Text.Notes.Take(2)];

    [InstallFact]
    public void TheRegionsWordsNeverOverlapAndTheScrapNeverCoversTheRingsOrTheRoute()
    {
        var f = Db().Font(L.MapFont);
        var reg = RemixRegion.Builtin();
        int plans = 0, labels = 0, onSmall = 0;
        var dropped = new Dictionary<string, int>();
        foreach (var run in Runs())
        {
            var offers = run.Offers(RemixNodes.Builtin());
            Assert.NotEmpty(offers);
            foreach (bool atlas in new[] { false, true })
                for (int sel = 0; sel < offers.Count; sel++)
                {
                    var plan = L.Region(reg, L.View, offers.Select(o => o.Map).ToList(), sel, run.Legs.Select(l => l.Map).ToList(), run.Map, t => f.Measure(t) * L.MapScale, L.MapCell, atlas, L.ScrapBox);
                    string what = $"run {run.Number} map '{run.Map}' sel {sel}";
                    NoOverlap(plan.Labels, plan.Marks, L.MapBox, what);
                    // the scrap covers no ring (the shown one least of all), no part of the run's road or the way to the shown offer
                    foreach (var o in plan.Offers)
                    {
                        var ring = new L.Box(o.X - o.Radius - 1, o.Y - o.Radius - 1, 2 * o.Radius + 2, 2 * o.Radius + 2);
                        Assert.False(ring.Intersects(L.ScrapBox), $"{what}: the scrap covers offer {o.Index + 1}'s ring");
                        Assert.True(ring.Within(L.MapBox), $"{what}: offer {o.Index + 1}'s ring leaves the map");
                    }
                    foreach (var s in plan.Segments.Where(s => s.Kind is L.Ink.Run or L.Ink.Approach))
                        Assert.False(L.Crosses(s, L.ScrapBox), $"{what}: the scrap covers the route");
                    // the words the map always shows: the named towns clear of the scrap, the through highways' numbers, the states
                    var must = atlas ? [] : reg.Towns.Where(t => !t.Quiet && L.View.Project(t.At).X < L.ScrapBox.X - 80).Select(t => L.Printed(t.Name))
                        .Concat(reg.Highways.Where(h => h.Towns.Count >= 3).Select(h => h.Name)).Concat(reg.States.Select(s => s.Name));
                    foreach (var name in must) Assert.True(plan.Labels.Any(l => l.Text == name), $"{what} atlas {atlas}: no place for {name}");
                    foreach (var d in plan.Dropped) dropped[d] = dropped.GetValueOrDefault(d) + 1;
                    plans++;
                    labels += plan.Labels.Count;
                    onSmall += plan.Labels.Count(l => plan.Small.Any(m => m.Intersects(l.Rect)));
                }
        }
        output.WriteLine($"{plans} region plans, {labels} words placed ({onSmall} over a trip map's small ring or a town's dot, where nothing clear was left); left out: {string.Join(", ", dropped.Select(kv => $"{kv.Key} x{kv.Value}"))}");
    }

    /// <summary>
    /// The offer's page: the job's map over the right half, its marks' words clear of each other, its rings and its routes, and the
    /// notepad slip on it covering no mark, no mark's word and no route, inside the page, every word of it whole; over every rolled
    /// offer (all sixteen maps' job maps), in the layout chosen and in the alternative (the slip in a band under the map).
    /// </summary>
    [InstallFact]
    public void TheOffersPageKeepsItsNotepadOffTheMarksAndTheRoutes()
    {
        var db = Db();
        var small = db.Font(L.TextFont);
        var big = db.Font(L.TitleFont);
        var map = db.Font(L.MapFont);
        int pages = 0, band = 0, words = 0, besideDropped = 0;
        var inkShare = new List<float>();
        float minMap = float.MaxValue;
        var bases = new HashSet<string>();
        var extremes = new List<(float Aspect, string What)>();
        foreach (var run in Runs())
        {
            var offers = run.Offers(RemixNodes.Builtin());
            for (int sel = 0; sel < offers.Count; sel++)
            {
                var o = offers[sel];
                var spec = run.Spec(o);
                var made = new Dictionary<(int, int), (I76.Formats.Mission.ArenaSketch Sketch, L.MarkPlan Marks)?>();
                (I76.Formats.Mission.ArenaSketch Sketch, L.MarkPlan Marks)? Map(int w, int h) =>
                    made.TryGetValue((w, h), out var m) ? m : made[(w, h)] = L.JobMap(TestInstall.Install, spec, w, h, t => map.Measure(t) * L.JobScale, L.JobCell, L.JobRing);
                string runLine = $"Run {run.Number}, job {run.Legs.Count + 1} of 4 (1 busted). ABX Leprechaun: armour 100%, chassis 100%, ammo 100%. Leaving Fort Stockton.";
                foreach (bool beside in new[] { false, true })
                {
                    var page = L.Page(L.PageArea, Map, RoguetripScreen.OfferTitle(o, sel), $"{sel + 1} of {offers.Count}", o.Flavour, Notes(spec), runLine,
                        t => big.Measure(t), t => small.Measure(t), beside);
                    string what = $"run {run.Number} offer {sel + 1} ({o.Map} {o.Template}){(beside ? " beside" : "")}";
                    Assert.True(page.Overflow.Count == 0, $"{what}: {string.Join(" | ", page.Overflow)}");
                    Assert.True(page.Slip.Within(L.PageArea), $"{what}: the slip leaves the page");
                    foreach (var l in page.Lines)
                        Assert.True(new L.Box(l.X, l.Y, (l.Big ? big : small).Measure(l.Text), l.Big ? big.Height : small.Height - 1).Within(page.Slip), $"{what}: '{l.Text}' leaves the slip");
                    Assert.True(new L.Box(page.Count.X, page.Count.Y, small.Measure(page.Count.Text), small.Height).Within(page.Slip));
                    // nothing cut: the title, the offer, the notes, the run's line and the help, every word, in order
                    Assert.Equal(string.Join(" ", new[] { runLine, RoguetripScreen.OfferTitle(o, sel), o.Flavour }.Concat(Notes(spec)).SelectMany(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries))),
                        string.Join(" ", page.Lines.Select(l => l.Text)));
                    if (Map((int)page.SketchBox.W, (int)page.SketchBox.H) is { } m)
                    {
                        bases.Add(o.Map);
                        var (keep, routes) = L.Obstacles(m.Marks, page.SketchBox);
                        var (cx0, cy0, cx1, cy1) = L.Content(m.Sketch, m.Marks);
                        Assert.True(new L.Box(page.SketchBox.X + cx0, page.SketchBox.Y + cy0, cx1 - cx0, cy1 - cy0).Within(L.PageArea.Grow(0.5f)), $"{what}: the map leaves the page");
                        Assert.False(keep.Any(k => k.Grow(-1.5f).Intersects(page.Slip)), $"{what}: the slip covers a mark or its word");
                        Assert.False(routes.Any(r => L.Crosses(r, page.Slip)), $"{what}: the slip covers a route");
                        // (the alternative's smaller map may have to leave a crowded mark's word out: counted, not held)
                        if (beside) besideDropped += m.Marks.Dropped.Count;
                        else Assert.True(m.Marks.Dropped.Count == 0, $"{what} band {page.Band} map {page.SketchBox.H:0}: no place for {string.Join(", ", m.Marks.Dropped)}");
                        NoOverlap(m.Marks.Labels, m.Marks.Rings.Select(r => new L.Box(r.X - L.JobRing - 1, r.Y - L.JobRing - 1, 2 * L.JobRing + 2, 2 * L.JobRing + 2)), new L.Box(0, 0, page.SketchBox.W, page.SketchBox.H), what);
                        if (!beside && m.Sketch.InkBounds() is var (x0, y0, x1, y1)) extremes.Add(((x1 - x0 + 1f) / (y1 - y0 + 1f), $"run {run.Number} offer {sel + 1} {o.Map}"));
                        words += m.Marks.Labels.Count;
                    }
                    if (!beside)
                    {
                        pages++;
                        if (page.Band) band++;
                        minMap = Math.Min(minMap, page.SketchBox.H);
                        // laid on the map, the map has the whole page (at least 55 % of it in any case)
                        Assert.True(page.SketchBox.W * page.SketchBox.H >= 0.55f * L.PageArea.W * L.PageArea.H || page.Band, $"{what}: the map has {page.SketchBox.W * page.SketchBox.H / (L.PageArea.W * L.PageArea.H):P0} of the page");
                        if (Map((int)page.SketchBox.W, (int)page.SketchBox.H) is { } dm && dm.Sketch.InkBounds() is var (ix0, iy0, ix1, iy1))
                            inkShare.Add((ix1 - ix0 + 1f) * (iy1 - iy0 + 1f) / (L.PageArea.W * L.PageArea.H));
                    }
                }
            }
        }
        extremes.Sort();
        output.WriteLine($"{pages} pages ({band} with the slip in a band of its own), {words} mark words, maps {string.Join(" ", bases.OrderBy(b => b))}; the map at least {minMap:0} px high; the alternative left out {besideDropped} mark words; the map's drawing covers {inkShare.Order().ElementAt(inkShare.Count / 2):P0} of the page (median), {inkShare.Min():P0} at the least");
        output.WriteLine($"tallest: {extremes[0].What} ({extremes[0].Aspect:0.00}); widest: {extremes[^1].What} ({extremes[^1].Aspect:0.00})");
        Assert.Equal(16, bases.Count);
    }

    /// <summary>
    /// The job's whole map as the loading card and the map (M) letter it (MissionPicture.LabelPlan, the routine the choice screen's
    /// page uses): over every rolled job on all sixteen maps, and the owner's packed case (run 11, job 2: a T02 convoy hit with START /
    /// HOME, CONVOY and EXIT in a row on the route and two landmarks beside it), no word on another word, a ring, a landmark's square or
    /// the route between the marks, every mark's word placed (a landmark's may yield), all inside the sheet. Measured with the shell's
    /// hand (font 0x45: the install's Lee at 15 px, the map's mark size; landmarks at 13 px).
    /// </summary>
    [InstallFact]
    public void TheWholeMapsWordsNeverPileUp()
    {
        var lee = Db().Font(L.TitleFont);
        float MarkW(string t) => lee.Measure(t);
        float LandW(string t) => lee.Measure(t) * 13f / 15f;
        int maps = 0, words = 0, leaders = 0, landDropped = 0;
        var bases = new HashSet<string>();
        var jobs = Runs().SelectMany(run => run.Offers(RemixNodes.Builtin()).Select(o => (What: $"run {run.Number} {o.Map} {o.Template}", Spec: run.Spec(o)))).ToList();
        var owner = RemixRun.Walk(11, "2,0");
        jobs.Insert(0, ("the owner's: run 11 job 2", owner.Spec));
        foreach (var (what, spec) in jobs)
        {
            if (L.Sketch(TestInstall.Install, spec, 560, 392) is not { } d) continue;
            var marks = d.Marks.Select(m => (new Godot.Vector2(40 + m.X, 56 + m.Y), m.Label)).ToList();
            var land = d.Landmarks.Select(m => (new Godot.Vector2(40 + m.X, 56 + m.Y), m.Label)).ToList();
            var routes = d.Routes.Select(r => r.Select(p => new Godot.Vector2(40 + p.X, 56 + p.Y)).ToArray()).ToList();
            var plan = I76.Game.Ui.MissionPicture.LabelPlan(marks, land, routes, MarkW, lee.Height, LandW, lee.Height * 13f / 15f, (x, y) => d.Sketch.Inked(x - 40, y - 56));
            var ringBoxes = plan.Rings.Select(r => new L.Box(r.X - I76.Game.Ui.MissionPicture.MarkRing, r.Y - I76.Game.Ui.MissionPicture.MarkRing, 2 * I76.Game.Ui.MissionPicture.MarkRing, 2 * I76.Game.Ui.MissionPicture.MarkRing));
            var squares = plan.Landmarks.Select(l => new L.Box(l.X - 3, l.Y - 3, 6, 6));
            NoOverlap(plan.Labels, ringBoxes.Concat(squares), new L.Box(30, 62, 580, 388), what);
            foreach (var l in plan.Labels.Where(l => l.Kind != "landmark"))
                foreach (var rt in routes)
                    for (int i = 0; i + 1 < rt.Length; i++)
                        Assert.False(L.Crosses(new L.Segment(rt[i].X, rt[i].Y, rt[i + 1].X, rt[i + 1].Y, L.Ink.Approach), l.Rect.Grow(-1)), $"{what}: '{l.Text}' on the route");
            // every mark's word placed (merged where marks share a place); a landmark's may yield
            Assert.True(plan.Dropped.All(t => d.Landmarks.Any(l => l.Label == t)), $"{what}: no place for {string.Join(", ", plan.Dropped)}");
            landDropped += plan.Dropped.Count;
            leaders += plan.Labels.Count(l => L.Leader(l, l.Kind == "landmark" ? 4 : I76.Game.Ui.MissionPicture.MarkRing) is not null);
            words += plan.Labels.Count;
            bases.Add(spec.Base);
            maps++;
        }
        output.WriteLine($"{maps} whole maps on {bases.Count} maps, {words} words ({leaders} with a leader), {landDropped} landmark names left out");
        Assert.Equal(16, bases.Count);
    }

    /// <summary>
    /// With no atlas, or one still being read, or one that cannot be read, the region is the drawn map (its lines and every named
    /// word); only a ready atlas takes their place.
    /// </summary>
    [Fact]
    public void WithoutAReadyAtlasTheDrawnMapShows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "i26-atlas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(L.AtlasState.Absent, RoguetripAtlas.State(dir, start: false));
            // a picture without its control points is no atlas
            File.WriteAllBytes(Path.Combine(dir, RoguetripAtlas.Picture + ".png"), [0]);
            Assert.Equal(L.AtlasState.Absent, RoguetripAtlas.State(dir, start: false));
            File.WriteAllText(Path.Combine(dir, RoguetripAtlas.Points), "33 -104 0 0\n");
            Assert.Equal(L.AtlasState.Loading, RoguetripAtlas.State(dir, start: false));
        }
        finally { Directory.Delete(dir, true); }
        var reg = RemixRegion.Builtin();
        foreach (var state in Enum.GetValues<L.AtlasState>())
        {
            var plan = L.Region(reg, L.View, ["t10"], 0, [], "", t => t.Length * 6f, 10, L.AtlasShown(state), L.ScrapBox);
            bool drawn = plan.Segments.Any(s => s.Kind == L.Ink.Highway) && reg.Towns.Where(t => !t.Quiet && L.View.Project(t.At).X < L.ScrapBox.X - 60).All(t => plan.Labels.Any(l => l.Text == L.Printed(t.Name)));
            Assert.Equal(state != L.AtlasState.Ready, drawn);
            Assert.NotEmpty(plan.Offers);
        }
    }

    [Fact]
    public void AnAtlasIsRegisteredByItsControlPoints()
    {
        // three points of a made-up picture, 100 px a degree, north up: the fit gives them back and a fourth in between
        var fit = L.AtlasFit.Parse("# lat lon x y\n33 -104 0 300\n33 -101 300 300\n31 -104 0 500\n");
        Assert.NotNull(fit);
        var (x, y) = fit!.ToPicture(32, -102.5);
        Assert.Equal(150, x, 3);
        Assert.Equal(400, y, 3);
        Assert.Null(L.AtlasFit.Parse("33 -104 0 300\n33 -101 300 300\n"));
        // the region's projection and its inverse agree
        var p = L.View.Project([32.5, -103.2]);
        var (lat, lon) = L.View.Unproject(p.X, p.Y);
        Assert.Equal(32.5, lat, 4);
        Assert.Equal(-103.2, lon, 4);
        // the view fills the sheet: the region's north and south edges on its top and foot, every trip map left of the scrap
        var reg = RemixRegion.Builtin();
        Assert.Equal(L.MapBox.Y, L.View.Project([reg.Bounds.North, -103]).Y, 2);
        Assert.Equal(L.MapBox.Bottom, L.View.Project([reg.Bounds.South, -103]).Y, 2);
        Assert.All(reg.Maps.Values, m => Assert.InRange(L.View.Project(m).X, L.MapBox.X + L.OuterRing, L.ScrapBox.X - L.OuterRing));
    }
}
