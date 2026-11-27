using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Server;
using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

/// <summary>
/// ROGUETRIP in co-op (docs/remix-missions.md "Roguetrip in co-op"): the host's run in a co-op lobby game, its jobs built on every machine
/// from the same numbers (the hash checked), the host's route map, each player's damage carried by name, a death of the whole crew ending
/// the run, a player leaving mid-job, a run resumed with fewer players, and a build that cannot play it turned away.
/// </summary>
[Collection("UserMissionDir")]
public class RoguetripCoopTests(ITestOutputHelper output)
{
    private static string? Install => HostTests.InstallDir;

    /// <summary>A scratch folder for the generated missions, GameInstall.UserMissionDir while the test runs.</summary>
    private sealed class Scratch : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "i26-roguecoop-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);
        private readonly string? _before = GameInstall.UserMissionDir;
        public Scratch() { Directory.CreateDirectory(Dir); GameInstall.UserMissionDir = Dir; }
        public void Dispose()
        {
            GameInstall.UserMissionDir = _before;
            try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
        }
    }

    private CoopFlowTests.Game Host(string install, CoopRun run, double next = 1) =>
        new(install, output, nextSeconds: next, tweak: o => o with { Roguetrip = run });

    [Fact]
    public void TheStateTravelsOnTheWire()
    {
        var run = CoopRun.Start(4242);
        run.Crew.Add(new CrewCar { Name = "Ann", Car = "vppirna1.vcf", State = new RunCar { ArmourLeft = 61, ChassisLeft = 90, AmmoLeft = 40 }, Out = true });
        run.LastObjectives.Add(new CoopObjective { Text = "Roguetrip 4242, job 1: hit them.", Done = true });
        var offers = new List<RunOffer> { new() { Map = "t07", Name = "the farms", Template = "raid", Seed = 637742, Challenge = 0.8, Flavour = "Gas Bandit's crew..." } };
        var job = new CoopJob(4242, RemixBuilder.Version, 1, "t07", "raid", 637742, 0.8, 2);
        var st = RoguetripState.Of(run, "abc1234", offers, job, [1, 2, 3]);
        var w = new NetWriter();
        st.Write(w);
        var r = new NetReader(w.ToArray());
        Assert.Equal(MsgType.RoguetripState, (MsgType)r.U8());
        var back = RoguetripState.Read(ref r);
        Assert.Equal(job, back.Job);
        Assert.Equal(0.8, back.Job!.Challenge);   // the challenge goes as a double: the client's roll is the host's to the bit
        Assert.Equal([1, 2, 3], back.Hash);
        Assert.Equal("abc1234", back.Build);
        Assert.Equal(637742, back.Offers[0].Seed);
        Assert.Equal("Gas Bandit's crew...", back.Offers[0].Flavour);
        Assert.Equal(new RoguetripCrew("Ann", "vppirna1.vcf", 61, 90, 40, true), back.Crew[0]);
        Assert.True(back.Objectives[0].Done);
        Assert.Equal(0, r.Remaining);
        // the sheet a client draws: the run's number, phase and this player's own car
        var view = back.ToRun("Ann");
        Assert.Equal(4242, view.Number);
        Assert.Equal(61, view.State!.ArmourLeft);
        Assert.True((byte)MsgType.RoguetripState >= 60);
        Assert.Equal(PeerFeatures.Roguetrip, Protocol.Features & PeerFeatures.Roguetrip);
    }

    /// <summary>
    /// The host and a client build the same job from the same numbers (two installs' worth of builds, each into its own folder): the files
    /// and the hash are equal for every offer of 12 runs' first two jobs and for 1, 2 and 4 players; a crew of a different size is another job.
    /// </summary>
    [Fact]
    public void HostAndClientBuildTheSameJobFromTheSameNumbers()
    {
        if (Install is not { } inst) return;
        using var host = new Scratch();
        var clientDir = Path.Combine(host.Dir, "client");
        int checkedJobs = 0;
        for (int number = 101; number <= 1212; number += 101)
        {
            var run = CoopRun.Start(number);
            for (int leg = 0; leg < 2; leg++)
            {
                var offers = run.Offers();
                Assert.NotEmpty(offers);
                foreach (var offer in offers)
                    foreach (int players in new[] { 1, 2, 4 })
                    {
                        if (players != 2 && offer != offers[0]) continue;
                        var job = run.Job(offer, players);
                        var mine = job.Build(new GameInstall(inst));
                        mine.WriteTo(host.Dir);
                        // the client: its own install object, its own folder, the job from the wire
                        var w = new NetWriter();
                        RoguetripState.Of(run, "abc1234", offers, job, CoopJob.Hash(mine)).Write(w);
                        var rd = new NetReader(w.ToArray());
                        rd.U8();
                        var told = RoguetripState.Read(ref rd);
                        var theirs = told.Job!.Build(new GameInstall(inst));
                        theirs.WriteTo(clientDir);
                        Assert.Equal(mine.Mission, theirs.Mission);
                        Assert.Null(told.Verify(CoopJob.Hash(theirs), "abc1234"));
                        Assert.Equal(File.ReadAllBytes(Path.Combine(host.Dir, "xcoop.msn")), File.ReadAllBytes(Path.Combine(clientDir, "xcoop.msn")));
                        checkedJobs++;
                    }
                // on to the next job: the first offer taken and won
                run.Run.Take(offers[0]);
                run.Finish(RemixRun.Outcome.Won, 0, [], "", []);
                run.Debriefed();
            }
        }
        output.WriteLine($"{checkedJobs} jobs built twice alike");
        Assert.True(checkedJobs >= 60, $"{checkedJobs} jobs");
        // a crew of four is not a crew of one: more enemies
        var r1 = CoopRun.Start(4242);
        var o1 = r1.Offers()[0];
        int one = RemixRoles.Hostiles(r1.Job(o1, 1).Spec()).Count, four = RemixRoles.Hostiles(r1.Job(o1, 4).Spec()).Count;
        output.WriteLine($"run 4242's first offer ({o1.Map} {o1.Template}): {one} hostile cars for one, {four} for four");
        Assert.True(four > one, $"{one} for one, {four} for four");
    }

    [Fact]
    public void AnotherBuildsJobIsRefusedNamingBothBuilds()
    {
        string why = CoopJob.Mismatch("abc1234", "def5678");
        Assert.Contains("abc1234", why);
        Assert.Contains("def5678", why);
        var st = RoguetripState.Of(CoopRun.Start(7), "abc1234", [], new CoopJob(7, RemixBuilder.Version, 1, "t07", "raid", 5, 0.8, 2), [9, 9, 9]);
        Assert.Equal(why, st.Verify([1, 2, 3], "def5678"));
        Assert.Null(st.Verify([9, 9, 9], "def5678"));
        // a job by another version of the generator is not built at all: it would be another mission
        var other = new CoopJob(7, RemixBuilder.Version + 1, 1, "t07", "raid", 5, 0.8, 2);
        var ex = Assert.Throws<RemixError>(() => other.Build(new GameInstall(Install ?? Path.GetTempPath())));
        Assert.Contains($"v{RemixBuilder.Version + 1}", ex.Message);
        Assert.Contains($"v{RemixBuilder.Version}", ex.Message);
    }

    [Fact]
    public void AGameThatCannotBuildTheJobsIsTurnedAway()
    {
        if (Install is not { } inst) return;
        using var s = new Scratch();
        using var g = Host(inst, CoopRun.Start(31));
        g.Join("Hal");
        var old = new MeleeClient { Features = Protocol.Features & ~PeerFeatures.Roguetrip };
        old.Connect("127.0.0.1", g.Server.Port, "Old", Protocol.MissionCar);
        g.Players.Add(("Old", old));
        Assert.True(g.Wait(() => old.State is ClientState.Rejected or ClientState.Disconnected), $"{old.State}");
        Assert.Equal(Protocol.RoguetripNeeded, old.Reason);
    }

    /// <summary>
    /// Two players: the route map's offers and the host's pick reach the guest; the start builds the job for two and tells it before the
    /// accepts; the guest's car is hurt and the job won; back in the lobby the debrief, the guest's damage carried (patched), and in the next
    /// job the guest's car starts with it. The host's CONTINUE is the next choice.
    /// </summary>
    [Fact]
    public void TwoPlayersPlayTwoJobsAndCarryTheirDamage()
    {
        if (Install is not { } inst) return;
        using var s = new Scratch();
        var run = CoopRun.Start(4242);
        using var g = Host(inst, run);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        Assert.True(g.Wait(() => ann.Roguetrip is { Phase: "choose", Offers.Count: > 1 }), "the route map did not reach Ann");
        Assert.Null(g.Server.RoguetripSelect(1));
        Assert.True(g.Wait(() => ann.Roguetrip?.Selected == 1), "the host's pick did not reach Ann");
        var offer = ann.Roguetrip!.Offers[1];
        g.Start();
        Assert.Equal(CoopJob.Stem, ann.Accept!.Arena);
        var job = ann.Roguetrip!.Job!;
        Assert.Equal(2, job.Players);
        Assert.Equal(offer.Seed, job.Seed);
        Assert.Equal("playing", ann.Roguetrip.Phase);
        Assert.NotEmpty(ann.Roguetrip.Hash);
        // a co-op roguetrip has no respawn: the accept says so
        Assert.Equal(Protocol.CoopNoRespawn, ann.Accept.Coop!.RespawnDelay);
        // the guest's car hurt on the host, the job won
        uint tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 5, [hal, ann]), "the job did not start");
        int[] max = g.Server.Invoke(h => h.Seats.First(x => x.Name == "Ann").Actor.Car.Health!.ArmourMax.ToArray());
        g.Server.Invoke(h => { var d = h.Seats.First(x => x.Name == "Ann").Actor.Car.Health!; d.Armour[0] = max[0] / 10; d.Armour[1] = max[1] / 10; });
        g.Server.Invoke(h => h.Setup.Script!.Host.EndMission(true, 0, -2));
        Assert.True(g.WaitDriving(() => ann.State == ClientState.InLobby && ann.Roguetrip?.Phase == "debrief", [hal, ann], 15000), $"no debrief: {ann.State} {ann.Roguetrip?.Phase}");
        var crew = ann.Roguetrip!.Crew.Single(c => c.Name == "Ann");
        Assert.True(crew.Armour < 100 && !crew.Out, $"Ann's armour {crew.Armour}%");
        var carried = run.Of("Ann")!.State!;
        // patched after the win: more than the 10 % left on two sides, less than full
        Assert.True(carried.Armour[0] >= max[0] / 10 && carried.ArmourLeft < 100, string.Join(",", carried.Armour));
        Assert.Equal("won", run.Run.Legs[0].Outcome);
        Assert.Null(g.Server.RoguetripContinue());
        Assert.True(g.Wait(() => ann.Roguetrip is { Phase: "choose" }), "no next choice");
        Assert.Single(ann.Roguetrip!.Legs);
        // the next job: Ann's car starts it as it was carried
        Assert.Null(g.Server.StartLobby(force: true));
        Assert.True(g.Wait(() => ann.State == ClientState.Joined && ann.Roguetrip?.Job?.Job == 2), $"no second job: {ann.State}");
        int[] now = g.Server.Invoke(h => h.Seats.First(x => x.Name == "Ann").Actor.Car.Health!.Armour.ToArray());
        Assert.Equal(carried.Armour, now);
        int[] hals = g.Server.Invoke(h => h.Seats.First(x => x.Name == "Hal").Actor.Car.Health!.Armour.ToArray());
        int[] halMax = g.Server.Invoke(h => h.Seats.First(x => x.Name == "Hal").Actor.Car.Health!.ArmourMax.ToArray());
        Assert.Equal(halMax, hals);
    }

    /// <summary>The whole crew killed at once ends the run (the debrief, then the run's end); one leaving mid-job does not end it.</summary>
    [Fact]
    public void TheWholeCrewKilledEndsTheRunALeaverDoesNot()
    {
        if (Install is not { } inst) return;
        using var s = new Scratch();
        var run = CoopRun.Start(77);
        using var g = Host(inst, run);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        var bob = g.Join("Bob");
        g.Start();
        uint tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 5, [hal, ann, bob]), "the job did not start");
        // Bob leaves: the job goes on
        bob.Leave();
        g.Players.RemoveAll(p => p.Name == "Bob");
        g.WaitDriving(() => false, [hal, ann], 500);
        Assert.Null(g.Server.Invoke(h => h.Setup.Script!.Result));
        Assert.Equal("playing", run.Run.Phase);
        // Ann killed: the job goes on for Hal (no respawn)
        g.Server.Invoke(h => h.Setup.Script!.Host.Destroy(h.Setup.Entities.First(e => e.Vehicle == h.Seats.First(x => x.Name == "Ann").Actor.Car)));
        g.WaitDriving(() => false, [hal, ann], 1500);
        Assert.Null(g.Server.Invoke(h => h.Setup.Script!.Result));
        Assert.True(g.Server.Invoke(h => h.Seats.First(x => x.Name == "Ann").Actor.Destroyed));
        // Hal killed too: the job fails on the death line, and the run is over
        g.Server.Invoke(h => h.Setup.Script!.Host.Destroy(h.Setup.Entities.First(e => e.Vehicle == h.Seats.First(x => x.Name == "Hal").Actor.Car)));
        Assert.True(g.WaitDriving(() => ann.State == ClientState.InLobby && ann.Roguetrip?.Phase == "debrief", [hal, ann], 20000), $"no debrief: {ann.State} {ann.Roguetrip?.Phase}");
        Assert.Equal("dead", run.Run.Legs[^1].Outcome);
        Assert.All(run.Crew, c => Assert.True(c.Out));
        Assert.Null(g.Server.RoguetripContinue());
        Assert.True(g.Wait(() => ann.Roguetrip?.Phase == "over"), "the run did not end");
        Assert.NotNull(g.Server.StartLobby(force: true));   // nothing to start: the run is over
        Assert.Null(g.Server.RoguetripNewRun(78));
        Assert.True(g.Wait(() => ann.Roguetrip is { Run: 78, Phase: "choose" }), "no new run");
    }

    /// <summary>A run saved after its first job resumes with fewer players: the second job for one, the host's carried car.</summary>
    [Fact]
    public void ARunResumesWithFewerPlayers()
    {
        if (Install is not { } inst) return;
        using var s = new Scratch();
        var run = CoopRun.Start(555);
        string kept;
        using (var g = Host(inst, run))
        {
            var hal = g.Join("Hal");
            var ann = g.Join("Ann");
            g.Start();
            uint tick = g.Tick;
            Assert.True(g.WaitDriving(() => g.Tick > tick + 5, [hal, ann]), "the job did not start");
            g.Server.Invoke(h => { var d = h.Seats.First(x => x.Name == "Hal").Actor.Car.Health!; d.Chassis[2] = d.ChassisMax[2] / 3; });
            g.Server.Invoke(h => h.Setup.Script!.Host.EndMission(false, 0, 2));   // the job's own failure: a bust
            Assert.True(g.WaitDriving(() => hal.Roguetrip?.Phase == "debrief", [hal, ann], 15000), "no debrief");
            Assert.Equal("bust", run.Run.Legs[0].Outcome);
            run.Save(s.Dir);
            kept = File.ReadAllText(Path.Combine(s.Dir, CoopRun.FileName));
        }
        Assert.Contains("crew:", kept);
        var back = CoopRun.Load(s.Dir)!;
        Assert.Equal(555, back.Run.Number);
        Assert.Equal(1, back.Run.Busts);
        using var g2 = Host(inst, back);
        var hal2 = g2.Join("Hal");
        // the debrief is passed by the start itself, and the job (a step harder for the bust) is built for one
        g2.Start();
        Assert.Equal(1, hal2.Roguetrip!.Job!.Players);
        Assert.Equal(2, hal2.Roguetrip.Job.Job);
        Assert.Equal(RemixRun.Challenges[2], hal2.Roguetrip.Job.Challenge);
        int[] chassis = g2.Server.Invoke(h => h.Seats.First(x => x.Name == "Hal").Actor.Car.Health!.Chassis.ToArray());
        Assert.Equal(back.Of("Hal")!.State!.Chassis, chassis);
    }
}
