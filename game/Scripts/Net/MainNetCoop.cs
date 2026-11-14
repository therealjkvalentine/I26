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
