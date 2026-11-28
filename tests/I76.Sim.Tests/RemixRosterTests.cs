using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Trip;
using I76.Sim.Vehicles;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// A roguetrip's rosters and salvage (docs/remix-missions.md "Rosters and salvage"). The owner, after a won job in a 595ci V-10 with
/// turrets: "none of this salvage is anywhere near the level it should be for the number of cars i fought and my current gear?" The
/// director's cars ignored the player's car, and the job's wrecks went through the trip's tier clamp at the level of the map's trip
/// mission (T01: everything walked down to 261ci, Stock, 30cal), which also merged them into a handful of groups the thinning cut to
/// about twenty. Builder v7 draws the cars from a ladder of the game's own cars around the player's gear, lets a roguetrip's wrecks
/// through unclamped, and gives a won job's prize (its lead car's best part) never in the red.
/// </summary>
[Collection("remix")]
public class RemixRosterTests(ITestOutputHelper output)
{
    private static readonly object Gate = new();
    private static string? _dir;

    private static string? Dir()
    {
        if (SnapshotRig.Install is null) return null;
        lock (Gate)
        {
            _dir ??= Path.Combine(Path.GetTempPath(), "i26-roster-tests-" + Environment.ProcessId);
            GameInstall.UserMissionDir = _dir;
            return _dir;
        }
    }

    /// <summary>The owner's car when the screen was taken: 595ci V-10, EtherX Rally, Aircraft Brk, 16in Billets, DrRadar Msl at best.</summary>
    private const string OwnersGear = "13.12.10.11.10";

    /// <summary>The ladder is the install's: each base-game car's parts and strength as the table says, and the Nitro Pack's copies of them carry the same parts.</summary>
    [Fact]
    public void TheLadderIsTheInstallsCars()
    {
        foreach (var dir in new[] { SnapshotRig.Install, SnapshotRig.NitroInstall })
        {
            if (dir is null) continue;
            var vfs = new AssetVfs(new GameInstall(dir));
            var me = VehicleFactory.Create(vfs, "vppirna1.vcf");
            var (php, pdps) = RemixCheck.Stats(me);
            int n = 0;
            foreach (var c in RemixRoster.Cars)
            {
                // (the Nitro Pack has every car a job fields; the base game's vehscn is the trip's own)
                if (!c.Enemy && dir == SnapshotRig.NitroInstall && !vfs.Exists(c.Stem + ".vcf")) continue;
                Assert.True(vfs.Exists(c.Stem + ".vcf"), $"{c.Stem}.vcf is not in {dir}");
                var (keys, _) = RemixRoster.Read(vfs, c.Stem + ".vcf");
                Assert.Equal(c.Gear, RemixRoster.GearOf(keys));
                var (hp, dps) = RemixCheck.Stats(VehicleFactory.Create(vfs, c.Stem + ".vcf"));
                // (the strengths are the base game's: the Nitro Pack's guns hit a little harder, the same cars on the same ladder)
                if (dir == SnapshotRig.Install) Assert.Equal(c.Strength, System.Math.Sqrt(hp / php * (dps / pdps)), 2);
                if (c.Enemy) Assert.Null(RemixRoster.Eligible(c.Stem, keys, dps));
                n++;
            }
            output.WriteLine($"{dir}: {n} cars as the table says, {RemixRoster.Enemies.Length} a job may field");
        }
        Assert.True(RemixRoster.Enemies.Length >= 50);
        Assert.All(RemixRoster.Enemies, c => Assert.DoesNotContain(c.Stem, RemixDirector.Police.Concat(RemixDirector.Leads)));
    }

    [Fact]
    public void AGearIsReadFromTheRunsCarParts()
    {
        const string json = """{"Car":{},"Nodes":[{"Stem":"eng04","Type":2,"State":1},{"Stem":"sus04","Type":3,"State":1},{"Stem":"bra04","Type":4,"State":1},{"Stem":"wauto_1d.wdf","Type":5,"State":1},{"Stem":"gsradar.gdf","Type":7,"State":1},{"Stem":"gmheavy.gdf","Type":7,"State":1},{"Stem":"tcheavy.gdf","Type":7,"State":2},{"Stem":"eng01","Type":2,"State":4}]}""";
        Assert.Equal(OwnersGear, RemixRoster.GearOfInventory(json)!.ToString());
        Assert.Null(RemixRoster.GearOfInventory(null));
        // a car saved from the garage under a new name: its model's first variant; anything else the melee Piranha
        Assert.Equal(RemixRoster.Of("vppirna1")!.Gear, RemixRoster.GearOfCar("vppirna5.vcf"));
        Assert.Equal(RemixRoster.Of("vppirna1")!.Gear, RemixRoster.GearOfCar("zzzz.vcf"));
        var run = RemixRun.Start(1, "vppirna1.vcf", "", "", 0);
        Assert.Equal(RemixRoster.Of("vppirna1")!.Gear.ToString(), run.Gear);
        run.Inventory = json;
        Assert.Equal(OwnersGear, run.Gear);
        Assert.Equal(OwnersGear, run.Offers(RemixNodes.Builtin())[0].Gear);
        // the crew's: each slot the mean of the cars'
        Assert.Equal("5.5.2.1.5", RemixRoster.Gear.Mean([RemixRoster.Gear.Parse("1.1.1.1.1"), RemixRoster.Gear.Parse("9.8.2.1.9")])!.ToString());
    }

    private static List<RemixRoster.Car> Fielded(RemixSpec spec) =>
        [.. RemixRoles.Hostiles(spec).Select(r => r.Car).Distinct().Where(spec.Cars.ContainsKey).Select(n => RemixRoster.Of(spec.Cars[n].Vcf)).OfType<RemixRoster.Car>().Where(c => c.Enemy)];

    /// <summary>The cars follow the gear: at challenge 1.0 a melee Piranha meets cars about its own level; a starter lower ones, the V-10 the ladder's top.</summary>
    [Fact]
    public void TheRosterFollowsThePlayersGear()
    {
        double Mean(string gear, double challenge)
        {
            var l = new List<double>();
            for (int seed = 1; seed <= 150; seed++)
                l.AddRange(Fielded(RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xrost", Challenge = challenge, Job = 2, Gear = gear })).Select(c => c.Level));
            return l.Average();
        }
        var piranha = RemixRoster.Of("vppirna1")!.Gear;
        double starter = Mean("1.1.1.1.1", 1.0), mid = Mean(piranha.ToString(), 1.0), top = Mean(OwnersGear, 1.0), midHard = Mean(piranha.ToString(), 1.4);
        output.WriteLine($"mean enemy level at 1.0: starter {starter:0.00}, Piranha (level {piranha.Level:0.00}) {mid:0.00}, V-10 {top:0.00}; the Piranha at 1.4 {midHard:0.00}");
        Assert.InRange(mid, piranha.Level - 1.5, piranha.Level + 0.5);
        Assert.True(starter < 3.5, $"{starter}");
        Assert.True(top > 9, $"{top}");
        Assert.True(midHard > mid + 0.5, $"{midHard} vs {mid}");
    }

    /// <summary>Without a gear the roll is builder v6's (the stock pools, no prize); with one only the cars differ: the places, start and steps are the same roll.</summary>
    [Fact]
    public void OnlyTheCarsChangeWithAGear()
    {
        var pools = RemixDirector.Heat.SelectMany(h => h).Concat(RemixDirector.Police).Concat(RemixDirector.Leads).ToHashSet();
        for (int seed = 1; seed <= 100; seed++)
        {
            var stock = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xrost", Challenge = 1.2 });
            var geared = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xrost", Challenge = 1.2, Gear = OwnersGear, Job = 3 });
            Assert.Null(stock.Prize);
            Assert.All(stock.Cars.Values.Where(c => c.Team == 0), c => Assert.Contains(c.Vcf, pools));
            Assert.Equal(stock.Base, geared.Base);
            Assert.Equal(stock.Objective.Template, geared.Objective.Template);
            Assert.Equal(stock.Start!.At, geared.Start!.At);
            Assert.Equal(stock.Places.Keys.Order(), geared.Places.Keys.Order());
            foreach (var (k, v) in stock.Places) Assert.Equal(v.SelectMany(p => p), geared.Places[k].SelectMany(p => p));
            Assert.Equal(RemixDirector.ToYaml(geared), RemixDirector.ToYaml(RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xrost", Challenge = 1.2, Gear = OwnersGear, Job = 3 })));
        }
    }

    /// <summary>The floor: a job at challenge 1.0 or over has a prize from its lead car at or above the player's part in that slot.</summary>
    [Fact]
    public void AJobAtOneOrOverHasAPrizeAtThePlayersGear()
    {
        foreach (var gearText in new[] { "1.1.1.1.1", RemixRoster.Of("vppirna1")!.Gear.ToString(), OwnersGear })
        {
            var gear = RemixRoster.Gear.Parse(gearText)!;
            for (int seed = 1; seed <= 120; seed++)
            {
                int job = 1 + seed % 4;
                double ch = RemixRun.Challenges[job - 1];
                var spec = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xrost", Challenge = ch, Gear = gearText, Job = job });
                if (ch < 1.0) { Assert.Null(spec.Prize); continue; }
                var prize = Assert.Single(spec.Prize!);
                int slot = PartTiers.TypeOf(prize) switch { 2 => 0, 3 => 1, 4 => 2, 0 => 3, _ => 4 };
                Assert.True(PartTiers.Level(prize) >= gear.Slot(slot), $"seed {seed} gear {gear}: prize {prize} under the player's");
                // the prize is the lead car's own part
                Assert.Contains(Fielded(spec), c => c.Key(slot) == prize);
            }
        }
    }

    [Fact]
    public void ARoguetripsPoolKeepsWhatTheWreckCarried()
    {
        var stock = new SalvagePool("t01");
        var rogue = SalvagePool.Roguetrip("t01");
        foreach (var p in new[] { stock, rogue }) { p.Add("eng04", 100, false); p.Add("tmheavy.gdf", 100, false); p.Add("bra04", 50, false); }
        Assert.Equal(["eng01", "tmlight.gdf", "bra02"], stock.Items.Select(i => i.Name));
        Assert.Equal(["eng04", "tmheavy.gdf", "bra04"], rogue.Items.Select(i => i.Name));
        // T12's and T04's maps still level as T11 and T03 and still write their salvage
        Assert.NotNull(SalvagePool.Roguetrip(RemixRun.SalvageStem("t12")).Finish(_ => null, () => 1));
    }

    [Fact]
    public void APrizeIsNeverInTheRed()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var vfs = new AssetVfs(new GameInstall(dir));
        var db = PartsDatabase.Build(vfs);
        var rand = new CrtRand(7);
        var inv = new TripInventory();
        inv.AddSalvage(Enumerable.Repeat(new SalvageItem("eng04", SalvageItem.Prize, true), 400), db, rand.Next);
        Assert.All(inv.Nodes, n => Assert.True(n.Health * 2 >= n.Part.MaxHp, $"{n.Health}/{n.Part.MaxHp}"));
        Assert.Contains(inv.Nodes, n => n.Health == n.Part.MaxHp);
        // the stock roll still gives a quarter a fifth of the time
        var stock = new TripInventory();
        stock.AddSalvage(Enumerable.Repeat(new SalvageItem("eng04", -1, true), 400), db, rand.Next);
        Assert.Contains(stock.Nodes, n => n.Health * 4 == n.Part.MaxHp);
    }

    /// <summary>
    /// The owner's job (run 850764, job 3: T01's raid at challenge 1.4) won clean in the V-10: builder v6 offered about twenty parts, none
    /// above trip level 7 and none at the car's gear; builder v7 fields cars near the gear and offers forty or more, a usable part at or
    /// above the gear in every draw.
    /// </summary>
    [Fact]
    public void TheOwnersJobOffersPartsAtTheirGear()
    {
        if (Dir() is not { } dir) return;
        var install = new GameInstall(SnapshotRig.Install!);
        var gear = RemixRoster.Gear.Parse(OwnersGear)!;
        foreach (bool legacy in new[] { true, false })
        {
            var (_, offer, spec) = RemixRun.Walk(850764, "2b,0,2", gear: legacy ? "" : OwnersGear);
            Assert.Equal((298270991, "t01", "raid", 1.4), (offer.Seed, offer.Map, offer.Template, offer.Challenge));
            RemixBuilder.Build(install, spec).WriteTo(dir);
            var vfs = new AssetVfs(install);
            var db = PartsDatabase.Build(vfs);
            var win = RemixSalvage.CleanWin(vfs, new I76.Formats.Assembly.ModelAssembler(vfs), spec.Id, spec, "vppirna1.vcf", legacy);
            Assert.Equal(win.Cars.Count, win.Destroyed);
            var counts = new List<int>();
            int floor = 0, best = 0;
            for (int d = 1; d <= 40; d++)
            {
                var o = RemixSalvage.Offered(win, db, d, legacy);
                counts.Add(o.Count);
                best = System.Math.Max(best, o.Max(n => RemixSalvage.Level(n.Part)));
                if (o.Any(n => RemixSalvage.AtGear(n, gear) && RemixSalvage.Usable(n))) floor++;
            }
            output.WriteLine($"{(legacy ? "v6" : "v7")}: {win.Cars.Count} cars ({string.Join(" ", win.Cars.Select(c => c.Vcf))}), {win.Pool.Items.Count()} parts in the pool, {counts.Average():0.0} offered ({counts.Min()}-{counts.Max()}), best level {best}, a usable part at the gear in {floor} of 40");
            if (legacy)
            {
                Assert.InRange(counts.Average(), 15, 26);
                Assert.True(best <= 7);
                Assert.Equal(0, floor);
            }
            else
            {
                Assert.True(counts.Average() >= 35, $"{counts.Average()}");
                Assert.True(best >= 13);
                Assert.Equal(40, floor);
                Assert.All(Fielded(spec), c => Assert.True(c.Level >= gear.Level - 3, $"{c.Stem} {c.Level}"));
            }
        }
    }
}
