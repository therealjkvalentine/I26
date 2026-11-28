using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Net;
using I76.Sim.Missions;
using I76.Sim.Trip;
using LiteNetLib;

namespace I76.Server;

/// <summary>
/// ROGUETRIP in co-op (the I26 play fork's; docs/remix-missions.md "Roguetrip in co-op"): a lobby game whose missions are the host's run's
/// jobs. The players gather in the lobby; the host picks a job on the route map (<see cref="RoguetripSelect"/>) and starts it, and the job
/// is built then for the players there (<see cref="CoopJob"/>: the enemies scaled to the crew), here and on every player's machine from
/// the same numbers (<see cref="MsgType.RoguetripState"/> goes before each accept; a client checks its build's hash and the mission file's
/// fingerprint against the host's). Each player's car carries its damage from job to job by name; a player killed in a job is out of
/// it (no respawn) and back for the next at a floor; the run ends when the whole crew is killed at once. After each job everyone goes
/// back to the lobby, where the debrief shows, then the next choice.
/// </summary>
public sealed partial class NetMeleeServer
{
    /// <summary>The host's co-op roguetrip, or null in any other game.</summary>
    public CoopRun? Roguetrip => Options.Roguetrip;

    /// <summary>The run changed (a pick, a job's start or end, the debrief passed): the host's game keeps it (coop-run.yaml). Raised under the server's lock.</summary>
    public event Action<CoopRun>? RoguetripChanged;

    /// <summary>The job being played (built for the crew at its start) and the hash of this host's build of it.</summary>
    private CoopJob? _rtJob;
    private byte[] _rtHash = [];
    /// <summary>The description's failure count of the job being played (the lines after them are its death lines).</summary>
    private int _rtFailures;
    /// <summary>The offers on the route map now (made once a run state).</summary>
    private List<RunOffer>? _rtOffers;

    /// <summary>
    /// A roguetrip's game before it is built: the job of the offer the host has highlighted (or, mid-job after a restart, the one in play)
    /// is built for one player as the lobby's mission, so the lobby has a mission to stand on; the start builds it again for the crew.
    /// </summary>
    public static ServerOptions PrepareRoguetrip(ServerOptions o)
    {
        if (o.Roguetrip is not { } run) return o;
        // a run kept at its debrief (the game closed on it) goes on to its next choice
        if (run.Run.Phase == "debrief") run.Debriefed();
        var offers = run.Offers();
        var job = run.Playing ?? (offers.Count > 0 ? run.Job(offers[Math.Clamp(run.Selected, 0, offers.Count - 1)], 1) : null);
        if (job is not null) Generate(o.Install, job);
        else if (GameInstall.InUserMissionDir(CoopJob.Stem + ".msn") is null) throw new InvalidOperationException("the co-op roguetrip has no job to stand on (its run is over)");
        return o with { Arena = CoopJob.Stem, Coop = CoopJob.Stem, HostMigration = false };
    }

    /// <summary>Builds a job into the generated missions' folder (<see cref="GameInstall.UserMissionDir"/>); returns its hash.</summary>
    public static byte[] Generate(string install, CoopJob job)
    {
        if (GameInstall.UserMissionDir is not { Length: > 0 } dir) throw new InvalidOperationException("no folder for generated missions");
        var built = job.Build(new GameInstall(install));
        built.WriteTo(dir);
        return CoopJob.Hash(built);
    }

    /// <summary>The run as the players are told it now.</summary>
    private RoguetripState? RoguetripNow()
    {
        if (Roguetrip is not { } run) return null;
        _rtOffers ??= run.Offers();
        return RoguetripState.Of(run, BuildId.Current, _rtOffers, run.Run.Phase == "playing" ? _rtJob : null, run.Run.Phase == "playing" ? _rtHash : []);
    }

    /// <summary>The run's state to one peer (only one whose join says it reads it).</summary>
    private void SendRoguetrip(NetPeer peer, RoguetripState state)
    {
        if (!_joined.TryGetValue(peer.Id, out var j) && _lobby.FirstOrDefault(m => m.Peer.Id == peer.Id) is { } lm) j = lm.Request;
        if (j is null || (j.Features & PeerFeatures.Roguetrip) == 0) return;
        _msg.Clear();
        state.Write(_msg);
        peer.Send(_msg.Span, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>The run's state to everyone (the lobby and the seats), and to the host's game to keep.</summary>
    private void TellRoguetrip()
    {
        if (RoguetripNow() is not { } st) return;
        foreach (var m in _lobby) SendRoguetrip(m.Peer, st);
        foreach (var (_, s) in _byPeer) SendRoguetrip((NetPeer)s.Connection!, st);
        RoguetripChanged?.Invoke(Roguetrip!);
    }

    /// <summary>A joiner whose game cannot build the jobs is turned away (null: it may come in).</summary>
    private string? RoguetripRefuses(JoinRequest j) => Roguetrip is not null && (j.Features & PeerFeatures.Roguetrip) == 0 ? Protocol.RoguetripNeeded : null;

    // ---- the host's choices (a listen host's route map and debrief; in process) ----

    /// <summary>The host highlights an offer on the route map (everyone's map follows). Null when done, else why not.</summary>
    public string? RoguetripSelect(int offer)
    {
        lock (_lock)
        {
            if (Roguetrip is not { } run) return "not a roguetrip";
            if (run.Run.Phase != "choose") return "no choice now";
            _rtOffers ??= run.Offers();
            if (offer < 0 || offer >= _rtOffers.Count) return $"offer {offer} of {_rtOffers.Count}";
            if (run.Selected == offer) return null;
            run.Selected = offer;
            Log?.Invoke($"roguetrip: the host picks {_rtOffers[offer].Name}, {RemixRun.JobWord(_rtOffers[offer].Template)} ({_rtOffers[offer].Challenge:0.0})");
            TellRoguetrip();
            return null;
        }
    }

    /// <summary>Past the debrief (the host's CONTINUE): the next choice, or the run's end. Null when done, else why not.</summary>
    public string? RoguetripContinue()
    {
        lock (_lock)
        {
            if (Roguetrip is not { } run) return "not a roguetrip";
            if (run.Run.Phase != "debrief") return "no debrief now";
            run.Debriefed();
            _rtOffers = null;
            Log?.Invoke($"roguetrip: on from the debrief: {run.Run.Phase}");
            TellRoguetrip();
            return null;
        }
    }

    /// <summary>A run over (the host's DONE): a new run with this number for whoever is in the lobby. Null when done, else why not.</summary>
    public string? RoguetripNewRun(int number)
    {
        lock (_lock)
        {
            if (Roguetrip is not { } run) return "not a roguetrip";
            if (!run.Run.Over) return "the run is not over";
            var fresh = CoopRun.Start(number);
            run.Run = fresh.Run;
            run.Crew.Clear();
            run.LastObjectives.Clear();
            run.Selected = 0;
            run.Players = 0;
            _rtOffers = null;
            Log?.Invoke($"roguetrip: a new run, {number}");
            TellRoguetrip();
            return null;
        }
    }

    // ---- a job's start and end ----

    /// <summary>
    /// The lobby's start of a roguetrip's job: the highlighted offer taken, built for the players in the lobby (here; each client builds it
    /// from the state it is sent before its accept), and the game rebuilt on it. Null when it may start, else why not (the lobby stays).
    /// </summary>
    private string? RoguetripStarting()
    {
        if (Roguetrip is not { } run) return null;
        if (run.Run.Phase == "debrief") { run.Debriefed(); _rtOffers = null; }
        if (run.Run.Over) return "the run is over (DONE on its summary starts a new one)";
        if (run.Run.Phase != "choose") return $"the run is at {run.Run.Phase}";
        _rtOffers ??= run.Offers();
        if (_rtOffers.Count == 0) return "no jobs on the road from here";
        var offer = _rtOffers[Math.Clamp(run.Selected, 0, _rtOffers.Count - 1)];
        int players = Math.Max(1, _lobby.Count);
        // the roster is drawn around the crew's average car (CoopJob.CrewGear), kept on the offer for the job's rebuilds (Playing)
        offer.Gear = CoopJob.CrewGear(_lobby.Select(m => m.Request.Car));
        var job = run.Job(offer, players);
        MeleeHost built;
        byte[] hash;
        try
        {
            hash = Generate(Options.Install, job);
            _rtFailures = job.Spec().Text.Failures.Count;
            built = new MeleeHost(Options with { Arena = CoopJob.Stem, Coop = CoopJob.Stem, Seed = unchecked(Options.Seed * 1103515245 + 12345 + job.Seed) });
        }
        catch (Exception ex) when (ex is RemixError or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            Log?.Invoke($"roguetrip: the job could not be built ({ex.Message})");
            return $"the job could not be built ({ex.Message})";
        }
        run.Run.Take(offer);
        run.Players = players;
        run.Run.Log.Add($"remix: RUN {run.Run.Number:000000} JOB {job.Job}/{RemixRun.Jobs} co-op {job}");
        _rtJob = job;
        _rtHash = hash;
        _rtOffers = null;
        Host.Log -= _hostLog;
        Host = built;
        built.Log += _hostLog;
        Log?.Invoke($"roguetrip: {job} built ({Convert.ToHexString(hash)[..12]}): the job starts");
        return null;
    }

    /// <summary>The crew seated for a job: each player's car takes what it carried (the same car file; a new car or a new player is as built).</summary>
    private void RoguetripSeated(IEnumerable<Seat> seats)
    {
        if (Roguetrip is not { } run || run.Run.Phase != "playing") return;
        foreach (var s in seats)
            if (s.Actor is CoopActor a && run.CarryFor(s.Name, a.CarFile) is { } carry)
            {
                RunCarry.Apply(carry, a.Car);
                Log?.Invoke($"roguetrip: {s.Name}'s {a.CarFile} carries armour {carry.ArmourLeft}%, chassis {carry.ChassisLeft}%");
            }
        TellRoguetrip();
    }

    /// <summary>
    /// The job is over (won, failed, or the host took everyone back to the lobby mid-job: <paramref name="won"/> null): the outcome
    /// (RemixRun.Classify: a death of the whole crew ends the run, any other failure is a bust), the kills, each player's car, the
    /// objectives as they stood; the run kept, and everyone back to the lobby after the hold, where the debrief shows.
    /// </summary>
    private void RoguetripEnded(CoopMode cm, bool? won)
    {
        if (Roguetrip is not { } run || run.Run.Phase != "playing") return;
        var script = cm.Setup.Script!;
        var seats = Host.Seats.Where(s => s.Actor is CoopActor).ToList();
        bool allDead = seats.Count > 0 && seats.All(s => s.Actor.Destroyed);
        int fail = won is null ? -1 : script.Host.End?.FailMessage ?? -1;
        var outcome = won is null ? RemixRun.Outcome.Bust : RemixRun.Classify(won.Value, fail, _rtFailures, allDead);
        int kills = script.Host.Entities.Count(e => e.Dead && e.KilledByPlayer);
        var text = cm.Setup.ObjectiveText;
        string why = won == true ? "" : won is null ? "The crew walked away from the job."
            : text is { } t && fail >= 0 && fail < t.Failures.Count ? t.Failures[fail] : outcome == RemixRun.Outcome.Dead ? "The whole crew was killed." : "The job went wrong.";
        var flags = script.Host.Objectives.Flags;
        var objectives = new List<CoopObjective>();
        for (int i = 0; i < flags.Length && text is not null && i < text.Objectives.Count; i++)
            if ((flags[i] & Objectives.Hidden) == 0)
                objectives.Add(new CoopObjective { Text = text.Objectives[i].Text, Done = (flags[i] & Objectives.Succeeded) != 0, Failed = (flags[i] & Objectives.Failed) != 0 });
        run.Finish(outcome, kills, seats.Select(s => (s.Name, ((CoopActor)s.Actor).CarFile, (RunCar?)RunCarry.Capture(s.Actor.Car), s.Actor.Destroyed)), why, objectives);
        Log?.Invoke($"roguetrip: job {run.Run.Legs.Count} {outcome.ToString().ToLowerInvariant()} ({(why.Length > 0 ? why : "done")}), {kills} kills; "
            + string.Join(", ", run.Crew.Select(c => $"{c.Name} {(c.Out ? "killed" : $"armour {c.State?.ArmourLeft ?? 100}%")}")));
        _rtJob = null;
        _rtHash = [];
        _rtOffers = null;
        TellRoguetrip();
    }
}
