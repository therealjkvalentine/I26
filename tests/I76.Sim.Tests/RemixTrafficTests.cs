using I76.Formats.Fsm;
using I76.Formats.Mission;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using I76.Sim.Session;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// Civilian traffic in generated jobs (docs/remix-missions.md "Civilian traffic"). The owner: "don't forget to place civillian cars on
/// some roads during some roguetrip missions too!" The stock trip's own (T02, T03, T06's "normal" cars): nor*.vcf cars of nobody's side
/// driving a road path and put back at its start out of the player's sight; a job draws two to four of them on some maps' paved routes.
/// </summary>
[Collection("remix")]
public class RemixTrafficTests(ITestOutputHelper output)
{
    private static readonly object Gate = new();
    private static string? _dir;

    private static string? Dir()
    {
        if (SnapshotRig.Install is null) return null;
        lock (Gate)
        {
            _dir ??= Path.Combine(Path.GetTempPath(), "i26-traffic-tests-" + Environment.ProcessId);
            GameInstall.UserMissionDir = _dir;
            return _dir;
        }
    }

    /// <summary>The paved routes are the install's: each listed route 85 % on paved road and 1200 m or longer, and no other.</summary>
    [Fact]
    public void ThePavedRoutesAreTheInstalls()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var install = new GameInstall(dir);
        foreach (var t in RemixNodes.Builtin())
        {
            var roads = RemixTraffic.Roads(new MissionFile(install.FindMission(t.Base)!));
            var want = t.Routes.Where(r => r.Points.Count >= 2 && RemixTraffic.Length(r.Points) >= 1200 && RemixTraffic.PavedShare(roads, r.Points) >= 0.85).Select(r => r.Name).Order().ToList();
            Assert.Equal(want, (RemixTraffic.Paved.GetValueOrDefault(t.Base) ?? []).Order().ToList());
        }
        // every civilian car is a base-game car the sim builds
        var vfs = new AssetVfs(install);
        foreach (var c in RemixTraffic.Cars.Concat(RemixTraffic.Slow)) Assert.NotNull(Vehicles.VehicleFactory.Create(vfs, c + ".vcf"));
    }

    /// <summary>
    /// 500 rolled jobs: some have traffic (only maps with paved routes), never more than four cars nor sixteen with the hostiles, on nobody's
    /// side, never a target; their routes' ends clear of the start and the places; the notepad says so; the civilian lines are said only
    /// then, and no line says anything untrue.
    /// </summary>
    [Fact]
    public void SomeJobsHaveTrafficAndItIsHeldToTheRules()
    {
        var tables = RemixNodes.Builtin();
        int with = 0, cars = 0, civLines = 0;
        var problems = new List<string>();
        for (int seed = 1; seed <= 500; seed++)
        {
            var spec = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Tables = tables, Challenge = RemixRun.Challenges[seed % 4] });
            var map = tables.First(t => t.Base == spec.Base);
            problems.AddRange(RemixVoiceCheck.Check(spec, map).Select(p => $"seed {seed}: {p}"));
            bool civilians = spec.Voice.Any(v => v.Line is { } id && RemixVoice.ById(id)!.Needs.Contains("civilians"));
            if (spec.Traffic is not { Count: > 0 } traffic) { Assert.False(civilians, $"seed {seed}"); continue; }
            with++;
            cars += traffic.Count;
            if (civilians) civLines++;
            Assert.True(RemixTraffic.Paved.ContainsKey(spec.Base));
            Assert.InRange(traffic.Count, 2, RemixTraffic.MaxCars);
            Assert.True(traffic.Count + RemixRoles.Hostiles(spec).Count <= RemixTraffic.MaxTotal);
            Assert.DoesNotContain(RemixRoles.Hostiles(spec), r => traffic.Any(t => t.Car == r.Car));
            Assert.Contains(spec.Text.Notes, n => n.StartsWith("Civilians on ", StringComparison.Ordinal));
            foreach (var t in traffic)
            {
                Assert.Equal(0, spec.Cars[t.Car].Team);
                Assert.Contains(spec.Cars[t.Car].Vcf, RemixTraffic.Cars.Concat(RemixTraffic.Slow));
                var pts = spec.Places[t.Route];
                foreach (var p in spec.Places.Where(p => !p.Key.StartsWith("civ_", StringComparison.Ordinal) && p.Value.Count is 1 or 2).Select(p => p.Value[0]).Append(spec.Start!.At))
                    foreach (var end in (double[][])[pts[0], pts[^1]])
                        Assert.True(System.Math.Sqrt((p[0] - end[0]) * (p[0] - end[0]) + (p[1] - end[1]) * (p[1] - end[1])) >= RemixTraffic.Clear, $"seed {seed} {t.Car}");
            }
        }
        output.WriteLine($"{with} of 500 jobs with traffic, {cars} civilians, {civLines} saying 'watch for civilians'");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
        Assert.InRange(with, 40, 200);
        Assert.True(civLines > 0);
    }

    private static (SimSession S, MissionSetup Setup, RemixSpec Spec) Built(int seed)
    {
        var spec = Enumerable.Range(seed, 200).Select(s => RemixDirector.Roll(s, new RemixDirector.JobAsk { Id = "xtraf" })).First(s => s.Traffic is { Count: >= 2 });
        RemixBuilder.Build(new GameInstall(SnapshotRig.Install!), spec).WriteTo(Dir()!);
        var l = new SnapshotRig.Loaded(SnapshotRig.Install!);
        var (s, setup) = SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, spec.Id, "vppirna1.vcf"), SimRate.R20, 1);
        return (s, setup, spec);
    }

    private static Entity? CarOf(MissionSetup setup, string name)
    {
        var vm = setup.Script!.Vm!;
        int i = Array.IndexOf(MissionScriptDecompiler.Keys(vm.Actors.Select(a => a.Label)), RemixBuilder.PathPrefix + name);
        return i >= 0 ? vm.Actors[i].Handle as Entity : null;
    }

    /// <summary>Headless: each civilian comes on after its delay, drives its route to the end and is put back at its start (a lap), with the player parked at the start.</summary>
    [Fact]
    public void CiviliansDriveTheirRoutesAndComeRound()
    {
        if (Dir() is null) return;
        var (s, setup, spec) = Built(1);
        var traffic = spec.Traffic!;
        var nearEnd = new bool[traffic.Count];
        var laps = new int[traffic.Count];
        for (int t = 0; t < 420 * 20; t++)
        {
            s.Tick([default]);
            for (int k = 0; k < traffic.Count; k++)
            {
                var v = CarOf(setup, traffic[k].Car)!.Vehicle!;
                var pts = spec.Places[traffic[k].Route];
                double end = System.Math.Sqrt((v.Pos.X - pts[^1][0]) * (v.Pos.X - pts[^1][0]) + (v.Pos.Z - pts[^1][1]) * (v.Pos.Z - pts[^1][1]));
                double start = System.Math.Sqrt((v.Pos.X - pts[0][0]) * (v.Pos.X - pts[0][0]) + (v.Pos.Z - pts[0][1]) * (v.Pos.Z - pts[0][1]));
                if (end < 60) nearEnd[k] = true;
                else if (nearEnd[k] && start < 40) { laps[k]++; nearEnd[k] = false; }
            }
        }
        output.WriteLine($"{spec.Base} {spec.Objective.Template}: laps {string.Join(" ", laps)}");
        Assert.All(laps, l => Assert.True(l >= 1));
        Assert.All(traffic, t => Assert.False(CarOf(setup, t.Car)!.Dead));
    }

    /// <summary>A civilian the player destroys: Groove remarks (S04's clip), the run counts it apart from the kills, and two move the next job a step on.</summary>
    [Fact]
    public void ACivilianKilledIsRemarkedOnAndCountsAsHeat()
    {
        if (Dir() is null) return;
        var (s, setup, spec) = Built(1);
        var heard = new List<string>();
        setup.Script!.Host.Cb.Started += l => heard.Add(Path.GetFileNameWithoutExtension(l.Name).ToLowerInvariant());
        for (int t = 0; t < 60 * 20; t++) s.Tick([default]);
        var civ = CarOf(setup, spec.Traffic![0].Car)!;
        civ.KilledByPlayer = true;
        setup.Script.Host.Destroy(civ);
        for (int t = 0; t < 20 * 20; t++) s.Tick([default]);
        Assert.Contains(RemixTraffic.KillClips[0], heard);
        var (kills, civilians) = RemixKills.Count(setup.Script.Host.Entities, setup.World.Player);
        Assert.Equal((0, 1), (kills, civilians));
        // the run's heat
        var run = RemixRun.Start(1, "vppirna1.vcf", "", "", 0);
        var offer = run.Offers(RemixNodes.Builtin())[0];
        run.Take(offer);
        run.Finish(RemixRun.Outcome.Won, 3, null, "", 2);
        run.Debriefed();
        Assert.Equal((3, 2), (run.Kills, run.Civilians));
        Assert.Equal(RemixRun.Challenges[2], run.NextChallenge);   // a job played, and two civilians: a step on
        run.Phase = "debrief";
        run.Retry();
        Assert.Equal(0, run.Civilians);
    }
}
