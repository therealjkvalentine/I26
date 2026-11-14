using I76.Net;
using LiteNetLib;

namespace I76.Server;

/// <summary>
/// A co-op game that goes on after its mission (the I26 play fork's, <see cref="PeerFeatures.CoopNext"/>; docs/multiplayer.md "On to the
/// next mission"): once a lobby's co-op mission is won the server plans the trip's next mission with the same players, cars and settings
/// (after the last, back to the lobby), tells the peers that listen (<see cref="MsgType.CoopNext"/>), and after a hold to read the outcome
/// carries everyone on as a melee carries its players into a next round: <see cref="MsgType.NextRound"/>, then every seat dealt at once and
/// the accepts (or the lobby again, everyone kept with their cars and ready marks). The host can choose sooner or otherwise
/// (<see cref="CoopChoose"/>). A peer without the feature is not taken into a next mission: it is told why and let go (at a won mission's
/// end its game goes back to its menus anyway, as before).
/// </summary>
public sealed partial class NetMeleeServer
{
    /// <summary>The word to a peer that cannot follow into the next mission (an upstream build).</summary>
    public const string CoopLeftBehindText = "The host went on to the next mission (your build cannot follow: join again).";

    private CoopPlan? _plan;
    /// <summary>When the plan is carried out, on the server's clock (infinity: when the host chooses).</summary>
    private double _planDue;
    /// <summary>This mission's end has been planned for.</summary>
    private bool _planned;

    /// <summary>What follows the co-op mission now (null while it plays, or when nothing does).</summary>
    public CoopPlan? CoopPlanNow { get { lock (_lock) return _plan; } }

    /// <summary>A co-op game is between missions and goes on: a console server stays for it.</summary>
    public bool CoopGoesOn { get { lock (_lock) return _plan is not null; } }

    /// <summary>
    /// The host's choice for the whole group (a listen host's menu; the console's), in the mission or at its end: <see cref="CoopPlanKind.Next"/>
    /// now (after a win, the plan's next mission), <see cref="CoopPlanKind.Replay"/> (this mission again from its start) or
    /// <see cref="CoopPlanKind.Lobby"/> (back to the lobby with everyone). Null when done, else why not.
    /// </summary>
    public string? CoopChoose(CoopPlanKind kind)
    {
        lock (_lock)
        {
            if (Host.Mode is not CoopMode cm || (Options.Features & PeerFeatures.CoopNext) == 0 || !Options.Lobby) return "not a co-op game that goes on";
            if (_lobbyOpen) return "the game is in its lobby";
            string mission = Options.Coop ?? Options.Arena;
            bool won = cm.Result == true;
            switch (kind)
            {
                case CoopPlanKind.Next when _plan is { Kind: CoopPlanKind.Next } p:
                    CarryOn(CoopPlanKind.Next, p.Mission, won);
                    return null;
                case CoopPlanKind.Next:
                    return "there is no next mission";
                case CoopPlanKind.Replay or CoopPlanKind.Lobby:
                    Log?.Invoke($"co-op: the host chooses {(kind == CoopPlanKind.Replay ? "to replay the mission" : "the lobby")}{(cm.Result is null ? " in the mission" : "")}");
                    CarryOn(kind, mission, won);
                    return null;
                default:
                    return $"{kind} is not a choice";
            }
        }
    }

    /// <summary>Each tick: a co-op mission that has ended gets its plan once, and the plan is carried out when it is due.</summary>
    private void CoopEndTick()
    {
        if (Host.Mode is not CoopMode cm || !Options.Lobby || (Options.Features & PeerFeatures.CoopNext) == 0 || cm.Result is not { } won) return;
        if (!_planned)
        {
            _planned = true;
            PlanFor(cm, won);
        }
        if (_plan is { Kind: not CoopPlanKind.Host } p && Now >= _planDue) CarryOn(p.Kind, p.Mission, won);
    }

    /// <summary>
    /// A won mission goes on to the trip's next (back to the lobby after the last); a failed one waits for the listen host's choice (Replay
    /// Mission or Back to Lobby), and on a dedicated server, which has no host, goes back to the lobby after the hold.
    /// </summary>
    private void PlanFor(CoopMode cm, bool won)
    {
        string mission = Options.Coop ?? Options.Arena;
        int hold = (int)Math.Ceiling(Options.CoopNextSeconds);
        if (!won)
        {
            SetPlan(Options.ListenHost ? new CoopPlan(CoopPlanKind.Host, mission, 0) : new CoopPlan(CoopPlanKind.Lobby, mission, hold));
            // an upstream game on its end menu waits as long as the game over lets it (its own Leave Match stays)
            cm.NextRoundSeconds = (byte)(Options.ListenHost ? 255 : Math.Clamp(hold, 1, 255));
            return;
        }
        var next = I76.Formats.Mission.MissionTitles.NextCoop(new I76.Formats.Vfs.GameInstall(Options.Install), mission);
        SetPlan(next is not null ? new CoopPlan(CoopPlanKind.Next, next, hold) : new CoopPlan(CoopPlanKind.Lobby, mission, hold));
        // the game over says the server carries on: an upstream game waits for it rather than leaving at once
        cm.NextRoundSeconds = (byte)Math.Clamp(hold, 1, 255);
    }

    /// <summary>The plan is set and told to every peer that listens.</summary>
    private void SetPlan(CoopPlan p)
    {
        _plan = p;
        _planDue = p.Kind == CoopPlanKind.Host ? double.PositiveInfinity : Now + p.Seconds;
        Log?.Invoke($"co-op: {p.Kind switch { CoopPlanKind.Next => $"the next mission is {p.Mission}", CoopPlanKind.Replay => $"{p.Mission} again", CoopPlanKind.Lobby => "back to the lobby", _ => "the host chooses" }}{(p.Kind == CoopPlanKind.Host ? "" : $" in {p.Seconds} s")}");
        foreach (var (id, s) in _byPeer)
        {
            if (!Both(id, PeerFeatures.CoopNext)) continue;
            _msg.Clear();
            p.Write(_msg);
            ((NetPeer)s.Connection!).Send(_msg.Span, DeliveryMethod.ReliableOrdered);
        }
    }

    /// <summary>
    /// The players go on: <paramref name="mission"/> is built as a new game with this game's options, and the players still connected are told
    /// (<see cref="MsgType.NextRound"/>) and either seated in it at once, every seat dealt before any accept as a lobby's start deals them (the
    /// host first, in seat 0), each in the car it played; or, for <see cref="CoopPlanKind.Lobby"/>, gathered in the lobby again, ready. A peer
    /// without <see cref="PeerFeatures.CoopNext"/> follows where upstream's client can (its game reads a next round and its accept or lobby as
    /// a join): into the lobby, and into a replay of a mission it has not won (a won mission's end has taken its game back to its menus).
    /// </summary>
    private void CarryOn(CoopPlanKind kind, string mission, bool won)
    {
        _plan = null;
        var old = Host;
        var o = old.Options with { Arena = mission, Coop = mission, Seed = unchecked(old.Options.Seed * 1103515245 + 12345) };
        MeleeHost next;
        try { next = new MeleeHost(o); }
        catch (Exception ex)
        {
            Log?.Invoke($"co-op: {mission} could not be built ({ex.Message}): the game ends here");
            return;
        }
        _planned = false;
        var carried = _byPeer.OrderBy(kv => kv.Key == _hostPeer ? 0 : 1).ThenBy(kv => kv.Value.Slot).Select(kv => ((NetPeer)kv.Value.Connection!, kv.Value)).ToList();
        old.Log -= _hostLog;
        Host = next;
        next.Log += _hostLog;
        Round++;
        OverAt = null;
        _sinceScores = 0;
        _byPeer.Clear();
        _waitTold = "";
        _coopSkipped.Clear();
        _skipTold.Clear();
        Log?.Invoke($"co-op: {kind switch { CoopPlanKind.Lobby => "back to the lobby of", CoopPlanKind.Replay => "again:", _ => "on to" }} {mission}, {carried.Count} players carry on");
        var seating = new List<(NetPeer Peer, JoinRequest Request)>();
        foreach (var (peer, s) in carried)
        {
            var j = (_joined.GetValueOrDefault(peer.Id) ?? new JoinRequest(Protocol.Version, s.Name, s.Actor.CarFile, 0, [])) with { Name = s.Name, Team = 0 };
            // a peer that cannot play on with the group (an upstream build) is let go with the reason, rather than seated and gone
            if ((kind == CoopPlanKind.Next || kind == CoopPlanKind.Replay && won) && !Both(peer.Id, PeerFeatures.CoopNext))
            {
                Log?.Invoke($"co-op: {s.Name} cannot follow into {mission} (its build does not go on): let go");
                _joined.Remove(peer.Id);
                Turn(peer, CoopLeftBehindText);
                continue;
            }
            _msg.Clear();
            _msg.U8((byte)MsgType.NextRound);
            _msg.Str(mission);
            peer.Send(_msg.Span, DeliveryMethod.ReliableOrdered);
            seating.Add((peer, j));
        }
        if (kind == CoopPlanKind.Lobby)
        {
            // the lobby again, everyone in it ready with the car they played (they readied once; the host starts when it likes)
            _lobbyOpen = true;
            _lobbyClosed = false;
            byte id = 0;
            foreach (var (peer, j) in seating) _lobby.Add(new LobbyPeer(id++, peer, j) { Ready = true });
            SendLobby();
        }
        else
        {
            var seated = seating.Select(x => (x.Peer, Seat: SeatIn(x.Peer, x.Request))).ToList();
            foreach (var (peer, seat) in seated) SendAccept(peer, seat);
        }
        RoundStarted?.Invoke(next);
    }
}
