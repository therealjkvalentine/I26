using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// ROGUETRIP in co-op (docs/remix-missions.md "Roguetrip in co-op"): every template's job played by a crew of two and of four (the
/// enemies scaled for them) ends as one player's does, its tests on "the player" meaning any living human; nobody is failed at the
/// start by a teammate left behind; one player killed does not fail the job, the whole crew killed does (a death, the run's end).
/// </summary>
[Collection("remix")]   // GameInstall.UserMissionDir is one for the process
public class RemixCoopTests(ITestOutputHelper output)
{
    private static readonly object Gate = new();
    private static string? _dir;

    private static string? Dir()
    {
        if (SnapshotRig.Install is null) return null;
        lock (Gate)
        {
            _dir ??= Path.Combine(Path.GetTempPath(), "i26-remix-coop-tests-" + Environment.ProcessId);
            GameInstall.UserMissionDir = _dir;
            return _dir;
        }
    }

    /// <summary>A template's job on the map its seed rolls, as a co-op roguetrip's first job for this many players.</summary>
    private static CoopJob JobOf(string template, int players, int seed = 11)
    {
        var spec = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Template = template, Challenge = 1.0 });
        return new CoopJob(4242, RemixBuilder.Version, 1, spec.Base, template, seed, 1.0, players);
    }

    private (RemixSpec Spec, AssetVfs Vfs, I76.Formats.Assembly.ModelAssembler Asm) Build(CoopJob job)
    {
        var install = new GameInstall(SnapshotRig.Install!);
        job.Build(install).WriteTo(Dir()!);
        var vfs = new AssetVfs(install);
        return (job.Spec(), vfs, new I76.Formats.Assembly.ModelAssembler(vfs));
    }

    [Theory]
    [InlineData("deliver")]
    [InlineData("tail")]
    [InlineData("intercept")]
    [InlineData("ambush")]
    [InlineData("defend")]
    [InlineData("escort")]
    [InlineData("raid")]
    public void EachTemplateEndsForACrewOfTwoAndFourAsForOne(string template)
    {
        if (Dir() is null) return;
        lock (Gate)
        {
            var results = new Dictionary<int, RemixCheck.WalkResult>();
            foreach (int n in new[] { 1, 2, 4 })
            {
                var job = JobOf(template, n);
                var (spec, vfs, asm) = Build(job);
                var r = RemixCheck.Walk(vfs, asm, CoopJob.Stem, spec, seats: n);
                output.WriteLine($"{template} x{n} ({job.Map}, {RemixRoles.Hostiles(spec).Count} hostile cars): {r.Trace}");
                results[n] = r;
            }
            // the walk wins it alone; a crew of two and four, the first walked and the others parked at the start, win it too
            Assert.True(results[1].Won == true, $"{template} alone: {results[1].Trace}");
            Assert.True(results[2].Won == true, $"{template} x2: {results[2].Trace}");
            Assert.True(results[4].Won == true, $"{template} x4: {results[4].Trace}");
        }
    }

    /// <summary>The crew idle at the start for 45 s: no template fails them for a teammate left behind (the T17 class of bug: a test on "the player" at the start).</summary>
    [Theory]
    [InlineData("deliver")]
    [InlineData("tail")]
    [InlineData("intercept")]
    [InlineData("ambush")]
    [InlineData("defend")]
    [InlineData("escort")]
    [InlineData("raid")]
    public void NoTemplateFailsAnIdleCrewAtTheStart(string template)
    {
        if (Dir() is null) return;
        lock (Gate)
        {
            foreach (int n in new[] { 2, 4 })
            {
                var job = JobOf(template, n);
                var (spec, vfs, asm) = Build(job);
                var r = RemixCheck.Walk(vfs, asm, CoopJob.Stem, spec, seats: n, horizon: 45, walk: false);
                output.WriteLine($"{template} x{n} idle: {r.Trace}");
                // still running, or (a crew that sits still under fire: a raid's guards woken by the scaled crowd) every one of them wrecked and
                // the job failed on a death line; never failed on any other line while everyone is still at the start
                bool allWrecked = Enumerable.Range(0, n).All(k => r.Trace.Contains($"seat{k}wrecked", StringComparison.Ordinal));
                Assert.True(r.Won is null || r.Won == false && allWrecked && RemixRun.Classify(false, r.Fail, spec.Text.Failures.Count, false) == RemixRun.Outcome.Dead,
                    $"{template} x{n}: ended at {r.Seconds:0} s with nobody moving ({r.Trace})");
            }
        }
    }

    /// <summary>One of two killed: the job goes on for the other (out of it, no respawn); both killed: the job fails on the death line, which ends a run.</summary>
    [Fact]
    public void OneKilledGoesOnTheWholeCrewKilledIsADeath()
    {
        if (Dir() is null) return;
        lock (Gate)
        {
            var job = JobOf("deliver", 2);
            var (spec, vfs, asm) = Build(job);
            var one = RemixCheck.Walk(vfs, asm, CoopJob.Stem, spec, seats: 2, kill: [(1, 8f)], horizon: 40, walk: false);
            output.WriteLine($"one killed: {one.Trace}");
            Assert.True(one.Won is null, one.Trace);
            var both = RemixCheck.Walk(vfs, asm, CoopJob.Stem, spec, seats: 2, kill: [(1, 8f), (0, 12f)], horizon: 40, walk: false);
            output.WriteLine($"both killed: {both.Trace}");
            Assert.True(both.Won == false, both.Trace);
            Assert.Equal(RemixRun.Outcome.Dead, RemixRun.Classify(false, both.Fail, spec.Text.Failures.Count, playerDead: false));
            // one killed and the other walking it through: won
            var won = RemixCheck.Walk(vfs, asm, CoopJob.Stem, spec, seats: 2, kill: [(1, 8f)]);
            output.WriteLine($"one killed, the other plays on: {won.Trace}");
            Assert.True(won.Won == true, won.Trace);
        }
    }
}
