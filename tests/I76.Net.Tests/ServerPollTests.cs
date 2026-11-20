using I76.Net;
using I76.Server;
using I76.Sim.Missions;
using I76.Sim.Trip;
using Xunit;

namespace I76.Net.Tests;

/// <summary>
/// The server's network poll (its socket's events between ticks: joins, messages, leaves) and its faults: a players' report
/// of a host whose game ended with "network poll failed: System.OverflowException: Arithmetic operation resulted in an overflow"
/// and, on the host's own screen, "Migrating host...". The overflow is the car check's armour total (a join's or a respawn's own
/// variant whose armour and chassis add up past what an int holds); a join the checks fail on is turned away, not the game
/// ended; and a server that does stop on a fault says so to its players at once.
/// </summary>
public class ServerPollTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>A Piranha variant whose armour and chassis total more than an int holds (each zone is a 32-bit field of the file).</summary>
    private static byte[] Overflowing(I76.Formats.Vfs.AssetVfs vfs) => VariantTests.Custom(vfs, c =>
    {
        c.Armour = [int.MaxValue, int.MaxValue, 1, 1];
        c.Chassis = [1, 1, 1, 1];
    });

    private static bool Wait(Func<bool> done, Action poll, int ms = 20000)
    {
        for (int t = 0; t < ms / 10 && !done(); t++) { poll(); Thread.Sleep(10); }
        return done();
    }

    /// <summary>The car check adds the zones as 64-bit numbers: a total past an int is refused, not an exception.</summary>
    [Fact]
    public void ACarWhoseArmourTotalOverflowsAnIntIsRefusedByTheCheck()
    {
        if (HostTests.InstallDir is null) return;
        var host = new MeleeHost(new ServerOptions { Install = HostTests.InstallDir, Arena = "m01" });
        var vcf = Overflowing(host.Vfs);
        var why = host.CheckJoin("vppirna4.vcf", vcf);
        output.WriteLine($"refused: {why}");
        Assert.NotNull(why);
        Assert.Contains("armour beyond what a car can carry", why);
        // the largest total an int holds is not refused for its size (whatever else the garage's check says of it)
        var big = VariantTests.Custom(host.Vfs, c => { c.Armour = [int.MaxValue - 4, 0, 0, 0]; c.Chassis = [1, 1, 1, 1]; });
        CarConfigCheck.Check(host.Vfs, PartsDatabase.Build(host.Vfs), "vppirna", big, out var error);
        output.WriteLine($"at the limit: {(error.Length == 0 ? "taken" : error)}");
        Assert.DoesNotContain("beyond what a car can carry", error);
    }

    /// <summary>
    /// Over the wire: a joiner with that car is turned away with the reason, a seated player who asks for it at a respawn is
    /// told no, and the game plays on for everyone (it used to end there, in the poll).
    /// </summary>
    [Fact]
    public void AJoinOrRespawnWithAnOverflowingCarIsRefusedAndTheGamePlaysOn()
    {
        if (HostTests.InstallDir is not { } install) return;
        using var server = new NetMeleeServer(new ServerOptions { Install = install, Arena = "m01", Port = 0, MaxPlayers = 8 });
        var lines = new List<string>();
        server.Log += m => { lock (lines) lines.Add(m); output.WriteLine($"[server] {m}"); };
        server.Start();
        var vcf = Overflowing(new I76.Formats.Vfs.AssetVfs(new I76.Formats.Vfs.GameInstall(install)));
        using var bad = new MeleeClient();
        bad.Connect("127.0.0.1", server.Port, "Armoured", "vppirna4.vcf", config: vcf);
        Assert.True(Wait(() => bad.State is ClientState.Rejected or ClientState.Disconnected, bad.Poll), "the join is answered");
        output.WriteLine($"the join: {bad.Reason}");
        Assert.Contains("armour beyond what a car can carry", bad.Reason ?? "");
        Assert.True(server.Running, "the server plays on after the join");
        Assert.Null(server.Fault);
        // a player in the game asks for it while dead (Reconfigure): refused, the game goes on
        using var good = new MeleeClient();
        good.Connect("127.0.0.1", server.Port, "Plain", "vppirna1.vcf");
        Assert.True(Wait(() => good.State == ClientState.Joined && good.Snapshots.Count > 5, good.Poll));
        server.Invoke(h => h.Setup.Damage.Destroy(h.Seats.Single().Player.Car));
        Assert.True(Wait(() => good.Snapshots.Count > 0 && server.Invoke(h => h.Seats.Single().Player.Destroyed), good.Poll));
        good.SendReconfigure("vppirna4.vcf", vcf);
        Assert.True(Wait(() => good.ReconfigureAnswers.Count > 0, good.Poll), "the respawn's car is answered");
        var answer = good.ReconfigureAnswers.Dequeue();
        output.WriteLine($"the respawn's car: {answer.Taken} {answer.Text}");
        Assert.False(answer.Taken);
        Assert.True(server.Running, "the server plays on after the respawn's car");
        Assert.Null(server.Fault);
        int ticks = server.Invoke(h => (int)h.Tick);
        Assert.True(Wait(() => server.Invoke(h => (int)h.Tick) > ticks + 20, good.Poll), "the game goes on ticking");
        lock (lines) Assert.DoesNotContain(lines, l => l.Contains("network poll failed"));
    }

    /// <summary>
    /// A server that stops on a fault in a game nobody takes over tells its players why at once: they used to stay joined to a
    /// socket that still answered, with nothing more coming.
    /// </summary>
    [Fact]
    public void AServerThatStopsOnAFaultTellsItsPlayersAtOnce()
    {
        if (HostTests.InstallDir is not { } install) return;
        using var server = new NetMeleeServer(new ServerOptions { Install = install, Arena = "m01", Port = 0 });
        server.Log += m => output.WriteLine($"[server] {m}");
        server.Log += m => { if (m.StartsWith("chat: Plain: fault", StringComparison.Ordinal)) throw new InvalidOperationException("injected"); };
        server.Start();
        using var c = new MeleeClient();
        c.Connect("127.0.0.1", server.Port, "Plain", "vppirna1.vcf");
        Assert.True(Wait(() => c.State == ClientState.Joined && c.Snapshots.Count > 5, c.Poll));
        var cut = System.Diagnostics.Stopwatch.StartNew();
        c.SendChat("fault");
        Assert.True(Wait(() => c.State == ClientState.Disconnected, c.Poll, 10000), $"still {c.State}");
        output.WriteLine($"told after {cut.Elapsed.TotalSeconds:0.00} s: {c.Reason}");
        Assert.Equal("the server stopped (InvalidOperationException)", c.Reason);
        Assert.True(cut.Elapsed.TotalSeconds < 2);
        Assert.IsType<InvalidOperationException>(server.Fault);
    }

    private static string? Nitro
    {
        get
        {
            var d = Environment.GetEnvironmentVariable("I76_NITRO_INSTALL") is { Length: > 0 } e ? e : Environment.GetEnvironmentVariable("I76_NITRO");
            return d is { Length: > 0 } && Directory.Exists(d) ? d : null;
        }
    }

    /// <summary>
    /// A long three-team capture the flag over the wire, as the report's game was: the bots and three headless players (the load
    /// test's drivers) on N12.CF3 with the host form's largest goal and a long clock, host migration on, and everything the poll
    /// handles going on all the while: inputs, chat, the cars asked about, deaths and respawns in another car, players leaving and
    /// joining again on every team, session queries. The server must never fault. Long: I76_NET_SOAK_SECONDS (90 by default) of
    /// the network clock (I76_NET_TIME_SCALE runs it faster); with the Nitro install only.
    /// </summary>
    [Fact]
    [Trait("Load", "Sensitive")]
    public void ALongThreeTeamCtfGameWithBotsAndPlayersNeverFaultsTheServer()
    {
        if (Nitro is not { } nitro) return;
        double seconds = double.TryParse(Environment.GetEnvironmentVariable("I76_NET_SOAK_SECONDS"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 90;
        using var server = new NetMeleeServer(new ServerOptions
        {
            Install = nitro, Arena = "N12.CF3", Port = 0, Kind = MeleeKind.Ctf, CtfTeams = 3, Goal = MeleeGoal.Captures, GoalValue = 99, TimeLimit = 99 * 60,
            Bots = 5, MaxPlayers = 8, BotSkill = BotSkill.Hard, HostMigration = true, ListenHost = true, RespawnDelay = 2,
        });
        var failed = new List<string>();
        int captures = 0, deaths = 0, rejoins = 0, refits = 0, answers = 0;
        server.Log += m =>
        {
            if (m.Contains("failed")) lock (failed) failed.Add(m);
        };
        server.Start();
        var cars = new[] { "vppirna1.vcf", "vcgcour1.vcf", "valepre1.vcf" };
        var players = Enumerable.Range(0, 3).Select(i => (Name: $"Soak{i + 1}", Team: (byte)(i + 1), Driver: new BotDriver(roams: i % 2 == 1, seed: i + 1), Client: new MeleeClient())).ToArray();
        foreach (var p in players) p.Client.Connect("127.0.0.1", server.Port, p.Name, cars[p.Team - 1], p.Team);
        using var browser = new SessionBrowser();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double next = 0, nextChat = 5, nextKill = 8, nextLeave = 20, nextAsk = 1, tick = server.Host.Session.TickSeconds;
        int leaver = 0, n = 0, kills = 0;
        while (NetClock.Seconds(clock) < seconds && server.Running)
        {
            double now = NetClock.Seconds(clock);
            for (int i = 0; i < players.Length; i++)
            {
                var c = players[i].Client;
                c.Poll();
                while (c.Events.Count > 0) c.Events.Dequeue();
                while (c.CarAnswers.Count > 0) players[i].Driver.Learn(c.CarAnswers.Dequeue());
                while (c.ReconfigureAnswers.Count > 0) { c.ReconfigureAnswers.Dequeue(); answers++; }
                while (c.Snapshots.Count > 4) c.Snapshots.RemoveAt(0);
            }
            if (now >= next)
            {
                next += tick;
                n++;
                foreach (var p in players)
                {
                    if (p.Client.State != ClientState.Joined) continue;
                    p.Client.SendInput(p.Driver.Next(p.Client));
                    if (n % 20 == 0 && p.Driver.Unknown(p.Client) is { Count: > 0 } unknown) p.Client.AskCars(unknown);
                }
            }
            if (now >= nextChat) { nextChat += 3; if (players[n % 3].Client.State == ClientState.Joined) players[n % 3].Client.SendChat($"chat at {now:0} s"); }
            if (now >= nextAsk) { nextAsk += 2; browser.Ask("127.0.0.1", server.Port); }
            browser.Poll();
            if (now >= nextKill)
            {
                // a death every 10 s, a carrier's every third (the others are let run for home); a dead player respawns in another car
                nextKill += 10;
                kills++;
                server.Invoke(h =>
                {
                    var carrier = kills % 3 == 0 ? h.Game.Ctf!.Flags.Select(f => f.Carrier).FirstOrDefault(c => c is not null) : null;
                    var victim = carrier ?? h.Game.Players.Where(p => !p.Destroyed && h.Game.Ctf!.Carried(p) is null).OrderBy(p => p.Car.NetId).ElementAtOrDefault(n % 5);
                    if (victim is not null && !victim.Destroyed) { h.Setup.Damage.Destroy(victim.Car); deaths++; }
                });
                foreach (var p in players)
                    if (p.Client.State == ClientState.Joined && server.Invoke(h => h.Seats.FirstOrDefault(x => x.Name == p.Name)?.Player.Destroyed == true))
                    {
                        p.Client.SendReconfigure(cars[(p.Team + refits) % 3], null);
                        refits++;
                    }
            }
            if (now >= nextLeave)
            {
                // a player leaves and joins again (each team in turn)
                nextLeave += 15;
                var p = players[leaver++ % 3];
                p.Client.Leave();
                p.Client.Dispose();
                int k = Array.FindIndex(players, x => x.Name == p.Name);
                players[k] = p with { Client = new MeleeClient() };
                players[k].Client.Connect("127.0.0.1", server.Port, p.Name, cars[p.Team - 1], p.Team);
                rejoins++;
            }
            Thread.Sleep(1);
        }
        var (tickNow, teams, over) = server.Invoke(h => (h.Tick, string.Join(" ", h.Game.Ctf!.TeamCaptures.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")), h.Over));
        captures = server.Invoke(h => h.Game.Ctf!.TeamCaptures.Values.Sum());
        output.WriteLine($"{NetClock.Seconds(clock):0} s, tick {tickNow}: {captures} captures (teams {teams}), {deaths} deaths caused, {refits} respawns in another car ({answers} answered), {rejoins} rejoins, {browser.Sessions.Count} session(s) seen, over {over}");
        foreach (var p in players) p.Client.Dispose();
        lock (failed) foreach (var f in failed) output.WriteLine(f);
        Assert.Null(server.Fault);
        Assert.True(server.Running, "the server still runs");
        lock (failed) Assert.Empty(failed);
    }
}
