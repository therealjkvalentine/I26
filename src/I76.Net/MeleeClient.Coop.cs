using LiteNetLib;

namespace I76.Net;

/// <summary>
/// The I26 play fork's co-op flow on the client (docs/multiplayer.md "Waiting for the others"), each part only with a host that says its
/// <see cref="PeerFeatures"/> bit: the game's stage in the start told to the server, and the server's word of whom the start waits for.
/// </summary>
public sealed partial class MeleeClient
{
    /// <summary>Whom the co-op start waits for, as the host last said (<see cref="MsgType.CoopWait"/>; null before it said, or with a host that does not); an empty list once it started.</summary>
    public CoopWait? StartWait { get; private set; }
    /// <summary>When <see cref="StartWait"/> came, on this client's clock (its seconds count down from then).</summary>
    public double StartWaitAt { get; private set; }

    /// <summary>Where this game is in the co-op start (<see cref="SendCoopStage"/>), told again to a host met afresh (a new accept).</summary>
    public CoopStage CoopStageNow { get; private set; }

    /// <summary>This game's stage in the co-op start: sent to a host that says <see cref="PeerFeatures.CoopWait"/>, kept for the next accept otherwise.</summary>
    public void SendCoopStage(CoopStage stage)
    {
        CoopStageNow = stage;
        if (_peer is null || State != ClientState.Joined || !Has(PeerFeatures.CoopWait)) return;
        _w.Clear();
        _w.U8((byte)MsgType.CoopStage);
        _w.U8((byte)stage);
        _peer.Send(_w.Span, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>The host's word that this game's briefing ends: everyone has built the mission and this player skipped theirs (<see cref="MsgType.CoopSkip"/>); the game takes it.</summary>
    public CoopSkip? SkippedBy { get; set; }

    /// <summary>What follows the co-op mission, as the host last planned it (<see cref="MsgType.CoopNext"/>), and when that came on this client's clock; null until the end.</summary>
    public CoopPlan? Plan { get; private set; }
    public double PlanAt { get; private set; }

    /// <summary>A new accept (a start, the next round): the last round's word goes, and the stage is told to this host if it listens.</summary>
    private void CoopAccepted()
    {
        StartWait = null;
        SkippedBy = null;
        Plan = null;
        if (CoopStageNow != CoopStage.Unknown) SendCoopStage(CoopStageNow);
    }

    /// <summary>The next round is coming: the last round's word goes.</summary>
    private void CoopNewRound() => StartWait = null;   // (the plan stays: the game that carries on reads it after the reload)

    /// <summary>The fork's co-op messages from a host that says their bit; false for any other message.</summary>
    private bool ReceiveCoop(MsgType type, ref NetReader r)
    {
        switch (type)
        {
            case MsgType.CoopWait when Has(PeerFeatures.CoopWait):
                StartWait = CoopWait.Read(ref r);
                StartWaitAt = Now;
                return true;
            case MsgType.CoopSkip when Has(PeerFeatures.CoopSkip):
                SkippedBy = CoopSkip.Read(ref r);
                return true;
            case MsgType.CoopNext when Has(PeerFeatures.CoopNext):
                Plan = CoopPlan.Read(ref r);
                PlanAt = Now;
                return true;
        }
        return false;
    }
}
