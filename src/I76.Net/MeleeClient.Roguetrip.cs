namespace I76.Net;

/// <summary>A co-op roguetrip on the client (docs/remix-missions.md "Roguetrip in co-op"): the host's run as it last said it.</summary>
public sealed partial class MeleeClient
{
    /// <summary>The host's co-op roguetrip as it last sent it (<see cref="MsgType.RoguetripState"/>), kept through the rounds; null in any other game.</summary>
    public RoguetripState? Roguetrip { get; private set; }

    /// <summary>A new state from the host: how many have come (the screens redraw on a change).</summary>
    public int RoguetripChanges { get; private set; }

    private bool ReceiveRoguetrip(MsgType type, ref NetReader r)
    {
        if (type != MsgType.RoguetripState || (Features & PeerFeatures.Roguetrip) == 0) return false;
        Roguetrip = RoguetripState.Read(ref r);
        RoguetripChanges++;
        return true;
    }
}
