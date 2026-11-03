using I76.Net;
using I76.Server;
using I76.Sim.Missions;
using Xunit;

namespace I76.Net.Tests;

/// <summary>
/// The host's Handicaps (docs/multiplayer.md "Handicaps"; protocol 23): the rule rides in the join accept and the listing, each
/// player's level in the scoreboard's rows; the host alone sets a level, and only where its rule is on.
/// </summary>
public class HandicapTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static Scoreboard Through(Scoreboard s, Dictionary<byte, PlayerRow> sent, Dictionary<byte, PlayerRow> board, out int bytes)
    {
        var w = new NetWriter();
        s.Write(w, sent);
        bytes = w.Length;
        var r = new NetReader(w.Span);
        Assert.Equal((byte)MsgType.Scoreboard, r.U8());
        return Scoreboard.Read(ref r, board);
    }

    [Fact]
    public void TheLevelRidesInTheScoreboardInFullAndAsAChange()
    {
        var rows = new List<PlayerRow>
        {
            new(0, "a", 0, 100, 1, 0, 40, 5, true) { Handicap = -3 },
            new(1, "b", 0, 0, 0, 1, 60, 6, true),
            new(5, "c", 0, 0, 0, 0, 0, 7, true) { Handicap = 3 },
        };
        var sent = new Dictionary<byte, PlayerRow>();
        var board = new Dictionary<byte, PlayerRow>();
        var first = Through(new Scoreboard(20, 1, rows), sent, board, out _);
        Assert.Equal(rows, first.Players);
        Assert.Equal([-3, 0, 3], first.Players.Select(p => (int)p.Handicap));
        // only b's level changed: one row, a few bytes, and the others keep theirs
        rows[1] = rows[1] with { Handicap = 2 };
        var second = Through(new Scoreboard(40, 2, rows), sent, board, out int delta);
        Assert.True(delta < 20, $"{delta} B");
        Assert.Equal([-3, 2, 3], second.Players.Select(p => (int)p.Handicap));
        // back to none
        rows[0] = rows[0] with { Handicap = 0 };
        var third = Through(new Scoreboard(60, 3, rows), sent, board, out _);
        Assert.Equal([0, 2, 3], third.Players.Select(p => (int)p.Handicap));
    }

    [Fact]
    public void TheRuleRidesInTheAcceptAndTheListing()
    {
        var acc = new JoinAccept(2, 17, "m05", 1234, "vppirna1.vcf", false, 0, 0, 5, [], []);
        foreach (var on in new[] { HandicapMode.Off, HandicapMode.Manual, HandicapMode.Auto })
        {
            var w = new NetWriter();
            (acc with { Handicaps = on }).Write(w);
            var r = new NetReader(w.Span);
            r.U8();
            var back = JoinAccept.Read(ref r);
            Assert.Equal(on, back.Handicaps);
            Assert.True(back.Cb);
            w.Clear();
            var info = new SessionInfo(7, Protocol.Version, "Night", "M05.MSN", 1, 8, false, false, 0, 0, false, [], Turrets: true, Hidden: true) { Handicaps = on };
            info.Write(w);
            var listed = SessionInfo.TryRead(w.Span)!;
            Assert.Equal((on, true, true), (listed.Handicaps, listed.Turrets, listed.Hidden));
        }
    }

    /// <summary>The scoreboard shows a level by the name, and the host's picked player's in brackets (even 0).</summary>
    [Fact]
    public void TheScoreWindowShowsTheLevelByTheName()
    {
        var rows = new List<PlayerRow>
        {
            new(0, "host", 0, 0, 0, 0, 0, 1, true),
            new(1, "ace", 0, 300, 3, 0, 40, 2, true) { Handicap = -2 },
            new(2, "rookie", 0, 0, 0, 2, 40, 3, true),
        };
        var c = new I76.Net.Hud.ScoreContext(MeleeKind.Combat, false, 0, true, I76.Net.Hud.ScoreOrder.Slot, 10) { HandicapPick = 2 };
        var view = I76.Net.Hud.ScoreTable.Players(rows, c, 0, _ => "");
        Assert.Equal(["host", "ace (-2)", "rookie [0]"], view.Lines.Select(l => l.Name));
        // the boot keys still name the player alone
        Assert.Equal(["ace", "rookie"], view.Numbered.Select(n => n.Name));
    }

    /// <summary>
    /// The host sets a level on a seat and on a bot (clamped to −5..+5), the scoreboard's rows carry it and a respawn keeps it;
    /// where the rule is off the host's request is refused and nothing changes.
    /// </summary>
    [Fact]
    public void TheHostSetsALevelOnlyWithTheRuleOn()
    {
        if (HostTests.InstallDir is not { } install) return;
        var off = new MeleeHost(new ServerOptions { Install = install, Arena = "m05", Seed = 7 });
        var a0 = off.Join("a", "vppirna1.vcf", 0);
        Assert.NotNull(off.SetHandicap(a0.Slot, 2));
        Assert.Equal(0, a0.Actor.Car.Handicap);

        var host = new MeleeHost(new ServerOptions { Install = install, Arena = "m05", Seed = 7, Handicaps = HandicapMode.Manual, Bots = 1, MaxPlayers = 4 });
        var a = host.Join("a", "vppirna1.vcf", 0);
        Assert.Null(host.SetHandicap(a.Slot, 9));
        Assert.Equal(5, a.Actor.Car.Handicap);          // clamped to the table's reach
        var bot = host.Bots.Single();
        Assert.Null(host.SetHandicap(bot.Slot, -1));
        Assert.Equal(-1, bot.Player.Car.Handicap);
        Assert.NotNull(host.SetHandicap(30, 1));
        var rows = host.Scoreboard().Players;
        Assert.Equal(5, rows.Single(r => r.Slot == a.Slot).Handicap);
        Assert.Equal(-1, rows.Single(r => r.Slot == bot.Slot).Handicap);
        // the car is the player's through a respawn (rebuilt in place)
        MeleeGame.Rebuild(a.Actor.Car, new I76.Sim.Vehicles.Vehicle(), new I76.Sim.Rng.CrtRand(1));
        Assert.Equal(5, host.HandicapOf(a.Slot));
    }

    /// <summary>
    /// Over the wire: a client of a game with the rule on is told so in its accept and sees the level the listen host sets on it in its
    /// scoreboard; a client has no message to set one (the host's server alone does), and a game without the rule refuses the host too.
    /// </summary>
    [Fact]
    public void AClientSeesTheHostsLevelAndCannotSetOne()
    {
        if (HostTests.InstallDir is not { } install) return;
        Assert.DoesNotContain(Enum.GetNames<MsgType>(), n => n.Contains("Handicap"));
        using var server = new NetMeleeServer(new ServerOptions { Install = install, Arena = "m05", Port = 0, MaxPlayers = 4, Handicaps = HandicapMode.Manual });
        server.Log += m => output.WriteLine($"[server] {m}");
        server.Start();
        using var c = new MeleeClient();
        c.Connect("127.0.0.1", server.Port, "Taurus", "vppirna1.vcf");
        Assert.True(Wait(c, () => c.State == ClientState.Joined && c.Scores is { Players.Count: 1 }));
        Assert.Equal(HandicapMode.Manual, c.Accept!.Handicaps);
        byte slot = c.Accept.Slot;
        Assert.Equal(0, c.Scores!.Players.Single().Handicap);
        Assert.Null(server.SetHandicap(slot, -2));
        Assert.True(Wait(c, () => c.Scores!.Players.Single().Handicap == -2), "the client sees the host's level");
        Assert.Equal(-2, server.HandicapOf(slot));

        using var plain = new NetMeleeServer(new ServerOptions { Install = install, Arena = "m05", Port = 0, MaxPlayers = 4 });
        plain.Start();
        using var d = new MeleeClient();
        d.Connect("127.0.0.1", plain.Port, "Mule", "vppirna1.vcf");
        Assert.True(Wait(d, () => d.State == ClientState.Joined && d.Scores is { Players.Count: 1 }));
        Assert.Equal(HandicapMode.Off, d.Accept!.Handicaps);
        Assert.NotNull(plain.SetHandicap(d.Accept.Slot, 1));
        Assert.Equal(0, plain.HandicapOf(d.Accept.Slot));
    }

    /// <summary>A scripted kill of <paramref name="victim"/> by <paramref name="killer"/>: rounds on the front until the car is dead, then the tick that books it.</summary>
    private static void Kill(MeleeHost host, I76.Sim.Vehicles.Vehicle killer, I76.Sim.Vehicles.Vehicle victim)
    {
        for (int k = 0; k < 400 && !I76.Sim.Damage.DamageModel.IsDead(victim); k++)
        {
            var p = new I76.Sim.Weapons.DamagePacket { Mask = 2, Kind = I76.Sim.Damage.DamageKind.Round, Attacker = killer };
            p.Ch[1] = 40;
            host.Setup.Damage.Apply(victim, p, new System.Numerics.Vector3(0, 0.5f, 2));
        }
        Assert.True(I76.Sim.Damage.DamageModel.IsDead(victim));
        host.Step();
    }

    /// <summary>
    /// Auto (docs/multiplayer.md "Auto handicap"): the game sets the levels from the standings, a kill at a time, says each move to
    /// everyone as a console line, the scoreboard carries the levels, the host's keys and console are refused, and a successor host
    /// restores the standings with the game.
    /// </summary>
    [Fact]
    public void AutoSetsTheLevelsFromTheKillsAndSaysSo()
    {
        if (HostTests.InstallDir is not { } install) return;
        var o = new ServerOptions { Install = install, Arena = "m05", Seed = 7, Handicaps = HandicapMode.Auto, Bots = 4, MaxPlayers = 6 };
        var host = new MeleeHost(o);
        Assert.True(host.Game.Rules.AutoHandicaps);
        for (int k = 0; k < 20; k++) host.Step();
        var bots = host.Bots.OrderBy(b => b.Slot).ToList();
        var (a, b) = (bots[0], bots[1]);
        host.TakeEvents();
        Kill(host, a.Player.Car, b.Player.Car);
        // one net kill over the table's average takes a to -1, b to +1, said to everyone
        Assert.Equal((1, 1, -1, 1), (a.Player.Kills, b.Player.Deaths, a.Player.Car.Handicap, b.Player.Car.Handicap));
        var said = host.TakeEvents().OfType<ChatEvent>().Where(c => c.From < 0).Select(c => c.Text).ToList();
        Assert.Contains(Handicaps.Announce(a.Name, -1), said);
        Assert.Contains(Handicaps.Announce(b.Name, 1), said);
        // b comes back (5 s and a moment), and goes again: two net kills over, a -2 and b +2, the others still 0
        for (int k = 0; k < 20 * 12 && b.Player.Destroyed; k++) host.Step();
        Assert.False(b.Player.Destroyed);
        host.TakeEvents();
        Kill(host, a.Player.Car, b.Player.Car);
        Assert.Equal((-2, 2, 0, 0), (a.Player.Car.Handicap, b.Player.Car.Handicap, bots[2].Player.Car.Handicap, bots[3].Player.Car.Handicap));
        said = host.TakeEvents().OfType<ChatEvent>().Where(c => c.From < 0).Select(c => c.Text).ToList();
        Assert.Contains(Handicaps.Announce(a.Name, -2), said);
        Assert.Contains(Handicaps.Announce(b.Name, 2), said);
        var rows = host.Scoreboard().Players;
        Assert.Equal((-2, 2), (rows.Single(r => r.Slot == a.Slot).Handicap, rows.Single(r => r.Slot == b.Slot).Handicap));
        // the host cannot set a level by hand in an Auto game
        Assert.Equal("this game's handicaps are automatic", host.SetHandicap(a.Slot, 2));
        Assert.Equal(-2, host.HandicapOf(a.Slot));
        // a successor host restores the standings and the levels with the game
        var state = host.Session.Snapshot();
        var fresh = new MeleeHost(o);
        fresh.RestoreMigrated(state);
        Assert.True(fresh.Game.Rules.AutoHandicaps);
        Assert.Equal(host.Game.Players.Select(p => (p.Form, p.Car.Handicap)), fresh.Game.Players.Select(p => (p.Form, p.Car.Handicap)));
        Assert.Equal((2f, -2f), (fresh.Game.Players.Single(p => p.Name == a.Name).Form, fresh.Game.Players.Single(p => p.Name == b.Name).Form));
    }

    private static bool Wait(MeleeClient c, Func<bool> done, int ms = 30000)
    {
        for (int t = 0; t < ms / 10 && !done(); t++) { c.Poll(); Thread.Sleep(10); }
        return done();
    }
}
