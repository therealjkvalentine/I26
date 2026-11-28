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
                run.SalvageDone();
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
        // three of the job's cars wrecked (the damage model's own death: their wrecks are the salvage), then the job won
        int wrecks = g.Server.Invoke(h =>
        {
            var cars = h.Setup.Entities.Where(e => e.Vehicle is { } v && !h.Setup.World.IsHuman(v) && !v.IsHeli && !v.IsEmplacement).Take(3).ToList();
            foreach (var e in cars) h.Setup.Damage.Die(e.Vehicle!, true);
            return cars.Count;
        });
        Assert.True(wrecks > 0, "no cars to wreck");
        g.Server.Invoke(h => h.Setup.Script!.Host.EndMission(true, 0, -2));
        Assert.True(g.WaitDriving(() => ann.State == ClientState.InLobby && ann.Roguetrip?.Phase == "debrief", [hal, ann], 15000), $"no debrief: {ann.State} {ann.Roguetrip?.Phase}");
        var crew = ann.Roguetrip!.Crew.Single(c => c.Name == "Ann");
        Assert.True(crew.Armour < 100 && !crew.Out, $"Ann's armour {crew.Armour}%");
        var hurt = run.Of("Ann")!.State!;
        Assert.Equal(max[0] / 10, hurt.Armour[0]);   // no plate at the debrief: it comes from the salvage's scrap
        Assert.Equal("won", run.Run.Legs[0].Outcome);
        Assert.NotNull(run.Of("Ann")!.Inventory);   // each player's garage kept by the host
        // past the debrief: the salvage draft, the same field on both machines after every move
        Assert.Null(g.Server.RoguetripContinue());
        Assert.True(g.Wait(() => ann.Salvage is not null && hal.Salvage is not null), "no salvage draft");
        Assert.Equal("salvage", ann.Roguetrip!.Phase);
        var field = ann.Salvage!.Items;
        output.WriteLine($"the field: {field.Count} parts ({string.Join(", ", field.Select(i => $"{i.Stem} {i.Health}"))})");
        Assert.NotEmpty(field);
        Assert.Equal("Hal", ann.Salvage.Turn);   // the host picks first
        var picks = new List<(string Who, string Stem)>();
        for (int k = 0; k < 3 && ann.Salvage!.Turn.Length > 0 && ann.Salvage.Items.Any(i => i.Taker.Length == 0); k++)
        {
            string who = ann.Salvage.Turn;
            var picker = who == "Hal" ? hal : ann;
            var item = ann.Salvage.Items.First(i => i.Taker.Length == 0);
            int before = ann.SalvageChanges;
            picker.SendPick(PickKind.Take, item.Id);
            Assert.True(g.Wait(() => ann.Salvage!.Items.Single(i => i.Id == item.Id).Taker == who && hal.Salvage!.Items.Single(i => i.Id == item.Id).Taker == who), $"{who}'s pick of {item.Stem} not seen");
            var server = g.Server.Draft!.Items.ToList();
            Assert.Equal(server, ann.Salvage!.Items);
            Assert.Equal(server, hal.Salvage!.Items);
            // a pick out of turn is refused
            (who == "Hal" ? hal : ann).SendPick(PickKind.Take, ann.Salvage.Items.FirstOrDefault(i => i.Taker.Length == 0)?.Id ?? 0);
            picks.Add((who, item.Stem));
        }
        g.Wait(() => false, 300);
        Assert.Equal(picks.Count, g.Server.Draft!.Items.Count(i => i.Taker.Length > 0));
        Assert.Equal(picks.Select(p => p.Who).Take(2), new[] { "Hal", "Ann" }.Take(picks.Count));   // turns round the crew
        hal.SendPick(PickKind.Done);
        ann.SendPick(PickKind.Done);
        Assert.True(g.Wait(() => ann.Salvage is { Garage: true } && hal.Salvage is { Garage: true }), "no garages");
        // Ann's pick is in her garage (the van), as the host keeps it and as her screen gets it
        foreach (var p in picks.Where(p => p.Who == "Ann")) Assert.Contains(p.Stem, ann.Salvage!.Mine, StringComparison.OrdinalIgnoreCase);
        int left = g.Server.Draft!.Left.Count();
        hal.SendGarage(hal.Salvage!.Mine);
        ann.SendGarage(ann.Salvage!.Mine);
        Assert.True(g.Wait(() => ann.Roguetrip is { Phase: "choose" } && ann.Salvage is null), "no next choice");
        Assert.Single(ann.Roguetrip!.Legs);
        var carried = run.Of("Ann")!.State!;
        // the plate: the scrap of what nobody took, shared out (a tenth of the full armour a part, the weakest side first)
        output.WriteLine($"{left} parts left: {left / 2} each; Ann's armour {string.Join(",", hurt.Armour)} -> {string.Join(",", carried.Armour)}");
        if (left >= 2) Assert.True(carried.Armour.Sum() > hurt.Armour.Sum(), "no plate");
        else Assert.Equal(hurt.Armour, carried.Armour);
        // the next job: Ann's car starts it as it was carried
        Assert.Null(g.Server.StartLobby(force: true));
        Assert.True(g.Wait(() => ann.State == ClientState.Joined && ann.Roguetrip?.Job?.Job == 2), $"no second job: {ann.State}");
        int[] now = g.Server.Invoke(h => h.Seats.First(x => x.Name == "Ann").Actor.Car.Health!.Armour.ToArray());
        Assert.Equal(carried.Armour, now);
        int[] hals = g.Server.Invoke(h => h.Seats.First(x => x.Name == "Hal").Actor.Car.Health!.Armour.ToArray());
        int[] halMax = g.Server.Invoke(h => h.Seats.First(x => x.Name == "Hal").Actor.Car.Health!.ArmourMax.ToArray());
        Assert.Equal(halMax, hals);
    }

    /// <summary>
    /// A won job's salvage and garage kept through a restart: Ann takes an engine from the field and fits it, Hal takes nothing; the run
    /// saved and hosted again, both come back in their garage's cars (configurations served by content), Ann's with her engine, and both
    /// garages as they were.
    /// </summary>
    [Fact]
    public void ARunResumesWithTheCrewsGarages()
    {
        if (Install is not { } inst) return;
        using var s = new Scratch();
        var run = CoopRun.Start(4242);
        int engine;
        string engineStem;
        using (var g = Host(inst, run))
        {
            var hal = g.Join("Hal");
            var ann = g.Join("Ann");
            Assert.True(g.Wait(() => ann.Roguetrip is { Phase: "choose", Offers.Count: > 1 }), "no route map");
            Assert.Null(g.Server.RoguetripSelect(1));
            g.Start();
            uint tick = g.Tick;
            Assert.True(g.WaitDriving(() => g.Tick > tick + 5, [hal, ann]), "the job did not start");
            g.Server.Invoke(h =>
            {
                foreach (var e in h.Setup.Entities.Where(e => e.Vehicle is { } v && !h.Setup.World.IsHuman(v) && !v.IsHeli && !v.IsEmplacement).Take(4).ToList())
                    h.Setup.Damage.Die(e.Vehicle!, true);
                return 0;
            });
            g.Server.Invoke(h => h.Setup.Script!.Host.EndMission(true, 0, -2));
            Assert.True(g.WaitDriving(() => ann.State == ClientState.InLobby && ann.Roguetrip?.Phase == "debrief", [hal, ann], 15000), "no debrief");
            Assert.Null(g.Server.RoguetripContinue());
            Assert.True(g.Wait(() => ann.Salvage is { Turn: "Hal" }), "no draft");
            hal.SendPick(PickKind.Done);
            Assert.True(g.Wait(() => ann.Salvage is { Turn: "Ann" }), "not Ann's pick");
            var vfs = g.Server.Invoke(h => h.Vfs);
            var db = I76.Sim.Trip.PartsDatabase.Build(vfs);
            string own = I76.Sim.Trip.TripInventory.Load(ann.Salvage!.Mine, db, vfs)!.Nodes.First(n => n.State == I76.Sim.Trip.PartState.Car && n.Part.Type == 2).Part.Stem;
            var item = ann.Salvage!.Items.FirstOrDefault(i => i.Stem.StartsWith("eng", StringComparison.OrdinalIgnoreCase) && !i.Stem.Equals(own, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(item);
            engineStem = item.Stem;
            ann.SendPick(PickKind.Take, item.Id);
            Assert.True(g.Wait(() => ann.Salvage!.Items.Single(i => i.Id == item.Id).Taker == "Ann"), "no pick");
            ann.SendPick(PickKind.Done);
            Assert.True(g.Wait(() => ann.Salvage is { Garage: true } && hal.Salvage is { Garage: true }), "no garages");
            // Ann's garage: the engine from her van into the car (her own was )
            output.WriteLine($"Ann fits {engineStem} in place of {own}");
            var inv = I76.Sim.Trip.TripInventory.Load(ann.Salvage!.Mine, db, vfs)!;
            var node = inv.Nodes.First(n => n.State == I76.Sim.Trip.PartState.Van && n.Part.Stem == engineStem);
            inv.Install(inv.Slots.FindIndex(sl => sl.Type == 2), node);
            Assert.Null(inv.Validate());
            engine = node.Part.Ordinal;
            ann.SendGarage(inv.Save());
            hal.SendGarage(hal.Salvage!.Mine);
            Assert.True(g.Wait(() => ann.Roguetrip is { Phase: "choose" }), "no next choice");
            run.Save(s.Dir);
        }
        var back = CoopRun.Load(s.Dir)!;
        Assert.Contains(engineStem, back.Of("Ann")!.Inventory!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(back.Of("Hal")!.Inventory);
        Assert.Equal("vppirna1.vcf", back.Of("Ann")!.Model);
        using var g2 = Host(inst, back);
        var hal2 = g2.Join("Hal");
        var ann2 = g2.Join("Ann");
        g2.Start();
        Assert.Equal(2, ann2.Roguetrip!.Job!.Job);
        var cars = g2.Server.Invoke(h => h.Seats.ToDictionary(x => x.Name, x => ((CoopActor)x.Actor).CarFile));
        var vfs2 = g2.Server.Invoke(h => h.Vfs);
        foreach (var (name, file) in cars)
        {
            Assert.StartsWith("n", file);   // the garage's car, served by its content
            var car = I76.Sim.Trip.TripCar.FromVcf(vfs2.TryRead(file)!, n => I76.Sim.Vehicles.VehicleFactory.EngineCompId(vfs2, n));
            var kept = I76.Sim.Trip.TripInventory.Load(back.Of(name)!.Inventory!, I76.Sim.Trip.PartsDatabase.Build(vfs2), vfs2)!.ToCar();
            Assert.Equal(kept.ToVcf(), car.ToVcf());
            if (name == "Ann") Assert.Equal(engine, car.Engine);
        }
        Assert.NotEqual(cars["Hal"], cars["Ann"]);
    }

    /// <summary>
    /// Four players draft one field: the turns go round all four in the lobby's order, every client's field equals the host's after every
    /// pick, and everyone's garage done ends it in the route map.
    /// </summary>
    [Fact]
    public void FourPlayersDraftOneField()
    {
        if (Install is not { } inst) return;
        using var s = new Scratch();
        var run = CoopRun.Start(4242);
        using var g = Host(inst, run);
        string[] names = ["Hal", "Ann", "Bob", "Cat"];
        var crew = names.Select(n => g.Join(n)).ToList();
        Assert.True(g.Wait(() => crew[3].Roguetrip is { Phase: "choose" }), "no route map");
        g.Start();
        uint tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 5, crew), "the job did not start");
        g.Server.Invoke(h =>
        {
            foreach (var e in h.Setup.Entities.Where(e => e.Vehicle is { } v && !h.Setup.World.IsHuman(v) && !v.IsHeli && !v.IsEmplacement).Take(4).ToList())
                h.Setup.Damage.Die(e.Vehicle!, true);
            return 0;
        });
        g.Server.Invoke(h => h.Setup.Script!.Host.EndMission(true, 0, -2));
        Assert.True(g.WaitDriving(() => crew.All(c => c.State == ClientState.InLobby && c.Roguetrip?.Phase == "debrief"), crew, 20000), "no debrief");
        Assert.Null(g.Server.RoguetripContinue());
        Assert.True(g.Wait(() => crew.All(c => c.Salvage is not null)), "no draft");
        var order = new List<string>();
        for (int k = 0; k < 8; k++)
        {
            var st = crew[0].Salvage!;
            if (st.Turn.Length == 0 || !st.Items.Any(i => i.Taker.Length == 0)) break;
            string who = st.Turn;
            order.Add(who);
            var item = st.Items.First(i => i.Taker.Length == 0);
            crew[Array.IndexOf(names, who)].SendPick(PickKind.Take, item.Id);
            Assert.True(g.Wait(() => crew.All(c => c.Salvage!.Items.Single(i => i.Id == item.Id).Taker == who)), $"{who}'s pick not seen by all");
            var server = g.Server.Draft!.Items.ToList();
            foreach (var c in crew) Assert.Equal(server, c.Salvage!.Items);
        }
        output.WriteLine($"turns: {string.Join(" ", order)}");
        Assert.Equal(names.Concat(names).Take(order.Count), order);
        foreach (var c in crew) c.SendPick(PickKind.Done);
        Assert.True(g.Wait(() => crew.All(c => c.Salvage is { Garage: true })), "no garages");
        foreach (var c in crew) c.SendGarage(c.Salvage!.Mine);
        Assert.True(g.Wait(() => crew.All(c => c.Roguetrip is { Phase: "choose" } && c.Salvage is null)), "no next choice");
        foreach (var n in names) Assert.NotNull(g.Server.Roguetrip!.Of(n)!.Inventory);
    }

    /// <summary>The draft's turns: the fewest turns had first, the host on a tie; a pass and a timeout count as a turn; READY is out for good; a leaver is out.</summary>
    [Fact]
    public void TheDraftGoesRoundTheCrew()
    {
        var d = new RoguetripDraft([("a", 1), ("b", 2), ("c", 3), ("d", 4), ("e", 5), ("f", 6), ("g", 7)], ["Hal", "Ann", "Bob"]);
        Assert.Equal("Hal", d.Turn);
        Assert.NotNull(d.Pick("Ann", 1));          // not her pick
        Assert.Null(d.Pick("Hal", 1));
        Assert.NotNull(d.Pick("Ann", 1));          // gone
        Assert.Null(d.Pick("Ann", 2));
        Assert.Equal("Bob", d.Turn);
        d.Tick(RoguetripDraft.PickSeconds + 0.1);  // Bob lets it run out: a pass
        Assert.Equal("Hal", d.Turn);
        Assert.Equal(1, d.Crew[2].Turns);
        d.Done("Hal");                              // Hal is done picking
        Assert.Equal("Ann", d.Turn);
        Assert.Null(d.Pick("Ann", 3));
        Assert.Equal("Bob", d.Turn);
        d.Leave("Bob");
        Assert.Equal("Ann", d.Turn);               // only Ann now, until she is done or the field is empty
        Assert.Null(d.Pick("Ann", 4));
        Assert.Equal("Ann", d.Turn);
        d.Done("Ann");
        Assert.True(d.DraftOver);
        Assert.True(d.Garage);
        Assert.Equal(3, d.Left.Count());
        Assert.Equal(1, d.ScrapShare);
        Assert.False(d.Over);
        d.GarageDone("Hal");
        Assert.False(d.Over);
        d.GarageDone("Ann");
        Assert.True(d.Over);                        // Bob left: counted ready
        Assert.Equal(["a"], d.TakenBy("Hal").Select(i => i.Stem));
        Assert.Equal(["b", "c", "d"], d.TakenBy("Ann").Select(i => i.Stem));
        // an empty field goes straight to the garages; their clock ends them
        var e = new RoguetripDraft([], ["Hal", "Ann"]);
        Assert.True(e.Garage);
        e.Tick(RoguetripDraft.GarageSeconds + 1);
        Assert.True(e.Over);
    }

    [Fact]
    public void TheSalvageTravelsOnTheWire()
    {
        var d = new RoguetripDraft([("eng6", 1500), ("brk3", 90)], ["Hal", "Ann"]);
        d.Pick("Hal", 2);
        var w = new NetWriter();
        RoguetripSalvage.Of(d, "{\"nodes\":[]}").Write(w);
        var r = new NetReader(w.ToArray());
        Assert.Equal(MsgType.RoguetripSalvage, (MsgType)r.U8());
        var back = RoguetripSalvage.Read(ref r);
        Assert.Equal(d.Items, back.Items);
        Assert.Equal(d.Crew, back.Crew);
        Assert.Equal("Ann", back.Turn);
        Assert.Equal("{\"nodes\":[]}", back.Mine);
        Assert.Equal(0, r.Remaining);
        w.Clear();
        RoguetripMoves.Pick(w, PickKind.Take, 7);
        r = new NetReader(w.ToArray());
        Assert.Equal(MsgType.RoguetripPick, (MsgType)r.U8());
        Assert.Equal((PickKind.Take, 7), RoguetripMoves.ReadPick(ref r));
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
