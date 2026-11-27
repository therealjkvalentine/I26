using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Session;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// Round five of the remix missions (docs/remix-missions.md): the pacing budget every rolled job keeps, the places inside structures
/// a car can be driven out of (Fort Davis's yard with its gate's doors off), a road trip's busts (the run goes on, a step harder)
/// against the player's death (the run over), RETRY JOB, the armour patched from scrap, and a job reproduced from its log line.
/// </summary>
[Collection("remix")]
public class RemixRound5Tests
{
    private static readonly object Gate = new();
    private static string? _dir;

    private static string? Dir()
    {
        if (SnapshotRig.Install is null) return null;
        lock (Gate)
        {
            _dir ??= Path.Combine(Path.GetTempPath(), "i26-remix5-tests-" + Environment.ProcessId);
            GameInstall.UserMissionDir = _dir;
            return _dir;
        }
    }

    private static (SimSession S, MissionSetup Setup) Make(string mission, uint seed = 1)
    {
        var l = new SnapshotRig.Loaded(SnapshotRig.Install!);
        return SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, mission, "vppirna1.vcf"), SimRate.R20, seed);
    }

    private static void Step(SimSession s, int ticks = 1)
    {
        for (int i = 0; i < ticks && s.Script!.Result is null; i++) { s.Tick([new CarInput()]); SnapshotRig.TakeEvents(s); }
    }

    /// <summary>
    /// The bust rule: the player's death (failure line 1, or a death line after the failures) ends the run; any other failure
    /// (leaving the map, the job's own) or walking away is a bust: the run goes on to the next choice with no salvage and the next
    /// job a step harder; four jobs played end it, busts and all. RETRY JOB undoes the last job and puts the car back.
    /// </summary>
    [Fact]
    public void ABustGoesOnAStepHarderAndADeathEndsTheRun()
    {
        // failures: 0 death, 1 left the map, 2+ the job's own; the death lines follow (5 failures here)
        Assert.Equal(RemixRun.Outcome.Won, RemixRun.Classify(true, -1, 5, false));
        Assert.Equal(RemixRun.Outcome.Dead, RemixRun.Classify(false, 0, 5, false));
        Assert.Equal(RemixRun.Outcome.Dead, RemixRun.Classify(false, 6, 5, false));
        Assert.Equal(RemixRun.Outcome.Dead, RemixRun.Classify(false, 3, 5, true));
        Assert.Equal(RemixRun.Outcome.Bust, RemixRun.Classify(false, 1, 5, false));
        Assert.Equal(RemixRun.Outcome.Bust, RemixRun.Classify(false, 2, 5, false));
        Assert.Equal(RemixRun.Outcome.Bust, RemixRun.Classify(false, -1, 5, false));   // walked away (Abort)

        var tables = RemixNodes.Builtin();
        var run = RemixRun.Start(4242, "vppirna1.vcf", "Groove", "Piranha", 0);
        Assert.Equal(RemixBuilder.Version, run.Builder);
        var first = run.Offers(tables);
        run.Take(first[1]);
        var hurt = new RunCar { Armour = [10, 20, 30, 40], ArmourMax = [100, 100, 100, 100], ChassisMax = [50, 50, 50, 50], ArmourLeft = 25 };
        run.Finish(RemixRun.Outcome.Bust, 2, hurt, "Too slow. The deal's off.");
        Assert.Equal("debrief", run.Phase);
        Assert.Equal(RemixRun.Outcome.Bust, run.Last);
        Assert.Equal(1, run.Busts);
        Assert.Equal(0, run.Won);
        Assert.Same(hurt, run.State);           // the damage carries, busted or not
        Assert.Equal(1.2, run.NextChallenge);   // job 2 at job 3's challenge: a step harder
        // RETRY JOB (the developer's): the job undone, the car as it began, the same offer to play again
        var again = run.Retry();
        Assert.Equal(first[1].Seed, again!.Seed);
        Assert.Equal("playing", run.Phase);
        Assert.Empty(run.Legs);
        Assert.Equal(0, run.Busts);
        Assert.Null(run.State);
        Assert.Equal(0, run.Kills);
        run.Finish(RemixRun.Outcome.Bust, 2, hurt, "Too slow. The deal's off.");
        run.Debriefed();
        Assert.Equal("choose", run.Phase);       // a bust: no salvage, the next choice
        var second = run.Offers(tables);
        Assert.All(second, o => Assert.Equal(1.2, o.Challenge));
        run.Take(second[0]);
        run.Finish(RemixRun.Outcome.Won, 3, hurt);
        run.Debriefed();
        Assert.Equal("salvage", run.Phase);      // a win: the field salvage and the garage
        run.Salvaged("{}", RemixRun.Patch(hurt, hurt.ArmourMax, hurt.ChassisMax, 2));
        Assert.Equal("choose", run.Phase);
        Assert.Equal(1.4, run.NextChallenge);
        // the run file keeps it all
        run = RemixRun.Parse(run.ToYaml());
        Assert.Equal(1, run.Busts);
        Assert.Equal("bust", run.Legs[0].Outcome);
        Assert.Equal("Too slow. The deal's off.", run.Legs[0].Why);
        for (int k = 2; k < RemixRun.Jobs; k++)
        {
            run.Take(run.Offers(tables)[0]);
            run.Finish(k == 3 ? RemixRun.Outcome.Bust : RemixRun.Outcome.Won, 1, null);
            run.Debriefed();
            if (run.Phase == "salvage") run.Salvaged(null, null);
        }
        Assert.Equal("done", run.Phase);         // four jobs played: done, busts and all
        Assert.Equal(2, run.Won);
        Assert.Equal(2, run.Busts);
        // a death: the debrief, then the run over
        var d = RemixRun.Start(77, "vppirna1.vcf", "Groove", "Piranha", 0);
        d.Take(d.Offers(tables)[0]);
        d.Finish(RemixRun.Outcome.Dead, 0, hurt, "You were killed.");
        Assert.Equal("debrief", d.Phase);
        d.Debriefed();
        Assert.Equal("over", d.Phase);
        Assert.True(d.Over);
    }

    /// <summary>The garage patches armour from scrap: a tenth of the full armour a part left in the field, weakest side first, then the chassis.</summary>
    [Fact]
    public void ScrapPatchesTheArmourWeakestSideFirstThenTheChassis()
    {
        var car = new RunCar { Armour = [100, 20, 100, 60], Chassis = [50, 50, 10, 50], ArmourMax = [100, 100, 100, 100], ChassisMax = [50, 50, 50, 50] };
        var one = RemixRun.Patch(car, car.ArmourMax, car.ChassisMax, 1);   // 40 points of plate
        Assert.Equal(40, one.Armour.Sum() - car.Armour.Sum());
        Assert.True(one.Armour[1] > car.Armour[1]);
        Assert.Equal(car.Chassis, one.Chassis);
        var lots = RemixRun.Patch(car, car.ArmourMax, car.ChassisMax, 5);   // 200: armour full (120 short), then 40 of the chassis' 40
        Assert.Equal([100, 100, 100, 100], lots.Armour);
        Assert.Equal([50, 50, 50, 50], lots.Chassis);
        Assert.Equal(100, lots.ArmourLeft);
        Assert.Equal(car.Armour, RemixRun.Patch(car, car.ArmourMax, car.ChassisMax, 0).Armour);
    }

    /// <summary>A road trip job's reproduction line rolls that exact job again headless (i76 remix roll --run N --job K --picks ...).</summary>
    [Fact]
    public void ALaunchLineReproducesItsJob()
    {
        var tables = RemixNodes.Builtin();
        var run = RemixRun.Start(31337, "vppirna1.vcf", "Groove", "Piranha", 0);
        var o1 = run.Offers(tables);
        run.Take(o1[1]);
        run.Finish(RemixRun.Outcome.Bust, 0, null);
        run.Debriefed();
        var o2 = run.Offers(tables);
        var offer = o2[^1];
        var spec = run.Spec(offer, tables);
        string line = RemixRun.LaunchLine(run, offer, spec, "vppirna1.vcf", "abc1234");
        Assert.Contains("RUN 031337 JOB 2/4", line);
        Assert.Contains($"#{offer.Seed}", line);
        Assert.Contains($"builder v{RemixBuilder.Version}", line);
        Assert.Contains("commit abc1234", line);
        string picks = line[(line.IndexOf("--picks ", StringComparison.Ordinal) + 8)..].TrimEnd(')');
        Assert.Equal($"1b,{o2.Count - 1}", picks);
        var (_, again, aspec) = RemixRun.Walk(31337, picks, tables);
        Assert.Equal(offer.Seed, again.Seed);
        Assert.Equal(RemixDirector.ToYaml(spec), RemixDirector.ToYaml(aspec));
        var (top, bottom) = RemixRun.Label(run, offer, spec);
        Assert.Equal($"RUN 031337 JOB 2/4 #{offer.Seed}", top);
        Assert.Contains($"v{RemixBuilder.Version}", bottom);
    }

    /// <summary>
    /// Pacing (the owner: one to two minutes of driving between events on average): 300 rolled jobs and the jobs of 20 road trips
    /// all keep their budget on their maps' roads, and the places of note (inside structures, overlooks, spurs) come up often.
    /// </summary>
    [Fact]
    public void RolledJobsKeepTheirPacingAndUseThePlacesOfNote()
    {
        if (SnapshotRig.Install is not { } inst) return;
        var install = new GameInstall(inst);
        var tables = RemixNodes.Builtin();
        var jobs = Enumerable.Range(1, 300).Select(seed => RemixDirector.Roll(seed, id: "xpace")).ToList();
        for (int n = 1; n <= 20; n++)
        {
            var run = RemixRun.Start(n, "vppirna1.vcf", "", "", 0);
            for (int k = 0; k < RemixRun.Jobs; k++)
            {
                var offers = run.Offers(tables);
                jobs.AddRange(offers.Select(o => run.Spec(o, tables)));
                run.Take(offers[0]);
                run.Finish(RemixRun.Outcome.Won, 0, null);
                run.Debriefed();
                run.Salvaged(null, null);
            }
        }
        var over = new List<string>();
        var means = new List<double>();
        foreach (var spec in jobs)
        {
            var rep = RemixPacing.Measure(install, spec);
            Assert.NotEmpty(rep.Legs);
            means.Add(rep.Mean);
            over.AddRange(rep.Problems.Select(p => $"{spec.Title} {spec.Base} {spec.Objective.Template} seed {spec.Seed}: {p}"));
        }
        // a road trip's offers may draw a rare job a little over (none of the 300 rolls is): at most one in a hundred
        Assert.True(over.Count * 100 <= jobs.Count, string.Join("\n", over));
        Assert.InRange(means.Order().ElementAt(means.Count / 2), 30, 90);
        var noted = jobs.Take(300).Count(s => s.Inside.Count > 0 || tables.First(t => t.Base == s.Base).Nodes.Any(n => n.IsSpecial
            && s.Places.Values.Where(p => p.Count == 1).Append(new List<double[]> { s.Start!.At }).Any(p => System.Math.Abs(p[0][0] - n.At[0]) < 1 && System.Math.Abs(p[0][1] - n.At[1]) < 1)));
        Assert.True(noted >= 60, $"{noted} of 300 jobs use a place of note");
        Assert.True(tables.Sum(t => t.Nodes.Count(n => n.IsInside)) >= 10);
        Assert.True(tables.Sum(t => t.Nodes.Count(n => n.IsSpecial)) >= 12);
    }

    /// <summary>
    /// Every place inside a structure is driven out of (its doors off, its wall section out, its friendly car parked); Fort Davis's
    /// yard with the doors left on is not: its gate holds the car in.
    /// </summary>
    [Fact]
    public void EveryPlaceInsideAStructureCanBeDrivenOutOf()
    {
        if (Dir() is null) return;
        var install = new GameInstall(SnapshotRig.Install!);
        var bad = new List<string>();
        foreach (var t in RemixNodes.Builtin())
            foreach (var n in t.Nodes.Where(n => n.IsInside))
                if (RemixDriveOut.Node(install, t, n) is { Passed: false } r) bad.Add($"{t.Base} {n.Name}: {r}");
        Assert.Empty(bad);
        var fort = RemixNodes.Builtin().First(t => t.Base == "t16");
        var yard = fort.Nodes.First(n => n.Name == "fort_yard");
        Assert.Equal(["HF1_DRL1", "HF1_DRR1", "HF1_GAT1"], yard.Open[0].Nodes);
        var shut = new PointNode { Name = "shut", Kinds = yard.Kinds, At = yard.At, Heading = yard.Heading, Inside = yard.Inside, Out = yard.Out, Called = yard.Called };
        Assert.False(RemixDriveOut.Node(install, fort, shut).Passed);
    }

    /// <summary>
    /// The doors taken off before the start: the fort's door nodes are marked removed and hold no collider, the rest of the fort
    /// stands, and a snapshot of the job restores into a rebuild of it.
    /// </summary>
    [Fact]
    public void FortDavisLosesItsGateDoorsBeforeTheStart()
    {
        if (Dir() is not { } dir) return;
        var fort = RemixNodes.Builtin().First(t => t.Base == "t16");
        var yard = fort.Nodes.First(n => n.Name == "fort_yard");
        var spec = new RemixSpec { Id = "xfort", Title = "FORT", Base = "t16", Start = new PoseSpec { At = yard.At, Heading = yard.Heading!.Value, Out = yard.Out } };
        spec.Places["home"] = [yard.Out!];
        spec.Objective.Template = "deliver";
        spec.Objective.Home = new ZoneSpec { Place = "home", Radius = 80 };
        spec.Open.AddRange(yard.Open);
        spec.Text.Objectives.Add("Out of the fort.");
        spec.Text.Failures.AddRange(["dead", "left"]);
        RemixBuilder.Build(new GameInstall(SnapshotRig.Install!), spec).WriteTo(dir);
        var (s, setup) = Make("xfort");
        var o = setup.Statics.Objects.First(x => x.Name.Equals("bhftdav1", StringComparison.OrdinalIgnoreCase));
        foreach (var name in yard.Open[0].Nodes)
        {
            int i = o.Nodes.FindIndex(n => n.Name == name);
            Assert.True(o.Nodes[i].Removed);
            Assert.DoesNotContain(o.Boxes, b => b.Node == i);
        }
        Assert.Contains(o.Boxes, b => o.Nodes[b.Node].Name == "HF1_WAL1");
        Step(s, 20 * 3);
        var snap = s.Snapshot();
        var (r, _) = Make("xfort");
        r.Restore(snap);
        Assert.Equal(s.StateHash(), r.StateHash());
    }

    /// <summary>A rolled raid's failures classified as the run takes them: too slow is a bust, killed in the middle of it is the run's end.</summary>
    [Fact]
    public void ARaidsFailuresAreABustOrTheRunsEnd()
    {
        if (Dir() is not { } dir) return;
        RemixSpec? spec = null;
        for (int seed = 1; seed < 300 && spec is null; seed++)
        {
            var s0 = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xbust", Template = "raid", Map = "t09" });
            if (s0.Bind.Count > 0) spec = s0;
        }
        RemixBuilder.Build(new GameInstall(SnapshotRig.Install!), spec!).WriteTo(dir);
        var o = spec!.Objective;
        var target = spec.Places[o.Arrive!.Place][0];
        // too slow: the roads closed (a bust)
        var (w, ws) = Make("xbust");
        Step(w, 20 * 2);
        ws.World.Player!.Pos = new Double3(target[0], ws.Ground.BareHeight((float)target[0], (float)target[1]) + 0.5, target[1]);
        Step(w, 20 * 3);
        foreach (var t in o.Targets)
        {
            var vm = w.Script!.Vm!;
            var keys = I76.Formats.Fsm.MissionScriptDecompiler.Keys(vm.Actors.Select(a => a.Label));
            var e = (Entity)vm.Actors[Array.IndexOf(keys, RemixBuilder.PathPrefix + t)].Handle!;
            if (e.Static is { } st) RemixCheck.Wreck(ws, st);
        }
        for (int i = 0; i < 20 * (o.Escape!.Seconds + 20) && w.Script!.Result is null; i++)
        {
            w.Tick([new CarInput()]);
            SnapshotRig.TakeEvents(w);
            // the alarm's crew is kept off him (killed as it comes out): only the clock may end it
            if (i % 20 == 0)
                foreach (var e in w.Script.Host.Entities.Where(e => e.Vehicle is { } v && v != ws.World.Player && !e.Dead && Weapons.Combat.IsLive(v)))
                    if (System.Math.Abs(e.Vehicle!.Pos.X - ws.World.Player!.Pos.X) < 400 && System.Math.Abs(e.Vehicle.Pos.Z - ws.World.Player.Pos.Z) < 400) { e.KilledByPlayer = true; w.Script.Host.Destroy(e); }
        }
        Assert.False(w.Script!.Result);
        int fm = w.Script.Host.End!.Value.FailMessage;
        Assert.Equal(RemixRun.Outcome.Bust, RemixRun.Classify(false, fm, spec.Text.Failures.Count, false));
        // killed: the run's end
        var (d, dsetup) = Make("xbust");
        Step(d, 20 * 2);
        d.Script!.Host.Destroy(d.Script.Host.Of(dsetup.World.Player!)!);
        Step(d, 20 * 10);
        Assert.False(d.Script.Result);
        Assert.Equal(RemixRun.Outcome.Dead, RemixRun.Classify(false, d.Script.Host.End!.Value.FailMessage, spec.Text.Failures.Count, true));
    }

    /// <summary>The radio's cut: Groove's "Dig incoming bandits" cut to its last two words, faded at both ends; the jobs use it.</summary>
    [Fact]
    public void TheIncomingCueIsACutOfItsClip()
    {
        Assert.Equal(("06sgc15", 1625, 2895), I76.Formats.Audio.SoundFiles.Cut("06sgc15@1625-2895.wav"));
        Assert.Null(I76.Formats.Audio.SoundFiles.Cut("06sgc15.wav"));
        Assert.Null(I76.Formats.Audio.SoundFiles.Cut("06sgc15@2895-1625"));
        Assert.Equal(RemixDirector.Bandits, "06sgc15@1625-2895");
        var jobs = Enumerable.Range(1, 60).Select(seed => RemixDirector.Roll(seed)).ToList();
        Assert.Contains(jobs, j => j.Radio.All.Contains(RemixDirector.Bandits));
        Assert.DoesNotContain(jobs, j => j.Radio.All.Contains("06sgc15"));
        Assert.All(jobs, j => Assert.All(j.Radio.All, c => Assert.True(j.Radio.Words.ContainsKey(c), $"no words for {c}")));
        if (SnapshotRig.Install is not { } inst) return;
        var vfs = new AssetVfs(new GameInstall(inst));
        var whole = I76.Formats.Audio.SoundFiles.Read(vfs, "06sgc15.wav")!;
        var cut = I76.Formats.Audio.SoundFiles.Read(vfs, "06sgc15@1625-2895.wav")!;
        Assert.InRange(whole.Seconds, 2.9, 3.05);
        Assert.InRange(cut.Seconds, 1.26, 1.28);
        Assert.Equal(whole.Rate, cut.Rate);
        // faded: the cut's first sample is silent, a sample in its middle is the clip's own
        int frame = cut.Channels * cut.Bits / 8;
        Assert.Equal(cut.Bits == 16 ? 0 : 128, cut.Bits == 16 ? (short)(cut.Data[0] | cut.Data[1] << 8) : cut.Data[0]);
        int mid = cut.Data.Length / frame / 2 * frame, from = (int)(1625L * whole.Rate / 1000) * frame;
        Assert.Equal(whole.Data[from + mid], cut.Data[mid]);
    }

    /// <summary>
    /// The job's map: the whole map (every road inside the sketch's box), one or two landmarks, each objective's place for the
    /// line that letters it (distance and compass), the radio's words; the region's map holds every trip map.
    /// </summary>
    [Fact]
    public void AJobsMapShowsTheWholeMapItsLandmarksAndItsSteps()
    {
        var specs = Enumerable.Range(1, 40).Select(seed => RemixDirector.Roll(seed)).ToList();
        Assert.All(specs, s => Assert.InRange(s.Landmarks.Count, 1, 2));
        var region = RemixRegion.Builtin();
        foreach (var t in RemixNodes.Builtin())
        {
            Assert.True(region.Maps.ContainsKey(t.Base), $"{t.Base} not on the region's map");
            var (x, y) = region.Project(region.Maps[t.Base], 304, 334);
            Assert.InRange(x, 0, 304);
            Assert.InRange(y, 0, 334);
        }
        Assert.All(region.Highways, h => Assert.True(region.Route(h).Count() == h.Towns.Count, $"highway {h.Name}: a town not in the list"));
        if (Dir() is not { } dir) return;
        var install = new GameInstall(SnapshotRig.Install!);
        var spec = RemixDirector.Roll(8, id: "xmapw");
        var built = RemixBuilder.Build(install, spec);
        built.WriteTo(dir);
        var marks = RemixMarks.Parse(built.Marks!);
        Assert.Equal(spec.Landmarks.Count, marks.Landmarks.Count);
        Assert.NotEmpty(marks.Steps);
        Assert.Equal(spec.Radio.Words.Count, marks.Words.Count);
        Assert.Equal(built.Marks, RemixMarks.Parse(built.Marks!).ToText());
        var msn = new I76.Formats.Mission.MissionFile(install.FindMission("xmapw")!);
        var ter = I76.Formats.Mission.TerrainTiles.Load(msn, I76.Formats.Assembly.MissionWorld.FindTerrain(install, msn)!);
        var sk = I76.Formats.Mission.ArenaSketch.Whole(msn, ter, [.. marks.Points.Select(p => p.At)], [.. marks.Lines.Select(l => l.Points)], [], 560, 392)!;
        Assert.True(sk.Styled);
        foreach (var r in msn.RoadSegments())
            foreach (var q in r.Points)
            {
                var u = sk.InBox(new System.Numerics.Vector2(q.X, q.Z));
                Assert.InRange(u.X, -0.01f, 1.01f);
                Assert.InRange(u.Y, -0.01f, 1.01f);
            }
    }

    /// <summary>
    /// The ambush (the owner: "opportunities for us to be the ones ambushing"): the player waits at a place by a route; the convoy
    /// comes along it unaware (no alarm, its own pace) until he is close or fires, then runs at its alert speed; it passes him in
    /// one to two minutes; killing it and getting away wins; letting it through is failure line 3, a bust; his death ends the run.
    /// </summary>
    [Fact]
    public void AnAmbushWaitsForTheConvoyAndItsEndsAreClassified()
    {
        var specs = Enumerable.Range(1, 300).Select(seed => RemixDirector.Roll(seed)).ToList();
        Assert.Contains(specs, x => x.Objective.Template == "ambush");
        Assert.Equal("AMBUSH", RemixRun.JobWord("ambush"));
        if (Dir() is not { } dir) return;
        var install = new GameInstall(SnapshotRig.Install!);
        RemixSpec? spec = null;
        for (int seed = 1; seed < 200 && spec is null; seed++)
        {
            var c = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xamb", Template = "ambush", Map = "t16" });
            if (c.Objective.Convoy!.Escorts.Count >= 1) spec = c;
        }
        var rep = RemixPacing.Measure(install, spec!);
        Assert.Empty(rep.Problems);
        Assert.InRange(rep.FirstContact, 20, 120);
        RemixBuilder.Build(install, spec!).WriteTo(dir);
        var cv = spec!.Objective.Convoy!;
        Entity Of(SimSession s, string name)
        {
            var vm = s.Script!.Vm!;
            var keys = I76.Formats.Fsm.MissionScriptDecompiler.Keys(vm.Actors.Select(a => a.Label));
            return (Entity)vm.Actors[Array.IndexOf(keys, RemixBuilder.PathPrefix + name)].Handle!;
        }
        // unaware: rolling on the route at its pace, the player untouched, while he waits where he is
        var (s1, set1) = Make("xamb");
        var lead = Of(s1, cv.Lead).Vehicle!;
        var me = set1.World.Player!;
        double hp0 = me.Health!.Armour.Sum() + me.Health.Chassis.Sum(), top = 0;
        double x0 = lead.Pos.X, z0 = lead.Pos.Z;
        for (int t = 0; t < 20 * 25; t++) { s1.Tick([new CarInput()]); SnapshotRig.TakeEvents(s1); if (t > 20 * 12) top = System.Math.Max(top, lead.Speed); }
        Assert.True(System.Math.Sqrt((lead.Pos.X - x0) * (lead.Pos.X - x0) + (lead.Pos.Z - z0) * (lead.Pos.Z - z0)) > 100, "the convoy rolled");
        Assert.True(top < cv.AlertSpeed - 3, $"unaware: {top:0.0} m/s, its pace {cv.Speed}, alert {cv.AlertSpeed}");
        Assert.Equal(hp0, me.Health.Armour.Sum() + me.Health.Chassis.Sum());
        // seen close: the player put beside the lead, it runs
        me.Pos = new Double3(lead.Pos.X + 40, lead.Pos.Y, lead.Pos.Z);
        me.Vel = default;
        double fast = 0;
        for (int t = 0; t < 20 * 12; t++) { s1.Tick([new CarInput()]); SnapshotRig.TakeEvents(s1); fast = System.Math.Max(fast, lead.Speed); }
        Assert.True(fast > top + 2, $"alerted: {fast:0.0} m/s against {top:0.0}");
        // let through: line 3, a bust
        var (s2, _) = Make("xamb");
        Step(s2, 20 * 600);
        Assert.False(s2.Script!.Result);
        Assert.Equal(RemixRun.Outcome.Bust, RemixRun.Classify(false, s2.Script.Host.End!.Value.FailMessage, spec.Text.Failures.Count, false));
        Assert.Contains("got through", spec.Text.Failures[s2.Script.Host.End!.Value.FailMessage]);
        // killed: the run over
        var (s3, set3) = Make("xamb");
        Step(s3, 20 * 3);
        s3.Script!.Host.Destroy(s3.Script.Host.Of(set3.World.Player!)!);
        Step(s3, 20 * 10);
        Assert.Equal(RemixRun.Outcome.Dead, RemixRun.Classify(false, s3.Script.Host.End!.Value.FailMessage, spec.Text.Failures.Count, true));
        // won: the convoy and the backup killed as they come, then away to the finish with nobody near
        var (s4, set4) = Make("xamb");
        var fin = spec.Places[spec.Objective.Finish!.Place][0];
        for (int t = 0; t < 20 * 300 && s4.Script!.Result is null; t += 20)
        {
            Step(s4, 20);
            foreach (var r in RemixRoles.Hostiles(spec))
            {
                var e = Of(s4, r.Car);
                if (!e.Dead && e.Vehicle is { } v && Weapons.Combat.IsLive(v) && e.Order is { } ord && ord != Fsm.AiOrder.Hide) { e.KilledByPlayer = true; s4.Script.Host.Destroy(e); }
            }
            if ((s4.Script.Host.Objectives.Flags[0] & Objectives.Succeeded) != 0 && (s4.Script.Host.Objectives.Flags[1] & Objectives.Succeeded) != 0)
            {
                var p = set4.World.Player!;
                p.Pos = new Double3(fin[0], set4.Ground.BareHeight((float)fin[0], (float)fin[1]) + 0.5, fin[1]);
                p.Vel = default;
            }
        }
        Assert.True(s4.Script!.Result, string.Join(", ", s4.Script.Host.Objectives.Flags.Select(Objectives.Describe)));
    }

    /// <summary>The run file keeps everything a job's launch and end add (the reproduction lines, the picks, the inventory, a job's start).</summary>
    [Fact]
    public void TheRunFileReadsBackWhatALaunchWrote()
    {
        var tables = RemixNodes.Builtin();
        var run = RemixRun.Start(4242, "valepre1.vcf", "Groove", "Piranha", 0);
        var offer = run.Offers(tables)[0];
        var spec = run.Spec(offer, tables);
        run.Take(offer);
        run.Log.Add(RemixRun.LaunchLine(run, offer, spec, "valepre1.vcf", "eba6a649"));
        run.Inventory = "{\"Car\":{\"Variant\":\"x: y # z\"}}";
        run.JobStart = new RunCar { Armour = [1, 2, 3, 4], ArmourMax = [9, 9, 9, 9] };
        var back = RemixRun.Parse(run.ToYaml());
        Assert.Equal(run.Log, back.Log);
        Assert.Equal(run.Picks, back.Picks);
        Assert.Equal(run.Inventory, back.Inventory);
        Assert.Equal("playing", back.Phase);
        Assert.Equal(offer.Seed, back.Current!.Seed);
    }
}
