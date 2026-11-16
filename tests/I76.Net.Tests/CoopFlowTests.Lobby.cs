using I76.Formats.Mission;
using I76.Formats.Vfs;
using I76.Net;
using I76.Server;
using Xunit;

namespace I76.Net.Tests;

/// <summary>
/// Changing the game in the co-op lobby (the fork's; docs/multiplayer.md "Changing the game in the lobby"): the host's mission and settings
/// between missions, told to everyone in the lobby in the messages upstream's lobby reads (the lobby itself and its chat), so a player on an
/// upstream build follows too; nothing new on the wire.
/// </summary>
public partial class CoopFlowTests
{
    private static List<string> Lines(MeleeClient c)
    {
        var l = new List<string>();
        while (c.LobbyLines.TryDequeue(out var line)) l.Add($"{line.Name}: {line.Text}");
        return l;
    }

    [Fact]
    public void EachChangeIsALineInTheHostFormsWords()
    {
        var was = new LobbySetup("t01", "Scene 5: Save Taurus", 4, false, false, true, true, 0, 1f, false, "");
        Assert.Empty(LobbySetup.Lines(was, was, m => m));
        var now = was with { Mission = "t02", Name = "Out for blood", MaxPlayers = 6, CoopScale = 3, CoopDamage = 1.5f, InviteOnly = true, AllowHidden = true, AllowCarReset = false, AllowCb = false, Turrets1997 = true, Password = "x" };
        Assert.Equal(
        [
            "The host changed the mission: T02", "Game name: Out for blood", "No. drivers: 6 (was 4)", "Extra enemies: +1/player (was Off)",
            "Damage taken: x1.5 (was x1)", "Invite only: on", "Hidden cars: allowed", "Car reset: off", "CB radio: off", "Turrets: 1997 CD", "Password: set",
        ], LobbySetup.Lines(was, now, m => m.ToUpperInvariant()));
        Assert.Equal(["Extra enemies: Up to +2 (was Up to +1)", "Password: changed"], LobbySetup.Lines(was with { CoopScale = 1, Password = "a" }, was with { CoopScale = 2, Password = "b" }, m => m));
        // the record prints no password
        Assert.DoesNotContain("secret", (was with { Password = "secret" }).ToString());
    }

    /// <summary>
    /// The host picks another mission and settings in the lobby: everyone there sees the new mission in the lobby and a line for each change,
    /// an upstream player too (they are upstream's messages), the listing follows, and the start plays the new mission with the new settings.
    /// </summary>
    [Fact]
    public void TheHostChangesTheMissionAndEveryoneLoadsIt()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t01");
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        var old = g.Join("Old", upstream: true);
        g.Pump(200);
        foreach (var (_, c) in g.Players) Lines(c);
        var setup = g.Server.Setup!;
        Assert.Equal("t01", setup.Mission);
        Assert.Null(g.Server.ChangeLobby(setup with { Mission = "t02", CoopScale = 2, CoopDamage = 1.5f }));
        string title = MissionTitles.CoopTitle(new GameInstall(install), "t02");
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.Lobby?.Mission == "t02")), string.Join(", ", g.Players.Select(p => $"{p.Name} {p.Client.Lobby?.Mission}")));
        g.Pump(200);
        foreach (var c in new[] { ann, old })
        {
            var said = Lines(c);
            Assert.Contains($"Server: The host changed the mission: {title}", said);
            Assert.Contains("Server: Extra enemies: Up to +2 (was Off)", said);
            Assert.Contains("Server: Damage taken: x1.5 (was x1)", said);
        }
        // everyone is still in the lobby, as they were
        Assert.All(g.Players, p => Assert.Equal(ClientState.InLobby, p.Client.State));
        Assert.Equal(["Hal", "Ann", "Old"], hal.Lobby!.Members.Select(m => m.Name));
        // the listing (the LAN answer and the internet list's tag are made from it) says the new mission
        var info = g.Server.Invoke(h => h.Info());
        Assert.Equal(("t02", 2, 1.5f), (info.Arena, info.CoopScale, info.CoopDamage));
        g.Start();
        Assert.All(g.Players, p => Assert.Equal("t02", p.Client.Accept!.Arena));
        var mode = g.Server.Invoke(h => (CoopMode)h.Mode);
        Assert.Equal(2, mode.Build.Coop!.ExtraEnemies);
        Assert.Equal(1.5f, mode.Build.Coop.DamageFactor);
        Assert.Equal(3, g.Server.Invoke(h => h.Seats.Count));
    }

    /// <summary>A change of settings alone: told, and the accepts and the game's next mission (the trip's) are built with it.</summary>
    [Fact]
    public void ASettingChangeIsToldAndTheNextMissionsBuildCarriesIt()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05", nextSeconds: 1);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Pump(200);
        Lines(ann);
        var s = g.Server.Setup!;
        Assert.Null(g.Server.ChangeLobby(s with { AllowCarReset = false, AllowCb = false, Turrets1997 = true, InviteOnly = true, CoopScale = 1, MaxPlayers = 3 }));
        Assert.True(g.Wait(() => ann.Lobby?.Max == 3));
        g.Pump(200);
        var said = Lines(ann);
        Assert.Equal(["Server: No. drivers: 3 (was 4)", "Server: Extra enemies: Up to +1 (was Off)", "Server: Invite only: on", "Server: Car reset: off", "Server: CB radio: off", "Server: Turrets: 1997 CD"], said);
        Assert.Equal("t05", ann.Lobby!.Mission);
        g.Start();
        Assert.False(ann.Accept!.Cb);
        Assert.True(ann.Accept.Turrets1997);
        Assert.Equal((false, true, 3), g.Server.Invoke(h => (h.Options.AllowCarReset, h.Options.InviteOnly, h.Options.MaxPlayers)));
        // the trip's next mission is built from the game's options: the change goes on with it
        g.PlayAndEnd(won: true);
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.State == ClientState.Joined && p.Client.Accept!.Arena == "t06"), 20000));
        Assert.Equal(1, g.Server.Invoke(h => ((CoopMode)h.Mode).Build.Coop!.ExtraEnemies));
        Assert.True(ann.Accept!.Turrets1997);
    }

    /// <summary>Only between missions, only what can be played, and only the host: a guest's lobby messages change nothing.</summary>
    [Fact]
    public void AChangeIsRefusedOnceTheMissionHasStartedAndAGuestCannotMakeOne()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t01");
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        var s = g.Server.Setup!;
        Assert.NotNull(g.Server.ChangeLobby(s with { Mission = "m01" }));            // a melee arena is no story mission
        Assert.NotNull(g.Server.ChangeLobby(s with { MaxPlayers = 1 }));             // two are here
        Assert.NotNull(g.Server.ChangeLobby(s with { CoopDamage = 3f }));
        Assert.NotNull(g.Server.ChangeLobby(s with { CoopScale = 9 }));
        Assert.NotNull(g.Server.SetLobby("sim-rate", "60"));                          // not a lobby setting
        // a guest has no way to: its start, chat and car change leave the mission as it is
        ann.SendChat("/mission t02");
        ann.SendLobbySet(true, null, null);
        g.Pump(500);
        Assert.Equal("t01", g.Server.Setup!.Mission);
        Assert.True(g.Server.InLobby);
        Assert.Equal("t01", ann.Lobby!.Mission);
        // once started, the change waits for the lobby (Back to Lobby)
        g.Start();
        Assert.Contains("the mission has started", g.Server.ChangeLobby(s with { Mission = "t02" }));
        Assert.Equal("t01", g.Server.Setup!.Mission);
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Lobby));
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.State == ClientState.InLobby)));
        Assert.Null(g.Server.ChangeLobby(g.Server.Setup! with { Mission = "t02" }));
        Assert.True(g.Wait(() => ann.Lobby?.Mission == "t02"));
        Assert.Null(g.Server.StartLobby());
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.State == ClientState.Joined && p.Client.Accept!.Arena == "t02")));
    }

    /// <summary>
    /// A rule that refuses a car already chosen (Hidden Cars turned off under a hidden car) does not drop its player: the car is taken off
    /// them, they are in the mission's own car, not ready, and told why as a refused car is; the others hear it.
    /// </summary>
    [Fact]
    public void HiddenCarsOffPutsAHiddenCarBackInTheMissionsOwnAndSaysWhy()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t01", tweak: o => o with { AllowHidden = true });
        var hal = g.Join("Hal");
        var ann = g.Join("Ann", car: "vuchuey1.vcf");
        ann.SendLobbySet(true, null, null);
        Assert.True(g.Wait(() => hal.Lobby!.Members.Any(m => m.Name == "Ann" && m.Ready && m.Car == "vuchuey1.vcf")));
        Lines(hal);
        Assert.Null(g.Server.ChangeLobby(g.Server.Setup! with { AllowHidden = false }));
        Assert.True(g.Wait(() => ann.ReconfigureAnswers.Count > 0 && ann.Lobby!.Me!.Car == Protocol.MissionCar));
        var a = ann.ReconfigureAnswers.Dequeue();
        Assert.False(a.Taken);
        Assert.Contains("mission's own car", a.Text);
        Assert.False(ann.Lobby!.Me!.Ready);
        Assert.Equal(ClientState.InLobby, ann.State);
        g.Pump(200);
        var said = Lines(hal);
        Assert.Contains("Server: Hidden cars: not allowed", said);
        Assert.Contains("Server: Ann's car is not allowed now: the mission's own car until another is picked", said);
        // and the car is refused as a join's would be
        ann.SendLobbySet(false, "vuchuey1.vcf", null);
        Assert.True(g.Wait(() => ann.ReconfigureAnswers.Count > 0));
        Assert.False(ann.ReconfigureAnswers.Dequeue().Taken);
    }

    /// <summary>A dedicated server has no host in the lobby: its console changes the game (i26-server's mission and set).</summary>
    [Fact]
    public void ADedicatedServersConsoleChangesTheLobby()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t01", listenHost: false, tweak: o => o with { Name = MissionTitles.CoopTitle(new GameInstall(o.Install), "t01") });
        var ann = g.Join("Ann");
        Assert.Null(g.Server.SetLobby("mission", "t03"));
        Assert.Null(g.Server.SetLobby("extra-enemies", "3"));
        Assert.Null(g.Server.SetLobby("damage", "0.75"));
        Assert.Null(g.Server.SetLobby("cb", "off"));
        Assert.NotNull(g.Server.SetLobby("damage", "lots"));
        var s = g.Server.Setup!;
        // a game named after its mission takes the new one's name
        Assert.Equal(("t03", MissionTitles.CoopTitle(new GameInstall(install), "t03"), 3, 0.75f, false), (s.Mission, s.Name, s.CoopScale, s.CoopDamage, s.AllowCb));
        Assert.True(g.Wait(() => ann.Lobby?.Mission == "t03"));
        Assert.Equal(s.Name, g.Server.Invoke(h => h.Info()).Name);
    }

    /// <summary>A host migration's successor takes the game's options from the savestate: the lobby's changes go across with them.</summary>
    [Fact]
    public void ASuccessorHostHasTheChangedSettings()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t01", tweak: o => o with { HostMigration = true });
        var hal = g.Join("Hal");
        var standby = new MigrationStandby(install, m => output.WriteLine($"[Ann standby] {m}"));
        var ann = new MeleeClient { CanHost = true, MigrationGiveUp = 6, Standby = standby, Features = Protocol.Features };
        ann.Joined += _ => ann.SendCoopStage(CoopStage.Building);
        ann.Log += m => output.WriteLine($"[Ann] {m}");
        ann.Connect("127.0.0.1", g.Server.Port, "Ann", Protocol.MissionCar);
        g.Players.Add(("Ann", ann));
        try
        {
            Assert.True(g.Wait(() => ann.State == ClientState.InLobby));
            Assert.Null(g.Server.ChangeLobby(g.Server.Setup! with { Mission = "t02", CoopScale = 2, CoopDamage = 0.5f, AllowCarReset = false }));
            Assert.True(g.Wait(() => ann.Lobby?.Mission == "t02"));
            g.Start();
            g.Server.MigrationOwner = hal.Accept!.Slot;
            uint now = g.Tick;
            Assert.True(g.WaitDriving(() => g.Server.SuccessorSlot == ann.Accept!.Slot && ann.MigrationLink is { State: not null } l && l.StateTick > now && standby.Ready, [hal, ann], 30000),
                "no savestate, or the standby game was not built");
            var (o, _, _) = NetMeleeServer.ReadMigrationOptions(ann.MigrationLink!.State!, install);
            Assert.Equal(("t02", 2, 0.5f, false), (o.Coop, o.CoopScale, o.CoopDamage, o.AllowCarReset));
            // the host goes: Ann's game takes over with them
            hal.Dispose();
            g.Server.Dispose();
            Assert.True(g.WaitDriving(() => ann.BecameHost && standby.Server is not null, [ann], 20000), $"{ann.State} {ann.Reason}");
            var taken = standby.Server!.Invoke(h => h.Options);
            Assert.Equal(("t02", 2, 0.5f, false), (taken.Coop, taken.CoopScale, taken.CoopDamage, taken.AllowCarReset));
            Assert.Equal(2, standby.Server.Invoke(h => ((CoopMode)h.Mode).Build.Coop!.ExtraEnemies));
        }
        finally { standby.Server?.Dispose(); standby.Dispose(); }
    }
}
