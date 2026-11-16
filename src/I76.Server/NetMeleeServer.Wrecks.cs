using I76.Net;
using LiteNetLib;

namespace I76.Server;

/// <summary>
/// The port's wreck rule in a network game (docs/multiplayer.md "Wrecks stay solid"): the host's choice, in force only while
/// every player in the game has it, so that the server's contacts and each client's prediction of its own car agree. A player
/// on upstream's build or an earlier fork build turns it off for as long as they play; the others are told each change.
/// </summary>
public sealed partial class NetMeleeServer
{
    /// <summary>What each peer with the rule was last told, and the host it was told of (a new mission's host tells again after its accepts).</summary>
    private readonly Dictionary<int, bool> _wreckTold = [];
    private MeleeHost? _wreckHost;

    private void WreckRuleTick()
    {
        if (!ReferenceEquals(_wreckHost, Host)) { _wreckHost = Host; _wreckTold.Clear(); }
        bool on = Host.WrecksSolidAsked;
        if (on)
            foreach (int id in _byPeer.Keys)
                if (!Both(id, PeerFeatures.WrecksSolid)) { on = false; break; }
        var world = Host.Setup.World;
        if (world.WrecksStaySolid != on)
        {
            world.WrecksStaySolid = on;
            Log?.Invoke(on ? "wrecks stay solid: every player has the rule" : "wrecks are the original's: a player without the rule is in the game");
        }
        if (!Host.WrecksSolidAsked && _wreckTold.Count == 0) return;
        foreach (var (id, s) in _byPeer)
        {
            if (!Both(id, PeerFeatures.WrecksSolid) || _wreckTold.TryGetValue(id, out bool told) && told == on) continue;
            _wreckTold[id] = on;
            _msg.Clear();
            _msg.U8((byte)MsgType.WreckRule);
            _msg.Bool(on);
            ((NetPeer)s.Connection!).Send(_msg.Span, DeliveryMethod.ReliableOrdered);
        }
    }
}
