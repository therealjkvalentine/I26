using I76.Formats.Input;
using I76.Game.Shell;
using Xunit;
using Xunit.Abstractions;

namespace I76.Game.Tests;

/// <summary>
/// The base game's network host form (the play fork's layout, HostFormLayout): the original's art with the options box and the
/// rules table; a labelled row for every rule, the form's own hotspot ids, the pad reaching them all. Upstream's tests for its
/// drawn sheet (56373bd), adapted where this form differs: the art's own arrows are smaller than a sheet's row, a check box's
/// value is its X, and the labels are this form's.
/// </summary>
public class HostFormLayoutTests(ITestOutputHelper output)
{
    private static readonly HostFormLayout.State Melee = new()
    {
        MaxScore = 20000, BotSkill = "Normal", Rate = 20, ShareMaps = true, CarReset = true, Cb = true,
    };
    private static readonly HostFormLayout.State Coop = Melee with { Coop = true };

    // the ids the form had before upstream drew it, less 7 (the original's driver box, which on a host form only showed the host's own car
    // again), and the play fork's 39 (the ignition lock), 43 (Handicaps), 40..42 (the co-op form's Co-op Scaling, Co-op Damage, Rolling Start);
    // the same sets as upstream's sheet has
    private static readonly int[] MeleeIds = [3, 4, 5, 6, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 20, 21, 22, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 43];
    private static readonly int[] CoopIds = [3, 4, 5, 6, 8, 9, 11, 12, 13, 14, 15, 17, 22, 27, 28, 32, 33, 35, 36, 37, 38, 39, 40, 41, 42, 43];

    private static string Value(HostFormLayout.State s, string label) => HostFormLayout.Fields(s).Single(f => f.Label == label).Value;

    [Fact]
    public void EveryRuleHasALabelledRowOfItsOwn()
    {
        var fields = HostFormLayout.Fields(Melee);
        string[] table = ["SIM RATE", "UNLIMITED TURRETS", "IGNITION LOCK", "HANDICAPS", "PASSWORD", "CHASE CROSSHAIR"];
        Assert.Equal(table, fields.Where(f => f.Where == HostFormLayout.Place.Table).OrderBy(f => f.Row).Select(f => f.Label));
        string[] checks = ["TEAM PLAY", "INVITE ONLY", "SHARE MAPS", "HIDDEN CARS", "CAR RESET", "CB RADIO"];
        Assert.Equal(checks.Order(), fields.Where(f => f.Where is HostFormLayout.Place.CheckA or HostFormLayout.Place.CheckB).Select(f => f.Label).Order());
        // one field a place: no two in a table row, a box row's column, or the box's stepped lines (BOTS, MAX SCORE)
        var places = fields.Select(f => (f.Where == HostFormLayout.Place.BoxRow ? HostFormLayout.Place.CheckA : f.Where, f.Row)).ToList();
        Assert.Equal(places.Count, places.Distinct().Count());
        Assert.DoesNotContain(fields, f => f.Where != HostFormLayout.Place.Table && f.Row is HostFormLayout.BotsRow or HostFormLayout.ScoreRow);
        Assert.All(fields.Where(f => f.Where == HostFormLayout.Place.Table), f => Assert.InRange(f.Row, 0, HostFormLayout.TableRows - 1));
        Assert.All(fields.Where(f => f.Where != HostFormLayout.Place.Table), f => Assert.InRange(f.Row, 0, HostFormLayout.BoxRow.Length - 1));
        // the table is above the arena sketch's room, the box inside its rules
        Assert.True(HostFormLayout.TableFoot <= 183);
        var (_, _, by0, by1) = HostFormLayout.BoxInside;
        Assert.All(HostFormLayout.BoxRow, b => Assert.True(b - 14 >= by0 - 1 && b + 5 <= by1 + 1));
    }

    [Fact]
    public void ACoopGameLeavesOutWhatItHasNot()
    {
        var labels = HostFormLayout.Fields(Coop).Select(f => f.Label).ToList();
        foreach (var gone in new[] { "TEAM PLAY", "SHARE MAPS" }) Assert.DoesNotContain(gone, labels);
        foreach (var kept in new[] { "INVITE ONLY", "PASSWORD", "HIDDEN CARS", "UNLIMITED TURRETS", "CAR RESET", "CB RADIO", "CHASE CROSSHAIR", "SIM RATE", "IGNITION LOCK", "HANDICAPS" })
            Assert.Contains(kept, labels);
        // the co-op rules a labelled row each, at the box's top
        Assert.Equal(["CO-OP SCALING", "CO-OP DAMAGE", "ROLLING START"],
            HostFormLayout.Fields(Coop).Where(f => f.Where == HostFormLayout.Place.BoxRow).OrderBy(f => f.Row).Select(f => f.Label));
        Assert.Equal([0, 1, 2], HostFormLayout.Fields(Coop).Where(f => f.Where == HostFormLayout.Place.BoxRow).Select(f => f.Row).Order());
        // no Bots or Max Score: their ids are not hotspots
        foreach (int id in new[] { 16, 18, 20, 21, 26, 29, 30, 31, 34 }) Assert.DoesNotContain(id, HostFormLayout.Hot(true).Keys);
    }

    [Fact]
    public void TheHotspotsAreTheFormsOwnIds()
    {
        Assert.Equal(MeleeIds, HostFormLayout.Hot(false).Keys.Order());
        Assert.Equal(CoopIds, HostFormLayout.Hot(true).Keys.Order());
    }

    [Fact]
    public void TheHotspotsLieOnTheFormApart()
    {
        foreach (bool coop in new[] { false, true })
        {
            var hot = HostFormLayout.Hot(coop);
            foreach (var (id, (x0, x1, y0, y1)) in hot)
            {
                Assert.True(0 <= x0 && x0 < x1 && x1 <= 640 && 0 <= y0 && y0 < y1 && y1 <= 480);
                // the art's own buttons and arrows keep the original's sizes (MAKE / MODEL's arrow is 11 x 15); the port's are bigger
                bool art = HostFormLayout.Art.ContainsKey(id);
                Assert.True(x1 - x0 >= (art ? 11 : 17) && y1 - y0 >= (art ? 11 : 16), $"{id} big enough to hit");
            }
            // strict rectangles (x0 < x < x1) may share an edge, never inside room
            var all = hot.ToList();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    var (a, b) = (all[i].Value, all[j].Value);
                    bool overlap = Math.Max(a.X0, b.X0) + 1 < Math.Min(a.X1, b.X1) && Math.Max(a.Y0, b.Y0) + 1 < Math.Min(a.Y1, b.Y1);
                    Assert.False(overlap, $"{all[i].Key} {a} and {all[j].Key} {b} overlap");
                }
            // the options box's ids inside the box, the table's inside the car panel above the sketch
            var (bx0, bx1, by0, by1) = HostFormLayout.BoxInside;
            foreach (var f in HostFormLayout.Fields(coop ? Coop : Melee))
            {
                var r = hot[f.Id];
                if (f.Where == HostFormLayout.Place.Table) Assert.True(r.X0 >= 219 && r.X1 <= 456 && r.Y1 <= HostFormLayout.TableFoot);
                else Assert.True(r.X0 + 1 >= bx0 && r.X1 - 1 <= bx1 && r.Y0 + 1 >= by0 && r.Y1 - 1 <= by1, $"{f.Id} {r} outside the box");
            }
        }
    }

    [Fact]
    public void TheValuesFollowTheSettings()
    {
        (string Label, Func<HostFormLayout.State, bool, HostFormLayout.State> Set)[] checks =
        [
            ("TEAM PLAY", (s, v) => s with { TeamPlay = v }), ("INVITE ONLY", (s, v) => s with { InviteOnly = v }),
            ("SHARE MAPS", (s, v) => s with { ShareMaps = v }), ("HIDDEN CARS", (s, v) => s with { Hidden = v }),
            ("CAR RESET", (s, v) => s with { CarReset = v }), ("CB RADIO", (s, v) => s with { Cb = v }),
        ];
        foreach (var (label, set) in checks)
        {
            Assert.Equal("X", Value(set(Melee, true), label));
            Assert.Equal("", Value(set(Melee, false), label));
        }
        (string Label, Func<HostFormLayout.State, bool, HostFormLayout.State> Set)[] rules =
        [
            ("UNLIMITED TURRETS", (s, v) => s with { Turrets = v }), ("IGNITION LOCK", (s, v) => s with { IgnitionStaysOn = v }),
            ("CHASE CROSSHAIR", (s, v) => s with { Crosshair = v }), ("ROLLING START", (s, v) => s with { RollingStart = v }),
        ];
        foreach (var (label, set) in rules)
        {
            var on = set(label == "ROLLING START" ? Coop : Melee, true);
            Assert.Equal("On", Value(on, label));
            Assert.Equal("Off", Value(set(on, false), label));
        }
        Assert.Equal("60 Hz", Value(Melee with { Rate = 60 }, "SIM RATE"));
        Assert.Equal("Set", Value(Melee with { Password = true }, "PASSWORD"));
        Assert.Equal("None", Value(Melee, "PASSWORD"));
        Assert.Equal("Auto", Value(Melee with { Handicaps = "Auto" }, "HANDICAPS"));
        Assert.Equal("x2", Value(Coop with { CoopDamage = "x2" }, "CO-OP DAMAGE"));
    }

    [Fact]
    public void ThePadReachesEveryField()
    {
        foreach (var (name, coop) in new[] { ("melee", false), ("co-op", true) })
        {
            var hot = HostFormLayout.Hot(coop);
            // as the screen gives them: the area list (11) as its six rows, the rest by id
            var regions = new List<(string Name, NavRect Rect)>();
            foreach (var (id, r) in hot.OrderBy(h => h.Key))
                if (id == 11) for (int k = 0; k < 6; k++) regions.Add(($"area list row {k}", new NavRect(r.X0 + 1, 151 + 17 * k, r.X1 - r.X0 - 2, 17)));
                else regions.Add((HostFormLayout.Fields(new HostFormLayout.State { Coop = coop }).FirstOrDefault(f => f.Id == id)?.Label ?? $"id {id}", new NavRect(r.X0, r.Y0, r.X1 - r.X0, r.Y1 - r.Y0)));
            var rects = regions.Select(r => r.Rect).ToList();
            int start = PadNav.Pick(rects, -1, NavDir.Down);
            var seen = new HashSet<int> { start };
            var queue = new Queue<int>([start]);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                foreach (var d in new[] { NavDir.Up, NavDir.Down, NavDir.Left, NavDir.Right })
                    if (PadNav.Pick(rects, at, d) is >= 0 and var to && seen.Add(to)) queue.Enqueue(to);
            }
            Assert.True(seen.Count == regions.Count, $"{name}: unreachable {string.Join(", ", Enumerable.Range(0, regions.Count).Where(i => !seen.Contains(i)).Select(i => regions[i].Name))}");
            output.WriteLine($"{name}: the pad starts on {regions[start].Name}");
            var walk = new List<string>();
            for (int at = start, n = 0; at >= 0 && n < 40; at = PadNav.Pick(rects, at, NavDir.Down), n++) walk.Add(regions[at].Name);
            output.WriteLine($"  down: {string.Join(" > ", walk)}");
        }
    }
}
