using LiteNetLib;

namespace I76.Net;

/// <summary>A co-op roguetrip on the client (docs/remix-missions.md "Roguetrip in co-op"): the host's run as it last said it, its salvage and the player's moves.</summary>
public sealed partial class MeleeClient
{
    /// <summary>The host's co-op roguetrip as it last sent it (<see cref="MsgType.RoguetripState"/>), kept through the rounds; null in any other game.</summary>
    public RoguetripState? Roguetrip { get; private set; }

    /// <summary>A new state from the host: how many have come (the screens redraw on a change).</summary>
    public int RoguetripChanges { get; private set; }

    /// <summary>The salvage draft and garage step as the host last sent it (<see cref="MsgType.RoguetripSalvage"/>), with this player's garage; null outside one.</summary>
    public RoguetripSalvage? Salvage { get; private set; }
    public int SalvageChanges { get; private set; }

    private bool ReceiveRoguetrip(MsgType type, ref NetReader r)
    {
        if ((Features & PeerFeatures.Roguetrip) == 0) return false;
        switch (type)
        {
            case MsgType.RoguetripState:
                Roguetrip = RoguetripState.Read(ref r);
                if (Roguetrip.Phase != "salvage") Salvage = null;
                RoguetripChanges++;
                return true;
            case MsgType.RoguetripSalvage:
                Salvage = RoguetripSalvage.Read(ref r);
                SalvageChanges++;
                return true;
        }
        return false;
    }

    /// <summary>A salvage move to the host (in its lobby, or seated).</summary>
    public void SendPick(PickKind kind, int id = 0)
    {
        if (_peer is null) return;
        _w.Clear();
        RoguetripMoves.Pick(_w, kind, id);
        _peer.Send(_w.Span, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>The garage done: this player's inventory to the host.</summary>
    public void SendGarage(string inventory)
    {
        if (_peer is null) return;
        _w.Clear();
        RoguetripMoves.Garage(_w, inventory);
        _peer.Send(_w.Span, DeliveryMethod.ReliableOrdered);
    }
}
