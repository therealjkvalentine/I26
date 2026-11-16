namespace I76.Net;

/// <summary>What comes after a co-op mission (<see cref="CoopPlan"/>).</summary>
public enum CoopPlanKind : byte
{
    /// <summary>The next mission of the trip, with the same players, cars and settings.</summary>
    Next = 0,
    /// <summary>The same mission again for the whole group.</summary>
    Replay = 1,
    /// <summary>Back to the host's lobby, the players kept with their cars and ready marks.</summary>
    Lobby = 2,
    /// <summary>The host is choosing (after a failure): nothing happens until they do.</summary>
    Host = 3,
}

/// <summary>
/// The server's plan once a co-op mission is over (the fork's, <see cref="MsgType.CoopNext"/>, only to a peer that says
/// <see cref="PeerFeatures.CoopNext"/>; docs/multiplayer.md "On to the next mission"): what follows, the mission it is on (the next one, the
/// same, or the lobby's) and in how many seconds (0 when the host chooses). A new plan replaces the last (the host cancelled or chose). The
/// round itself then comes as a melee's next round does: <see cref="MsgType.NextRound"/>, then the accept, or the lobby.
/// </summary>
public sealed record CoopPlan(CoopPlanKind Kind, string Mission, int Seconds)
{
    public void Write(NetWriter w)
    {
        w.U8((byte)MsgType.CoopNext);
        w.U8((byte)Kind);
        w.Str(Mission);
        w.VarU((uint)Math.Clamp(Seconds, 0, 65535));
    }

    /// <summary>Reads the body (after the type byte).</summary>
    public static CoopPlan Read(ref NetReader r)
    {
        byte k = r.U8();
        if (k > (byte)CoopPlanKind.Host) throw new WireException($"co-op plan {k}");
        return new CoopPlan((CoopPlanKind)k, r.Str(), (int)Math.Min(r.VarU(), 65535));
    }

    /// <summary>The notice's head: the mission's outcome.</summary>
    public static string Head(bool won) => won ? "MISSION COMPLETE" : "MISSION FAILED";

    /// <summary>
    /// The notice's lines under the head ("Next mission: Scene 6: Out of Gas in 9 s"): <paramref name="title"/> names the mission,
    /// <paramref name="left"/> is the seconds left on this client's clock. The host's own line names its keys: <paramref name="now"/> takes everyone
    /// on at once (<see cref="NowKeyGoes"/>), <paramref name="menu"/> (the menu key as bound, Esc by default) opens the menu with the other choices.
    /// Esc is the menu's, never the skip (the owner: "esc to skip to next mission doesn't work that is for the menu").
    /// </summary>
    public IReadOnlyList<string> Lines(string title, double left, bool host, string now = NowKey, string menu = "Esc")
    {
        string when = left >= 1 ? $" in {(int)Math.Ceiling(left)} s" : "";
        return Kind switch
        {
            CoopPlanKind.Next => [$"Next mission: {title}{when}", host ? $"{now}: Next Mission now   {menu}: menu (Replay, Lobby)" : "the same players, cars and settings"],
            CoopPlanKind.Replay => [$"Replaying {title}{when}"],
            CoopPlanKind.Lobby => [$"Back to the lobby{when}", "your car and ready mark are kept"],
            _ => [host ? $"{menu}: menu (Replay Mission or Back to Lobby)" : "Waiting for the host to choose"],
        };
    }

    /// <summary>The key that takes the group on now (a melee's START NOW's: Enter), as the host's line names it.</summary>
    public const string NowKey = "Enter";

    /// <summary>The host's <see cref="NowKey"/> takes everyone on to this plan's mission at once: only a listen host's, and only when the plan is the next mission.</summary>
    public static bool NowKeyGoes(CoopPlan? plan, bool host) => host && plan is { Kind: CoopPlanKind.Next };
}
