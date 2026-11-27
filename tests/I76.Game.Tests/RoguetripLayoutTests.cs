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
/// runs on every map, every word on the region inside its box and on no other word or mark (the towns the region names, the highway
/// numbers and the states all placed), every offer's words wrapped whole inside the column (the challenge number whole), the notes
/// whole at its foot, the job map's words clear of each other and its rings. Measured with the shell fonts the screen sets them in.
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

    [InstallFact]
    public void TheRegionsWordsNeverOverlapAndTheNamedOnesAreAllPlaced()
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
                    var plan = L.Region(reg, L.RegionBox, offers.Select(o => o.Map).ToList(), sel, run.Legs.Select(l => l.Map).ToList(), run.Map, t => f.Measure(t), f.Height, atlas);
                    NoOverlap(plan.Labels, plan.Marks, L.RegionBox, $"run {run.Number} map '{run.Map}' sel {sel}");
                    // the words the region always shows: the named towns, the through highways' numbers, the states (a quiet town's name or a short spur's number may go)
                    var must = atlas ? [] : reg.Towns.Where(t => !t.Quiet).Select(t => t.Name).Concat(reg.Highways.Where(h => h.Towns.Count >= 3).Select(h => h.Name)).Concat(reg.States.Select(s => s.Name));
                    foreach (var name in must) Assert.True(plan.Labels.Any(l => l.Text == name), $"run {run.Number} map '{run.Map}' sel {sel} atlas {atlas}: no place for {name}");
                    foreach (var d in plan.Dropped) dropped[d] = dropped.GetValueOrDefault(d) + 1;
                    // a word crossing a road is allowed only where no clear place was left
                    plans++;
                    labels += plan.Labels.Count;
                    onSmall += plan.Labels.Count(l => plan.Small.Any(m => m.Intersects(l.Rect)));
                }
        }
        output.WriteLine($"{plans} region plans, {labels} words placed ({onSmall} over a trip map's small ring or a town's dot, where nothing clear was left); left out: {string.Join(", ", dropped.Select(kv => $"{kv.Key} x{kv.Value}"))}");
    }

    [InstallFact]
    public void TheOffersAndNotesAreWrappedWholeInsideTheColumn()
    {
        var db = Db();
        var f = db.Font(L.TextFont);
        var nf = db.Font(L.MapFont);
        int cols = 0, maxLines = 0;
        float minMap = float.MaxValue;
        foreach (var run in Runs())
        {
            var offers = run.Offers(RemixNodes.Builtin());
            for (int sel = 0; sel < offers.Count; sel++)
            {
                var notes = run.Spec(offers[sel]).Text.Notes.Take(2).ToList();
                var col = L.Column(L.ColumnBox, offers.Select((o, i) => (RoguetripScreen.OfferTitle(o, i), o.Flavour)).ToList(), notes, t => f.Measure(t), L.OfferLine, L.NoteLine, t => nf.Measure(t), L.NoteLine);
                Assert.True(col.Overflow.Count == 0, $"run {run.Number}: {string.Join(" | ", col.Overflow)}");
                for (int i = 0; i < offers.Count; i++)
                {
                    var b = col.Offers[i];
                    Assert.True(b.Box.Within(L.ColumnBox), $"run {run.Number} offer {i} leaves the column");
                    foreach (var (t, x, y) in b.Lines.Take(b.TitleLines))
                        Assert.True(new L.Box(x, y, f.Measure(t), f.Height).Within(b.Box), $"run {run.Number}: '{t}' leaves its offer's box");
                    foreach (var (t, x, y) in b.Lines.Skip(b.TitleLines))
                        Assert.True(new L.Box(x, y, nf.Measure(t), nf.Height).Within(b.Box), $"run {run.Number}: '{t}' leaves its offer's box");
                    // nothing cut: the lines are the words, in order, every one
                    Assert.Equal(RoguetripScreen.OfferTitle(offers[i], i) + " " + offers[i].Flavour, string.Join(" ", b.Lines.Select(l => l.Text)));
                    Assert.Contains($"{offers[i].Challenge:0.0}", b.Lines.Take(b.TitleLines).Last().Text);
                    maxLines = Math.Max(maxLines, b.Lines.Count);
                }
                Assert.Equal(string.Join(" ", notes), string.Join(" ", col.Notes.Select(n => n.Text)));
                foreach (var (t, x, y) in col.Notes) Assert.True(new L.Box(x, y, nf.Measure(t), nf.Height).Within(L.ColumnBox), $"run {run.Number}: note '{t}' leaves the column");
                Assert.True(col.Map.H >= L.MinMapH);
                minMap = Math.Min(minMap, col.Map.H);
                cols++;
            }
            // the two lines over the sheet fit across it
            Assert.True(f.Measure($"Run {run.Number}: job {run.Legs.Count + 1} of 4. ABX Leprechaun: armour 100%, chassis 100%, ammo 100%.") <= L.Sheet.W);
        }
        output.WriteLine($"{cols} columns; at most {maxLines} lines an offer; the job map at least {minMap:0} px high");
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
                var col = L.Column(L.ColumnBox, offers.Select((o, i) => (RoguetripScreen.OfferTitle(o, i), o.Flavour)).ToList(), spec.Text.Notes.Take(2).ToList(), t => tf.Measure(t), L.OfferLine, L.NoteLine, t => f.Measure(t), L.NoteLine);
                // the sketch box as the screen sizes it (RoguetripScreen.DrawJobMap)
                int w = (int)col.Map.W - 2 * 5 - 2, h = (int)col.Map.H - 2 * 5 - 7 - 6;
                if (L.JobMap(TestInstall.Install, spec, w, h, t => f.Measure(t), f.Height) is not { } jm) continue;
                Assert.True(jm.Marks.Dropped.Count == 0, $"run {run.Number} {offers[sel].Map} {offers[sel].Template}: no place for {string.Join(", ", jm.Marks.Dropped)}");
                NoOverlap(jm.Marks.Labels, jm.Marks.Rings.Select(r => new L.Box(r.X - 5, r.Y - 5, 10, 10)), new L.Box(0, 0, w, h), $"run {run.Number} {offers[sel].Map}");
                maps++;
                words += jm.Marks.Labels.Count;
            }
        }
        output.WriteLine($"{maps} job maps, {words} words");
        Assert.True(maps > 40);
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
        var reg = RemixRegion.Builtin();
        var p = reg.Project([32.5, -103.2], 304, 334);
        var (lat, lon) = L.Unproject(reg, p.X, p.Y, 304, 334);
        Assert.Equal(32.5, lat, 6);
        Assert.Equal(-103.2, lon, 6);
    }
}
