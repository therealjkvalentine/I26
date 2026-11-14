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
                return true;
            }
        }
        return false;
    }

    /// <summary>A peer gone: what it said goes with it.</summary>
    private void CoopForget(int peer) => _coopStage.Remove(peer);

    private static int PeerOf(Seat s) => ((NetPeer)s.Connection!).Id;

    /// <summary>The seats as the start's rules see them.</summary>
    private List<CoopStart.Seat> StartSeats() =>
        [.. _byPeer.Values.OrderBy(s => s.Slot).Select(s => new CoopStart.Seat(s.Name, s.Inputs.LastReceived > 0, _coopStage.GetValueOrDefault(PeerOf(s))))];

    /// <summary>
    /// Each tick of a lobby's co-op mission before it starts: whom the start waits for, told to the peers that listen whenever it changes
    /// (an empty list once it has started).
    /// </summary>
    private void CoopStartTick()
    {
        if (Host.Mode is not CoopMode cm || !Options.Lobby || _lobbyOpen || _byPeer.Count == 0) return;
        if ((Options.Features & PeerFeatures.CoopWait) == 0) return;
        List<(string Name, CoopStage Stage)> waiting = cm.Started ? [] : CoopStart.Waiting(StartSeats());
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
}
