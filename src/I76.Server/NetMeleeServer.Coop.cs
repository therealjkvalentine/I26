using I76.Net;
using LiteNetLib;

namespace I76.Server;

/// <summary>
/// The I26 play fork's co-op flow on the server (docs/multiplayer.md "Waiting for the others"), each part only between this host and a peer
/// that both say its <see cref="PeerFeatures"/> bit; an upstream peer gets none of it and plays as before.
/// </summary>
public sealed partial class NetMeleeServer
{
    /// <summary>What each seated peer's game says it is doing in the co-op start (<see cref="MsgType.CoopStage"/>), by peer id.</summary>
    private readonly Dictionary<int, CoopStage> _coopStage = [];
    /// <summary>The wait list last told (its names and stages), so it goes out only when it changes.</summary>
    private string _waitTold = "";
    /// <summary>The peers whose player skipped their briefing in this mission's start, and those whose briefing the host has ended (<see cref="MsgType.CoopSkip"/>).</summary>
    private readonly HashSet<int> _coopSkipped = [], _skipTold = [];

    /// <summary>This host and the peer both have the feature (the peer's join said it).</summary>
    private bool Both(int peer, PeerFeatures f) => (Options.Features & f) == f && _joined.TryGetValue(peer, out var j) && (j.Features & f) == f;

    /// <summary>A seated peer's message of the fork's co-op flow; false for any other.</summary>
    private bool ReceiveCoop(NetPeer peer, Seat s, MsgType type, ref NetReader r)
    {
        switch (type)
        {
            case MsgType.CoopStage when Both(peer.Id, PeerFeatures.CoopWait):
            {
                var stage = CoopStages.Of(r.U8());
                if (_coopStage.GetValueOrDefault(peer.Id) != stage) Log?.Invoke($"co-op: {s.Name} is {CoopStages.Doing(stage)}");
                _coopStage[peer.Id] = stage;
                if (stage == CoopStage.Skipped) _coopSkipped.Add(peer.Id);
                return true;
            }
        }
        return false;
    }

    /// <summary>A peer gone: what it said goes with it.</summary>
    private void CoopForget(int peer)
    {
        _coopStage.Remove(peer);
        _carriedAt.Remove(peer);
        _coopSkipped.Remove(peer);
        _skipTold.Remove(peer);
    }

    private static int PeerOf(Seat s) => ((NetPeer)s.Connection!).Id;

    /// <summary>
    /// The seats as the start's rules see them. A game without <see cref="PeerFeatures.CoopSkip"/> plays its briefing before it builds the
    /// mission, so its Briefing is not built yet: it reads as Unknown (loading or watching the briefing).
    /// </summary>
    private List<CoopStart.Seat> StartSeats() =>
        [.. _byPeer.Values.OrderBy(s => s.Slot).Select(s =>
        {
            int id = PeerOf(s);
            var stage = _coopStage.GetValueOrDefault(id);
            if (stage == CoopStage.Briefing && !Both(id, PeerFeatures.CoopSkip)) stage = CoopStage.Unknown;
            return new CoopStart.Seat(s.Name, s.Inputs.LastReceived > 0, stage, _coopSkipped.Contains(id));
        })];

    /// <summary>
    /// Each tick of a lobby's co-op mission: before it starts, whom the start waits for, told to the peers that listen whenever it changes
    /// (an empty list once it has started); and once every game has built the mission and somebody skipped the briefing, the briefings
    /// still playing are ended (<see cref="CoopStart.SkippedBy"/>).
    /// </summary>
    private void CoopStartTick()
    {
        if (Host.Mode is not CoopMode cm || !Options.Lobby || _lobbyOpen || _byPeer.Count == 0) return;
        var seats = StartSeats();
        CoopSkipTick(seats);
        if ((Options.Features & PeerFeatures.CoopWait) == 0) return;
        List<(string Name, CoopStage Stage)> waiting = cm.Started ? [] : CoopStart.Waiting(seats);
        string told = string.Join("\n", waiting.Select(w => $"{w.Name}\t{(byte)w.Stage}"));
        if (told == _waitTold) return;
        _waitTold = told;
        var msg = new CoopWait((int)Math.Ceiling(cm.LoadLeft ?? 0), waiting);
        foreach (var (id, s) in _byPeer)
        {
            if (!Both(id, PeerFeatures.CoopWait)) continue;
            _msg.Clear();
            msg.Write(_msg);
            ((NetPeer)s.Connection!).Send(_msg.Span, DeliveryMethod.ReliableOrdered);
        }
        if (waiting.Count > 0) Log?.Invoke($"co-op: the start waits for {string.Join(", ", waiting.Select(w => $"{w.Name} ({CoopStages.Doing(w.Stage)})"))}");
    }

    /// <summary>Somebody skipped and every game has built the mission: each briefing still playing (its game says so) is ended, once, with the skipper's name.</summary>
    private void CoopSkipTick(List<CoopStart.Seat> seats)
    {
        if ((Options.Features & PeerFeatures.CoopSkip) == 0 || CoopStart.SkippedBy(seats) is not { } by) return;
        foreach (var (id, s) in _byPeer)
        {
            if (!Both(id, PeerFeatures.CoopSkip) || _coopStage.GetValueOrDefault(id) != CoopStage.Briefing || !_skipTold.Add(id)) continue;
            _msg.Clear();
            new CoopSkip(by).Write(_msg);
            ((NetPeer)s.Connection!).Send(_msg.Span, DeliveryMethod.ReliableOrdered);
            Log?.Invoke($"co-op: {s.Name}'s briefing ends: everyone has loaded and {by} skipped theirs");
        }
    }
}
