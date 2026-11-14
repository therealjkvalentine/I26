using Godot;
using I76.Formats.Vfs;
using I76.Net;

namespace I76.Game.Boot;

/// <summary>
/// The fork's co-op flow in the game (docs/multiplayer.md "The briefing for everyone"): with a host that says
/// <see cref="PeerFeatures.CoopSkip"/>, a lobby start's briefing plays over the built mission, as the original plays it after the load, this
/// game's inputs held back meanwhile so that the server's start waits for it; its skip is told to the host, and the host's word that
/// everyone has loaded and somebody skipped ends it here too, with a line saying who.
/// </summary>
public partial class Main
{
    /// <summary>The briefing to play once the mission is built (null: none, or it plays before the build, upstream's order).</summary>
    private string? _netBriefing;
    /// <summary>The briefing playing over the built mission.</summary>
    private I76.Game.Ui.MoviePlayer? _netBriefingPlayer;

    /// <summary>A mission's briefing movie (WRLD +0x04) when the install has it, else null.</summary>
    private string? BriefingOf(string mission)
    {
        var install = new GameInstall(_opt.Install!);
        try
        {
            if (install.FindMission(mission) is { } path
                && I76.Formats.Mission.WorldSettings.Read(new I76.Formats.Mission.MissionFile(path))?.Briefing is { Length: > 0 } name
                && I76.Game.Ui.MoviePlayer.Find(install, name) is not null) return name;
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or System.ArgumentException) { }
        return null;
    }

    /// <summary>
    /// The mission is built (the end of the load): a network co-op game's briefing to play now plays over it, and true is returned (the
    /// music follows the briefing); otherwise the game says it is ready and false is returned.
    /// </summary>
    private bool NetBriefingAfterLoad()
    {
        if (_net is null || _netAccept?.Coop is null) return false;
        var c = _net.Client;
        string? name = _netBriefing;
        _netBriefing = null;
        if (name is null || _opt.Install is null || I76.Game.Ui.MoviePlayer.Find(new GameInstall(_opt.Install), name) is not { } path)
        {
            c.SendCoopStage(CoopStage.Ready);
            return false;
        }
        var install = new GameInstall(_opt.Install);
        DropMenuMusic();
        I76.Game.Ui.MoviePlayer player;
        try { player = new I76.Game.Ui.MoviePlayer { Path = path, Faithful = _opt.Mode1997, Nitro = install.IsNitro, Name = "Movie" }; }
        catch (System.Exception ex)
        {
            GD.PrintErr($"movie {name}: not played: {ex}");
            c.SendCoopStage(CoopStage.Ready);
            return false;
        }
        player.Finished += () =>
        {
            bool skipped = player.Skipped;
            GD.Print($"movie: {name} over here{(skipped ? " (skipped)" : "")}");
            _netBriefingPlayer = null;
            // a skip first, so that the host can end the others' briefings; then this game's inputs come
            if (skipped) c.SendCoopStage(CoopStage.Skipped);
            c.SendCoopStage(CoopStage.Ready);
            if (IsInsideTree()) StartMissionMusic();
        };
        _netBriefingPlayer = player;
        GD.Print($"movie: {name} (the briefing, over the built mission)");
        c.SendCoopStage(CoopStage.Briefing);
        AddChild(player);
        return true;
    }

    // ---- on to the next mission (docs/multiplayer.md "On to the next mission") ----

    /// <summary>The won mission's outro playing (each player's own), before the next mission is built.</summary>
    private I76.Game.Ui.MoviePlayer? _netOutro;

    /// <summary>This game is a co-op game whose host goes on after the mission (both say <see cref="PeerFeatures.CoopNext"/>).</summary>
    private bool NetCoopFlow => _net is not null && _netAccept?.Coop is not null && _net.Client.Has(PeerFeatures.CoopNext);

    /// <summary>The listen host of such a game: its choices are its menu's.</summary>
    private bool NetCoopHost => NetCoopFlow && _net!.Server is not null;

    /// <summary>The host has a plan for after the mission (or this game is still in the last one's ending): no timing out back to the menus meanwhile.</summary>
    private bool NetCoopGoesOn(MeleeClient c) => _netOutro is not null || c.Has(PeerFeatures.CoopNext) && c.Plan is not null;

    /// <summary>
    /// The script's end in a co-op game that goes on: a win plays the mission's outro (WRLD +0x11) over the game, each player's own, and the
    /// game stays: no way back to the menus, the next mission follows (the server's NextRound, carried into once the outro is over). True
    /// when this took the end; false leaves it to the single-player end (a failure, or a host that does not go on).
    /// </summary>
    private bool NetCoopEnded(bool won)
    {
        if (!NetCoopFlow || !won) return false;
        var c = _net!.Client;
        GD.Print("network: the co-op mission is won: the group goes on");
        if (!_opt.SkipMovies && _builder?.Settings is { Outro.Length: > 0 } ws && _opt.Install is { } dir && I76.Game.Ui.MoviePlayer.Find(new GameInstall(dir), ws.Outro) is { } path)
        {
            var install = new GameInstall(dir);
            GetNodeOrNull<AudioStreamPlayer>("Music")?.Stop();
            var player = new I76.Game.Ui.MoviePlayer { Path = path, Faithful = _opt.Mode1997, Nitro = install.IsNitro, Name = "Movie" };
            player.Finished += () => { GD.Print($"movie: {ws.Outro} over here"); _netOutro = null; };
            _netOutro = player;
            c.SendCoopStage(CoopStage.Ending);
            GD.Print($"movie: {ws.Outro} (the outro)");
            AddChild(player);
        }
        return true;
    }

    private string CoopTitle(string mission) => _opt.Install is { } i ? I76.Formats.Mission.MissionTitles.CoopTitle(new GameInstall(i), mission) : mission.ToUpperInvariant();

    /// <summary>The next mission's title when the host can take the group on to it now (its menu's Next Mission), else null.</summary>
    private string? NetCoopNextTitle() => NetCoopHost && _net!.Server!.CoopPlanNow is { Kind: CoopPlanKind.Next } p ? CoopTitle(p.Mission) : null;

    /// <summary>The host can take the group back to the lobby (its menu's Back to Lobby): the mission is over and the group goes on.</summary>
    private bool NetCoopCanLobby() => NetCoopHost && _net!.Server!.CoopPlanNow is not null;

    /// <summary>The host's choice from its menu, for everyone.</summary>
    private void NetCoopChoose(I76.Game.Ui.MenuOutcome o)
    {
        if (_net?.Server is not { } server) return;
        var kind = o == I76.Game.Ui.MenuOutcome.CoopNext ? CoopPlanKind.Next : CoopPlanKind.Lobby;
        GD.Print($"network: co-op: the host chooses {kind}: {server.CoopChoose(kind) ?? "done"}");
    }

    /// <summary>The host's word that everyone has loaded and somebody skipped: this game's briefing ends too, and the feed says who skipped.</summary>
    private void NetBriefingSkip(MeleeClient c)
    {
        if (c.SkippedBy is not { } skip) return;
        c.SkippedBy = null;
        if (_netBriefingPlayer is not { } p || !IsInstanceValid(p)) return;
        GD.Print($"network: {skip.Line}: the briefing ends here too");
        _netHud?.Say(skip.Line);
        p.End();
    }
}
