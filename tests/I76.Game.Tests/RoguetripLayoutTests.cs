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

    [InstallFact]
    public void TheShownOffersWordsAreWholeOnItsScrap()
    {
        var f = Db().Font(L.TextFont);
        int scraps = 0, maxLines = 0;
        float minMap = float.MaxValue;
        foreach (var run in Runs())
        {
            var offers = run.Offers(RemixNodes.Builtin());
            for (int sel = 0; sel < offers.Count; sel++)
            {
                var o = offers[sel];
                var notes = Notes(run.Spec(o));
                string runLine = $"Run {run.Number}, job {run.Legs.Count + 1} of 4 (1 busted). ABX Leprechaun: armour 100%, chassis 100%, ammo 100%. Leaving Fort Stockton and the Pecos.";
                foreach (var help in new[] { "Arrows, 1-3 or a ring: choose. Enter: take it. Esc: back.", "The host chooses (the red ring). Esc: back to the lobby." })
                {
                    var sp = L.Scrap(L.ScrapBox, runLine, $"{sel + 1} of {offers.Count}", RoguetripScreen.OfferTitle(o, sel), o.Flavour, notes, help, t => f.Measure(t), L.OfferLine);
                    string what = $"run {run.Number} offer {sel + 1}";
                    Assert.True(sp.Overflow.Count == 0, $"{what}: {string.Join(" | ", sp.Overflow)}");
                    var inner = L.ScrapBox.Grow(-8);
                    var boxes = sp.Lines.Select(l => new L.Box(l.X, l.Y, f.Measure(l.Text), f.Height)).ToList();
                    boxes.Add(new L.Box(sp.Count.X, sp.Count.Y, f.Measure(sp.Count.Text), f.Height));
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        Assert.True(boxes[i].Within(inner), $"{what}: a line leaves the scrap ({boxes[i]})");
                        Assert.False(boxes[i].Intersects(sp.Map), $"{what}: a line on the job's map");
                        for (int j = i + 1; j < boxes.Count; j++) Assert.False(boxes[i].Grow(-0.5f).Intersects(boxes[j].Grow(-0.5f)), $"{what}: two lines overlap");
                    }
                    Assert.True(sp.Map.Within(L.ScrapBox) && sp.Map.H >= L.MinMapH);
                    // nothing cut: every word of the title, the offer, the notes and the help is there, in order
                    string all = string.Join(" ", sp.Lines.Select(l => l.Text));
                    Assert.Equal(string.Join(" ", new[] { runLine, RoguetripScreen.OfferTitle(o, sel), o.Flavour }.Concat(notes).Append(help).SelectMany(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries))), all);
                    Assert.Contains($"{o.Challenge:0.0}", all);
                    maxLines = Math.Max(maxLines, sp.Lines.Count);
                    minMap = Math.Min(minMap, sp.Map.H);
                    scraps++;
                }
            }
        }
        output.WriteLine($"{scraps} scraps; at most {maxLines} lines; the job map at least {minMap:0} px high");
    }

    [InstallFact]
    public void TheJobMapsMarksAreLetteredClearOfEachOther()
    {
        var f = Db().Font(L.MapFont);
        var tf = Db().Font(L.TextFont);
        int maps = 0, words = 0;
        foreach (var run in Runs().Where((_, i) => i % 2 == 0))
        {
            var offers = run.Offers(RemixNodes.Builtin());
            for (int sel = 0; sel < offers.Count; sel++)
            {
                var spec = run.Spec(offers[sel]);
                var sp = L.Scrap(L.ScrapBox, $"Run {run.Number}, job 1 of 4. ABX Leprechaun: the car as built. Pick where the run starts.", "1 of 3",
                    RoguetripScreen.OfferTitle(offers[sel], sel), offers[sel].Flavour, Notes(spec), "Arrows, 1-3 or a ring: choose. Enter: take it. Esc: back.", t => tf.Measure(t), L.OfferLine);
                // the sketch's box as the screen sizes it (RoguetripScreen.DrawOfferScrap)
                int w = (int)sp.Map.W, h = (int)sp.Map.H - 7 - 6;
                if (L.JobMap(TestInstall.Install, spec, w, h, t => f.Measure(t) * L.MapScale, L.MapCell) is not { } jm) continue;
                Assert.True(jm.Marks.Dropped.Count == 0, $"run {run.Number} {offers[sel].Map} {offers[sel].Template}: no place for {string.Join(", ", jm.Marks.Dropped)}");
                NoOverlap(jm.Marks.Labels, jm.Marks.Rings.Select(r => new L.Box(r.X - 5, r.Y - 5, 10, 10)), new L.Box(0, 0, w, h), $"run {run.Number} {offers[sel].Map}");
                maps++;
                words += jm.Marks.Labels.Count;
            }
        }
        output.WriteLine($"{maps} job maps, {words} words");
        Assert.True(maps > 40);
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
