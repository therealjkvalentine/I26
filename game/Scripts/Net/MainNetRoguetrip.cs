using Godot;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Net;
using I76.Server;

namespace I76.Game.Boot;

/// <summary>
/// ROGUETRIP in co-op in the game (docs/remix-missions.md "Roguetrip in co-op"): the host's run (user://remix/coop-run.yaml) handed to
/// its listen server and kept as it changes; on every player's game the job built from the host's numbers before the accept is read,
/// its hash checked against the host's; the lobby's ROUTE MAP and debrief.
/// </summary>
public partial class Main
{
    /// <summary>The co-op run to host: the one in progress, else a new one (I76_RUN_NUMBER for a capture, else a new number).</summary>
    private ServerOptions RoguetripHost(ServerOptions o)
    {
        GameInstall.UserMissionDir = I76.Game.Shell.RemixScenarios.Dir;
        var run = CoopRun.Load(I76.Game.Shell.RemixScenarios.Dir);
        if (run is null || run.Run.Over)
        {
            int n = int.TryParse(System.Environment.GetEnvironmentVariable("I76_RUN_NUMBER"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int g) && g > 0
                ? g : System.Random.Shared.Next(1, 1_000_000);
            run = CoopRun.Start(n);
            GD.Print($"remix: ROGUETRIP co-op: a new run, {n}");
        }
        else GD.Print($"remix: ROGUETRIP co-op: run {run.Run.Number} goes on at job {run.Run.Legs.Count + 1}/{RemixRun.Jobs} ({run.Run.Phase}), crew {string.Join(", ", run.Crew.Select(c => c.Name))}");
        SaveCoopRun(run);
        return o with { Roguetrip = run, Lobby = true, HostMigration = false, Name = _opt.GameName is { Length: > 0 } gn && gn != I76.Game.Shell.RemixScenarios.Roguetrip ? gn : $"ROGUETRIP co-op {run.Run.Number}" };
    }

    private static void SaveCoopRun(CoopRun r)
    {
        try { r.Save(I76.Game.Shell.RemixScenarios.Dir); }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { GD.PrintErr($"roguetrip: the co-op run not saved: {ex.Message}"); }
    }

    /// <summary>The listen server's run kept as it changes (and the run line logged for a reader of the log).</summary>
    private void WatchRoguetrip(NetMeleeServer server)
    {
        if (server.Roguetrip is null) return;
        server.RoguetripChanged += SaveCoopRun;
    }

    /// <summary>
    /// A co-op roguetrip's job, on this game, before the accept is read: built here from the host's numbers (a listen host's own server
    /// built it into this folder already), its hash checked against the host's. Null when it is here and the same, else why not.
    /// </summary>
    private async System.Threading.Tasks.Task<string?> RoguetripJobHere(JoinAccept acc)
    {
        if (acc.Coop is null || !acc.Arena.Equals(CoopJob.Stem, System.StringComparison.OrdinalIgnoreCase)) return null;
        var state = _net!.Client.Roguetrip;
        if (state?.Job is not { } job) return "The host did not say which ROGUETRIP job it plays (its build is older than this one)";
        if (job.Builder != RemixBuilder.Version) return CoopJob.BuilderMismatch(job.Builder, RemixBuilder.Version);
        GameInstall.UserMissionDir = I76.Game.Shell.RemixScenarios.Dir;
        if (_net.Server is not null) { GD.Print($"remix: ROGUETRIP co-op: {job} (built by this game's server)"); return null; }
        string install = _opt.Install!, dir = I76.Game.Shell.RemixScenarios.Dir;
        byte[] mine;
        try
        {
            mine = await System.Threading.Tasks.Task.Run(() =>
            {
                var built = job.Build(new GameInstall(install));
                built.WriteTo(dir);
                return CoopJob.Hash(built);
            });
        }
        catch (System.Exception ex) when (ex is RemixError or System.IO.IOException or System.UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            return $"This game could not build the host's ROGUETRIP job: {ex.Message}";
        }
        bool same = state.Verify(mine, BuildId.Current) is null;
        GD.Print($"remix: ROGUETRIP co-op: {job} built here, hash {System.Convert.ToHexString(mine)[..12]}, the host's {(state.Hash.Length > 0 ? System.Convert.ToHexString(state.Hash)[..12] : "none")}: {(same ? "the same" : "DIFFERENT")}");
        return state.Verify(mine, BuildId.Current);
    }

    // ---- the lobby's ROUTE MAP (I76.Game.Shell.CoopLobbyScreen, RoguetripScreen's co-op view) ----

    /// <summary>The route map's view of the host's run for this game: the host's choices go to its server in this process.</summary>
    private I76.Game.Shell.RoguetripCoopView? RoguetripView()
    {
        var c = _net!.Client;
        if (c.Roguetrip is null) return null;
        var server = _net.Server;
        return new I76.Game.Shell.RoguetripCoopView
        {
            State = () => c.Roguetrip,
            Me = () => c.Lobby?.Me?.Name ?? _opt.PlayerName ?? "",
            Host = server is not null,
            Select = i => { if (server?.RoguetripSelect(i) is { } no) GD.Print($"network: roguetrip: {no}"); },
            Continue = () => { if (server?.RoguetripContinue() is { } no) GD.Print($"network: roguetrip: {no}"); },
            NewRun = () => { if (server?.RoguetripNewRun(System.Random.Shared.Next(1, 1_000_000)) is { } no) GD.Print($"network: roguetrip: {no}"); },
        };
    }

    // ---- capture hooks (docs/dev-hooks.md) ----

    /// <summary>I76_ROGUE_PICK=K: a listen host's route map takes offer K by itself once the lobby is up (each choice of the run).</summary>
    private static readonly int RoguePick = int.TryParse(System.Environment.GetEnvironmentVariable("I76_ROGUE_PICK"), out int rp) ? rp : -1;
    /// <summary>I76_ROGUE_CONTINUE=S: a listen host's debrief goes on by itself S seconds after it shows.</summary>
    private static readonly double RogueContinue = double.TryParse(System.Environment.GetEnvironmentVariable("I76_ROGUE_CONTINUE"), System.Globalization.CultureInfo.InvariantCulture, out double rc) ? rc : -1;
    private double _rogueDebriefAt = -1;

    private void RoguetripCaptureLobby(MeleeClient c)
    {
        if (_net?.Server is not { Roguetrip: { } run } server) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (run.Run.Phase == "debrief" && RogueContinue >= 0)
        {
            if (_rogueDebriefAt < 0) _rogueDebriefAt = now;
            if (now - _rogueDebriefAt >= RogueContinue) { _rogueDebriefAt = -1; GD.Print($"network: I76_ROGUE_CONTINUE: {server.RoguetripContinue() ?? "on from the debrief"}"); }
        }
        if (run.Run.Phase == "choose" && RoguePick >= 0 && run.Selected != RoguePick) GD.Print($"network: I76_ROGUE_PICK: {server.RoguetripSelect(RoguePick) ?? $"offer {RoguePick}"}");
        // I76_ROGUE_START=N: the lobby starts each job by itself (forced) once N players are in it and RoguePick (if given) is the pick,
        // 3 s after the choice is up (the route map seen first)
        if (RogueStart > 0 && run.Run.Phase == "choose" && (c.Lobby?.Members.Count ?? 0) >= RogueStart && (RoguePick < 0 || run.Selected == RoguePick) && _rogueStartedJob != run.Run.Legs.Count)
        {
            if (_rogueChooseAt < 0) _rogueChooseAt = now;
            if (now - _rogueChooseAt < 3) return;
            _rogueStartedJob = run.Run.Legs.Count;
            _rogueChooseAt = -1;
            GD.Print($"network: I76_ROGUE_START: job {run.Run.Legs.Count + 1} with {c.Lobby!.Members.Count} players");
            System.Threading.Tasks.Task.Run(() => GD.Print($"network: I76_ROGUE_START: {server.StartLobby(force: true) ?? "started"}"));
        }
    }

    private static readonly int RogueStart = int.TryParse(System.Environment.GetEnvironmentVariable("I76_ROGUE_START"), out int rs) ? rs : 0;
    private static int _rogueStartedJob = -1;
    private double _rogueChooseAt = -1;
}

public partial class Main
{
    /// <summary>
    /// I76_ROGUE_SHOTS="DIR@SECONDS": a co-op roguetrip game saves its window every SECONDS, in the lobby (and its route map or debrief) and in
    /// a job, as DIR/TAG-NNN-WHERE.png (TAG: I76_ROGUE_TAG, else host or guest), without quitting. I76_ROGUE_OPEN=1: each game opens the
    /// route map itself when the choice is up (once a job), so both pictures show it.
    /// </summary>
    private static readonly (string Dir, double Every)? RogueShots = System.Environment.GetEnvironmentVariable("I76_ROGUE_SHOTS") is { Length: > 0 } rs2 && rs2.Split('@') is [var d, var e]
        && double.TryParse(e, System.Globalization.CultureInfo.InvariantCulture, out double ev) ? (d, ev) : null;
    private static int _rogueShotN;
    private static double _rogueShotAt = -1;
    private static int _rogueOpenedFor = -1;

    private void RoguetripShot(string where)
    {
        if (RogueShots is not { } rs || _net?.Client.Roguetrip is null) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (_rogueShotAt >= 0 && now - _rogueShotAt < rs.Every) return;
        _rogueShotAt = now;
        string tag = System.Environment.GetEnvironmentVariable("I76_ROGUE_TAG") ?? (_net.Server is null ? "guest" : "host");
        string path = System.IO.Path.Combine(rs.Dir, $"{tag}-{_rogueShotN++:000}-{where}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"screenshot: {path}");
    }

    /// <summary>Each lobby frame of a co-op roguetrip: its pictures, and the route map opened by the capture hook.</summary>
    private void RoguetripLobbyFrame(MeleeClient c)
    {
        if (c.Roguetrip is not { } st) return;
        if (System.Environment.GetEnvironmentVariable("I76_ROGUE_OPEN") == "1" && st.Phase == "choose" && _rogueOpenedFor != st.Legs.Count && _lobbyScreen is { } ls)
        {
            _rogueOpenedFor = st.Legs.Count;
            ls.OpenRoute();
        }
        RoguetripShot($"lobby-job{st.Legs.Count + (st.Phase == "choose" ? 1 : 0)}-{st.Phase}");
    }
}

public partial class Main
{
    private I76.Game.Shell.CoopSalvage? _coopSalvage;

    /// <summary>The salvage draft's screens' view for this player (one per draft: a new one after the last closed), or null outside a draft.</summary>
    private I76.Game.Shell.CoopSalvage? RoguetripSalvageFlow(MeleeClient c)
    {
        if (c.Salvage is null) { _coopSalvage = null; return null; }
        return _coopSalvage ??= new I76.Game.Shell.CoopSalvage
        {
            State = () => c.Salvage,
            Me = () => c.Lobby?.Me?.Name ?? _opt.PlayerName ?? "",
            Car = () => c.Lobby?.Me?.Car is { } car && car != Protocol.MissionCar ? car : c.Lobby?.OwnCar ?? "",
            Send = (k, id) => { GD.Print($"network: roguetrip salvage: {k} {id}"); c.SendPick(k, id); },
            SendGarage = json => { GD.Print($"network: roguetrip: the garage done ({json.Length} bytes)"); c.SendGarage(json); },
        };
    }
}

public partial class Main
{
    /// <summary>
    /// I76_ROGUE_WRECK=T (a capture hook, on a listen host of a co-op roguetrip): at server tick T of each job, up to four of the job's
    /// cars die the damage model's death (their wrecks feed the salvage, as the crew's kills would).
    /// </summary>
    private static readonly uint RogueWreck = uint.TryParse(System.Environment.GetEnvironmentVariable("I76_ROGUE_WRECK"), out uint rw) ? rw : 0;
    private object? _rogueWrecked;

    private void RoguetripWreck()
    {
        if (RogueWreck == 0 || _net?.Server is not { Roguetrip: { Run.Phase: "playing" } } server) return;
        server.Invoke(h =>
        {
            if (ReferenceEquals(_rogueWrecked, h) || h.Tick < RogueWreck) return 0;
            _rogueWrecked = h;
            var cars = h.Setup.Entities.Where(e => e.Vehicle is { } v && !h.Setup.World.IsHuman(v) && !v.IsHeli && !v.IsEmplacement && !I76.Sim.Damage.DamageModel.IsDead(v)).Take(4).ToList();
            foreach (var e in cars) h.Setup.Damage.Die(e.Vehicle!, true);
            GD.Print($"network: I76_ROGUE_WRECK: {cars.Count} cars wrecked at tick {h.Tick}");
            return cars.Count;
        });
    }
}
