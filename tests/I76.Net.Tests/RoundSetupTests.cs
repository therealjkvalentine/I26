using I76.Net;
using I76.Server;
using I76.Sim.Missions;
using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

/// <summary>
/// The host's in-game GAME SETUP (the play fork's; docs/multiplayer.md "Changing the game for the next round"): a melee's host changes the next
/// round while the game runs; everyone is told at once in the chat, and the next round is built from the change, its accept carrying the new
/// limit and rules to a fork player and an upstream one alike. Nothing new on the wire.
/// </summary>
public class RoundSetupTests(ITestOutputHelper output)
{
    private static ServerOptions Opts => new() { Install = "unused", Arena = "m01", Name = "Pit", MaxPlayers = 8, MaxScore = 20000, Bots = 1 };

    [Fact]
    public void ASetupGoesIntoTheOptionsAndLeavesTheRestAlone()
    {
        var o = Opts with { TeamPlay = true, Rate = I76.Sim.Session.SimRate.FromLabel(30), AllowHidden = true, Kind = MeleeKind.Combat };
        var s = RoundSetup.Of(o) with { Arena = "m05", MaxScore = 0, Goal = MeleeGoal.Kills, GoalValue = 30, Bots = 3, Handicaps = HandicapMode.Auto };
        var n = s.Into(o);
        Assert.Equal(("m05", 0, MeleeGoal.Kills, 30, 3, HandicapMode.Auto), (n.Arena, n.MaxScore, n.Goal, n.GoalValue, n.Bots, n.Handicaps));
        // the kind, team play, the rate and the car rules are the game's
        Assert.Equal((true, 30, true, MeleeKind.Combat), (n.TeamPlay, n.Rate.Label, n.AllowHidden, n.Kind));
        Assert.Equal(s, RoundSetup.Of(n));
        Assert.DoesNotContain("secret", (s with { Password = "secret" }).ToString());
    }

    [Fact]
    public void EachChangeIsALineInTheFormsWords()
    {
        var was = RoundSetup.Of(Opts);
        Assert.Empty(RoundSetup.Lines(was, was, a => a));
        var now = was with { Arena = "m05", MaxScore = 0, Goal = MeleeGoal.Kills, GoalValue = 30, Bots = 3, InviteOnly = true, AllowCb = false, Password = "x", TimeLimit = 0 };
        var lines = RoundSetup.Lines(was, now, a => a == "m05" ? "The Crater" : a);
        output.WriteLine(string.Join("\n", lines));
        Assert.Equal(
        [
            "Next round: the arena is The Crater", "Next round: the limit is 30 kills (was 20000 points)", "Next round: bots 3 (was 1)",
            "Next round: invite only on", "Next round: CB radio off", "Next round: password set",
        ], lines);
        Assert.Equal("10 minutes", RoundSetup.LimitWord(0, MeleeGoal.None, 0, 600));
        Assert.Equal("20 kills or 5 minutes", RoundSetup.LimitWord(20000, MeleeGoal.Kills, 20, 300));
        Assert.Equal("no limit", RoundSetup.LimitWord(0, MeleeGoal.Laps, 0, 0));
        Assert.Equal("5 laps", RoundSetup.LimitWord(0, MeleeGoal.Laps, 5, 0));
    }

    /// <summary>
    /// A melee of two (a fork build and an upstream one) on m01 with a 3 s limit: the host sets the next round to m05, 5000 points, invite only,
    /// CB off and one bot; both players hear it at once, the round in play goes on unchanged, and the next round is m05 with those rules, in
    /// both accepts. A change the next round cannot take (another kind of arena, an arena the install lacks, fewer seats than players, a game
    /// without rounds) is refused with the reason and changes nothing.
    /// </summary>
    [Fact]
    public void TheNextRoundIsBuiltFromTheHostsChange()
    {
        if (HostTests.InstallDir is not { } install) return;
        // (an install with a race arena of its own, for the refusal of another kind)
        using var scratch = new ScratchMapInstall(install, "round");
        scratch.SyntheticMap("m01", "zzlap", ".rac");
        using var server = new NetMeleeServer(new ServerOptions
        {
            Install = scratch.Root, Arena = "m01", Port = 0, TimeLimit = 3, Rounds = true, RoundPause = 1, MaxPlayers = 4, Name = "Pit", ListenHost = false,
        });
        server.Log += m => output.WriteLine($"[server] {m}");
        server.Start();
        using var fork = new MeleeClient();
        using var upstream = new MeleeClient { Features = PeerFeatures.None };
        fork.Connect("127.0.0.1", server.Port, "Fork", "vcgcour1.vcf");
        upstream.Connect("127.0.0.1", server.Port, "Up", "valepre1.vcf");
        bool Wait(Func<bool> done, int ms = 20000)
        {
            for (int t = 0; t < ms / 10 && !done(); t++) { fork.Poll(); upstream.Poll(); Thread.Sleep(10); }
            return done();
        }
        Assert.True(Wait(() => fork.State == ClientState.Joined && upstream.State == ClientState.Joined));
        var first = server.Host;
        var now = server.NextRoundSetup!;
        Assert.Equal(("m01", 0, true), (now.Arena, now.MaxScore, now.AllowCb));

        // refusals: nothing changes
        Assert.Contains("another kind", server.SetNextRound(now with { Arena = "zzlap.rac" }));
        Assert.Contains("not an arena", server.SetNextRound(now with { Arena = "zznosuch" }));
        Assert.Contains("2 players", server.SetNextRound(now with { MaxPlayers = 1 }));
        Assert.False(server.NextRoundChanged);

        // the change: told at once, the round in play untouched
        var want = now with { Arena = "m05", MaxScore = 5000, InviteOnly = true, AllowCb = false, Bots = 1 };
        Assert.Null(server.SetNextRound(want));
        Assert.True(server.NextRoundChanged);
        Assert.Equal(want, server.NextRoundSetup);
        Assert.Same(first, server.Host);
        Assert.Equal(("m01", 0, true), (server.Options.Arena, server.Options.MaxScore, server.Options.AllowCb));
        string Said(MeleeClient c) => string.Join("\n", c.Events.SelectMany(e => e.Events).OfType<ChatEvent>().Select(e => e.Text));
        Assert.True(Wait(() => Said(fork).Contains("Next round: the arena is") && Said(upstream).Contains("Next round: the arena is")));
        Assert.Contains("Next round: the limit is 5000 points or 3 s (was 3 s)", Said(upstream));
        Assert.Contains("Next round: CB radio off", Said(fork));
        // the same again is nothing new to say
        Assert.Null(server.SetNextRound(want));

        // the next round: m05 with the new rules, for both builds
        Assert.True(Wait(() => fork.Rounds == 1 && upstream.Rounds == 1 && fork.State == ClientState.Joined && upstream.State == ClientState.Joined
            && fork.Accept is { Arena: "m05" } && upstream.Accept is { Arena: "m05" }, 30000));
        Assert.NotSame(first, server.Host);
        foreach (var c in new[] { fork, upstream })
        {
            Assert.Equal(5000, c.Accept!.MaxScore);
            Assert.False(c.Accept.Cb);
            Assert.False(c.CbAllowed);
        }
        Assert.Equal(("m05", 5000, true, false, 1), (server.Options.Arena, server.Options.MaxScore, server.Options.InviteOnly, server.Options.AllowCb, server.Options.Bots));
        Assert.Equal(3f, server.Options.TimeLimit);       // what the host did not change stays
        Assert.Single(server.Host.Bots);                    // the bot the host asked for drives in the new round
        Assert.False(server.NextRoundChanged);             // taken: the round after starts from it
        Assert.Equal(server.Options.Arena, server.NextRoundSetup!.Arena);
    }

    [Fact]
    public void AGameWithoutRoundsHasNoNextRound()
    {
        if (HostTests.InstallDir is not { } install) return;
        using var server = new NetMeleeServer(new ServerOptions { Install = install, Arena = "m01", Port = 0, Rounds = false });
        Assert.Null(server.NextRoundSetup);
        Assert.Contains("no next round", server.SetNextRound(RoundSetup.Of(server.Options) with { MaxScore = 100 }));
    }
}
