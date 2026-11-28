using System.Numerics;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// The boundaries of a generated job (docs/remix-missions.md "Boundaries"): no start, co-op seat, early place or first drive where the
/// stock leave-map routine warns, nor at the map's edge; being near an exit does nothing, crossing into its gate is a bust with its line;
/// a job's marks are spaced (the owner's run 850764, job 2: the start on the convoy's own road, between two exits).
/// </summary>
[Collection("remix")]   // GameInstall.UserMissionDir is one for the process
public class RemixBoundsTests(ITestOutputHelper output)
{
    private static readonly object Gate = new();
    private static string? _dir;

    private static string? Dir()
    {
        if (SnapshotRig.Install is null) return null;
        lock (Gate)
        {
            _dir ??= Path.Combine(Path.GetTempPath(), "i26-remix-bounds-tests-" + Environment.ProcessId);
            GameInstall.UserMissionDir = _dir;
            return _dir;
        }
    }

    private static (AssetVfs Vfs, I76.Formats.Assembly.ModelAssembler Asm) Build(RemixSpec spec)
    {
        var install = new GameInstall(SnapshotRig.Install!);
        RemixBuilder.Build(install, spec).WriteTo(Dir()!);
        var vfs = new AssetVfs(install);
        return (vfs, new I76.Formats.Assembly.ModelAssembler(vfs));
    }

    /// <summary>Every shipped node table keeps its starts, places and convoy roads clear of the exits and its places off the map's edge.</summary>
    [Fact]
    public void EveryTableKeepsItsMargins()
    {
        if (SnapshotRig.Install is not { } inst) return;
        var install = new GameInstall(inst);
        var bad = RemixNodes.Builtin().Where(t => !t.Nitro && install.FindMission(t.Base) is not null).SelectMany(t => RemixBoundsTable.Check(install, t).Select(p => $"{t.Base}: {p}")).ToList();
        foreach (var b in bad) output.WriteLine(b);
        Assert.Empty(bad);
    }

    /// <summary>
    /// Rolled jobs (150 seeds and every map's every template once), each measured with a co-op crew of eight: no start, seat or place
    /// inside the margins, no first drive through a warning, no wander into a fail, marks spaced.
    /// </summary>
    [Fact]
    public void RolledJobsKeepClearOfTheBoundariesWithEightSeats()
    {
        if (Dir() is null) return;
        lock (Gate)
        {
            var jobs = new List<(string Name, RemixSpec Spec)>();
            for (int seed = 1; seed <= 150; seed++) jobs.Add(($"seed {seed}", RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xbt", Challenge = 1.0 })));
            foreach (var t in RemixNodes.Builtin().Where(t => !t.Nitro))
                foreach (var k in RemixDirector.Templates.Where(k => RemixDirector.Allows(t, k)))
                    jobs.Add(($"{t.Base} {k}", RemixDirector.Roll(7, new RemixDirector.JobAsk { Id = "xbt", Challenge = 1.0, Map = t.Base, Template = k })));
            var problems = new List<string>();
            foreach (var (name, spec) in jobs)
            {
                var (vfs, asm) = Build(spec);
                var m = RemixBounds.Of(vfs, asm, spec.Id, spec, seats: 8);
                foreach (var p in RemixBounds.Problems(m).Concat(RemixSpacing.Of(spec).Problems)) problems.Add($"{name} ({spec.Base} {spec.Objective.Template}): {p}");
            }
            foreach (var p in problems) output.WriteLine(p);
            output.WriteLine($"{jobs.Count} jobs, {problems.Count} problems");
            Assert.Empty(problems);
        }
    }

    /// <summary>Thirty rolled jobs with a crew of four idle for a minute: nobody comes inside a leave-map warning, nothing ends.</summary>
    [Fact]
    public void AnIdleCrewOfFourIsNeverWarnedOrEnded()
    {
        if (Dir() is null) return;
        lock (Gate)
        {
            for (int seed = 201; seed <= 230; seed++)
            {
                var spec = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xbt", Challenge = 1.0 });
                var (vfs, asm) = Build(spec);
                var r = RemixCheck.Walk(vfs, asm, spec.Id, spec, seats: 4, horizon: 60, walk: false);
                Assert.True(r.NearestExit >= RemixBounds.Warn, $"seed {seed} ({spec.Base} {spec.Objective.Template}): a seat came {r.NearestExit:0} m from an exit");
                Assert.False(r.Won == false && r.Fail == 1, $"seed {seed}: failed for leaving the map ({r.Trace})");
            }
        }
    }

    private static Vector2 Start(RemixSpec spec) => new((float)spec.Start!.At[0], (float)spec.Start.At[1]);

    /// <summary>A point on the routine's square <paramref name="d"/> from an exit's trigger, on the start's side of it.</summary>
    private static Vector2 Toward(RemixBounds.Exit e, Vector2 from, float d) =>
        MathF.Abs(from.X - e.At.X) >= MathF.Abs(from.Y - e.At.Y) ? new(e.At.X + MathF.Sign(from.X - e.At.X) * d, e.At.Y) : new(e.At.X, e.At.Y + MathF.Sign(from.Y - e.At.Y) * d);

    /// <summary>
    /// Each template: a player who wanders to 120 m of an exit (inside its warnings, outside its gate) and stays is warned and nothing more;
    /// one who drives into the gate (20 m) has left the map: the job fails on its second line, "left the map", which a roguetrip counts a
    /// bust (not a death). In co-op one player in the gate is put back beside the other; both gone is the same bust.
    /// </summary>
    [Theory]
    [InlineData("deliver")]
    [InlineData("tail")]
    [InlineData("intercept")]
    [InlineData("ambush")]
    [InlineData("defend")]
    [InlineData("escort")]
    [InlineData("raid")]
    public void NearAnExitIsAWarningIntoItsGateIsABust(string template)
    {
        if (Dir() is null) return;
        lock (Gate)
        {
            var spec = RemixDirector.Roll(11, new RemixDirector.JobAsk { Id = "xbt", Challenge = 1.0, Template = template });
            var (vfs, asm) = Build(spec);
            var exits = RemixBounds.Exits(vfs.Install.FindMission(spec.Id)!, spec);
            Assert.NotEmpty(exits);
            var start = Start(spec);
            var e = RemixBounds.NearestExit(exits, start)!;
            var near = Toward(e, start, 120);
            // (put down again every two seconds: a car left on an exit road's slope rolls down it)
            var r1 = RemixCheck.Walk(vfs, asm, spec.Id, spec, horizon: 30, walk: false, put: [.. Enumerable.Range(0, 15).Select(i => (0, 1f + 2 * i, near.X, near.Y))]);
            output.WriteLine($"{template} ({spec.Base}): 120 m from {e.Name} at ({near.X:0}, {near.Y:0}), nearest {r1.NearestExit:0}: {r1.Trace}");
            Assert.True(r1.Won is null || r1.Fail != 1, $"near is not out: {r1.Trace}");
            var gate = Toward(e, start, 20);
            var r2 = RemixCheck.Walk(vfs, asm, spec.Id, spec, horizon: 30, walk: false, put: [(0, 1f, gate.X, gate.Y)]);
            output.WriteLine($"{template}: into {e.Name}'s gate: {r2.Trace}");
            Assert.True(r2.Won == false && r2.Fail == 1, r2.Trace);
            Assert.Equal(RemixRun.Outcome.Bust, RemixRun.Classify(false, r2.Fail, spec.Text.Failures.Count, false));
            Assert.False(string.IsNullOrWhiteSpace(spec.Text.Failures[1]));   // the line the debrief shows ("why")
            // co-op: one in the gate is put back beside the other; both in it is the bust
            var c1 = RemixCheck.Walk(vfs, asm, spec.Id, spec, seats: 2, horizon: 30, walk: false, put: [(1, 1f, gate.X, gate.Y)]);
            output.WriteLine($"{template} x2, seat 1 into the gate: {c1.Trace}");
            Assert.True(c1.Won is null, c1.Trace);
            var c2 = RemixCheck.Walk(vfs, asm, spec.Id, spec, seats: 2, horizon: 40, walk: false, put: [(0, 1f, gate.X, gate.Y), (1, 1f, gate.X, gate.Y + 5)]);
            output.WriteLine($"{template} x2, both into the gate: {c2.Trace}");
            Assert.True(c2.Won is null || c2.Fail == 1, c2.Trace);
        }
    }

    /// <summary>
    /// Each template: standing at any of its places at the start (an escape, a way out, a home, a finish) ends nothing: a place counts only
    /// once its step is open.
    /// </summary>
    [Theory]
    [InlineData("deliver")]
    [InlineData("tail")]
    [InlineData("intercept")]
    [InlineData("ambush")]
    [InlineData("defend")]
    [InlineData("escort")]
    [InlineData("raid")]
    public void APlaceAtTheStartEndsNothing(string template)
    {
        if (Dir() is null) return;
        lock (Gate)
        {
            var spec = RemixDirector.Roll(11, new RemixDirector.JobAsk { Id = "xbt", Challenge = 1.0, Template = template });
            var (vfs, asm) = Build(spec);
            foreach (var (label, at) in RemixCheck.PlayerPlaces(spec).Where(p => p.Label != "START"))
            {
                var r = RemixCheck.Walk(vfs, asm, spec.Id, spec, horizon: 20, walk: false, put: [(0, 1f, (float)at[0], (float)at[1])]);
                output.WriteLine($"{template} at {label}: {r.Trace}");
                Assert.True(r.Won is null, $"{template}: standing at {label} at the start ended it: {r.Trace}");
            }
        }
    }

    /// <summary>
    /// The owner's job (run 850764, job 2, picks 2b,0, builder v6): T02's crossroad route ran past both of the crossroad's exits, the
    /// convoy parked beyond the east one and the start on its road between them. The spacing rule refuses such a start; the table's route
    /// now keeps inside the exits; the run's job 2 rolls clear.
    /// </summary>
    [Fact]
    public void TheOwnersCrossroadJobIsRefusedAndRollsClearNow()
    {
        var spec = new RemixSpec
        {
            Id = "xbt", Title = "T", Base = "t02", Start = new PoseSpec { At = [9367, 46135], Heading = 90 },
            Places = { ["route"] = [[10074, 46135], [9461, 46135], [8516, 46135], [7264, 46135]], ["exit"] = [[7264, 46135]], ["home"] = [[9367, 46135]] },
            Cars = { ["car1"] = new CarSpec { Vcf = "x", At = [10074, 46135] } },
        };
        spec.Objective.Template = "intercept";
        spec.Objective.Convoy = new ConvoySpec { Lead = "car1", Route = "route", Exit = new ZoneSpec { Place = "exit", Radius = 120 } };
        spec.Objective.Finish = new ZoneSpec { Place = "home", Radius = 100 };
        var sp = RemixSpacing.Of(spec);
        output.WriteLine(sp.ToString());
        Assert.Contains(sp.Problems, p => p.Contains("convoy's road", StringComparison.Ordinal));
        var crossroad = RemixNodes.Builtin().First(t => t.Base == "t02").Routes.First(r => r.Name == "crossroad_west");
        Assert.All(crossroad.Points, p => Assert.InRange(p[0], 7700, 9450));   // between the exits (7420, 9725) by 300 m and more
        if (Dir() is null) return;
        lock (Gate)
        {
            var (_, _, job) = RemixRun.Walk(850764, "2b,0");
            job.Id = "xbt";
            var (vfs, asm) = Build(job);
            var m = RemixBounds.Of(vfs, asm, job.Id, job, seats: 8);
            output.WriteLine($"run 850764 job 2 now: {job.Base} {job.Objective.Template}: {m}; {RemixSpacing.Of(job)}");
            Assert.Empty(RemixBounds.Problems(m));
            Assert.Empty(RemixSpacing.Of(job).Problems);
        }
    }
}
