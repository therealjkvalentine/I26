namespace I76.Net;

/// <summary>
/// Where a player's game is in a co-op mission's start (the I26 play fork's, <see cref="PeerFeatures.CoopWait"/>; docs/multiplayer.md
/// "Waiting for the others"): what the client says with <see cref="MsgType.CoopStage"/> and the server's wait notice names.
/// <see cref="Unknown"/> is a game that does not say (an upstream build): loading or watching its briefing.
/// </summary>
public enum CoopStage : byte
{
    Unknown = 0,
    /// <summary>The accept is in and the mission is being built.</summary>
    Building = 1,
    /// <summary>The briefing plays.</summary>
    Briefing = 2,
    /// <summary>The player skipped the briefing.</summary>
    Skipped = 3,
    /// <summary>The mission is built and the briefing is over (or there was none): the inputs follow.</summary>
    Ready = 4,
    /// <summary>The last mission's ending (its outro) still plays here; the next mission is built after it.</summary>
    Ending = 5,
}

/// <summary>The stages' words and rules, for the notices and the server.</summary>
public static class CoopStages
{
    /// <summary>A stage off the wire: one this build does not know reads as <see cref="CoopStage.Unknown"/>.</summary>
    public static CoopStage Of(byte b) => b <= (byte)CoopStage.Ending ? (CoopStage)b : CoopStage.Unknown;

    /// <summary>The mission is built in that game (its briefing may still play).</summary>
    public static bool Loaded(CoopStage s) => s is CoopStage.Briefing or CoopStage.Skipped or CoopStage.Ready;

    /// <summary>What a waited-for player is doing, as the notice says it.</summary>
    public static string Doing(CoopStage s) => s switch
    {
        CoopStage.Building => "loading",
        CoopStage.Briefing => "watching the briefing",
        CoopStage.Skipped or CoopStage.Ready => "starting",
        CoopStage.Ending => "watching the ending",
        _ => "loading or watching the briefing",
    };
}

/// <summary>
/// The server's word while a co-op mission's start is held for players still loading (the fork's, <see cref="MsgType.CoopWait"/>, only to a
/// peer that says <see cref="PeerFeatures.CoopWait"/>): how many seconds are left before it starts without them, and who they are with what
/// each is doing. An empty list: the start is no longer held. Sent when the list changes.
/// </summary>
public sealed record CoopWait(int SecondsLeft, IReadOnlyList<(string Name, CoopStage Stage)> Players)
{
    public void Write(NetWriter w)
    {
        w.U8((byte)MsgType.CoopWait);
        w.VarU((uint)Math.Clamp(SecondsLeft, 0, 65535));
        int n = Math.Min(Players.Count, Protocol.MaxCoopSeats);
        w.U8((byte)n);
        for (int i = 0; i < n; i++) { w.Str(Players[i].Name); w.U8((byte)Players[i].Stage); }
    }

    /// <summary>Reads the body (after the type byte).</summary>
    public static CoopWait Read(ref NetReader r)
    {
        int left = (int)Math.Min(r.VarU(), 65535);
        int n = r.U8();
        if (n > Protocol.MaxCoopSeats) throw new WireException($"a wait for {n} players");
        var l = new List<(string, CoopStage)>(n);
        for (int i = 0; i < n; i++) l.Add((r.Str(), CoopStages.Of(r.U8())));
        return new CoopWait(left, l);
    }

    /// <summary>
    /// The notice's lines: "Waiting for 2 players", a line a player ("Ann: watching the briefing"), and how long until the start goes
    /// without them (<paramref name="left"/> seconds, counted down on the client from <see cref="SecondsLeft"/>). Empty when nobody is waited for.
    /// </summary>
    public IReadOnlyList<string> Lines(double left)
    {
        if (Players.Count == 0) return [];
        var lines = new List<string> { $"Waiting for {Players.Count} player{(Players.Count == 1 ? "" : "s")}" };
        foreach (var (name, stage) in Players) lines.Add($"{name}: {CoopStages.Doing(stage)}");
        if (left >= 1) lines.Add($"starting without them in {(int)Math.Ceiling(left)} s");
        return lines;
    }

    /// <summary>The notice when the host does not say who is waited for (an upstream host): the start stands still, nobody named.</summary>
    public static readonly IReadOnlyList<string> Unnamed = ["Waiting for the other players", "loading or watching the briefing"];
}

/// <summary>
/// The start's rules on the server (the fork's; docs/multiplayer.md "Waiting for the others"): who the start waits for. Pure, for the tests.
/// </summary>
public static class CoopStart
{
    /// <summary>A seat as the start sees it: its player's name, whether its game has sent an input (its mission runs), what it says it is doing, and whether it skipped its briefing.</summary>
    public readonly record struct Seat(string Name, bool Input, CoopStage Stage, bool Skipped = false)
    {
        /// <summary>The server's peer of the seat (which game is told what): each game hears whom it waits for, never itself.</summary>
        public int Peer { get; init; } = -1;
    }

    /// <summary>The players the start waits for: those whose game has not sent an input yet, with what each says it is doing.</summary>
    public static List<(string Name, CoopStage Stage)> Waiting(IEnumerable<Seat> seats) =>
        [.. seats.Where(s => !s.Input).Select(s => (s.Name, s.Stage))];

    /// <summary>
    /// Who skipped their briefing, once every seat's game has built the mission (an input sent, or it says <see cref="CoopStages.Loaded"/>)
    /// and somebody skipped (<see cref="PeerFeatures.CoopSkip"/>): the briefings still playing then end for everyone. Null while any game
    /// still loads (a game still loading is never cut short; an upstream game, which says nothing, counts as loaded only once its inputs
    /// come, its briefing played before its build) or when nobody skipped. Any one skip is enough: nobody waits on a cut-scene.
    /// </summary>
    public static string? SkippedBy(IReadOnlyCollection<Seat> seats)
    {
        if (seats.Count < 2 || !seats.All(s => s.Input || CoopStages.Loaded(s.Stage))) return null;
        return seats.FirstOrDefault(s => s.Skipped).Name;
    }
}

/// <summary>The server's word that a briefing ends for everyone (the fork's, <see cref="MsgType.CoopSkip"/>): the player who skipped theirs.</summary>
public sealed record CoopSkip(string Name)
{
    public void Write(NetWriter w) { w.U8((byte)MsgType.CoopSkip); w.Str(Name); }
    public static CoopSkip Read(ref NetReader r) => new(r.Str());

    /// <summary>The line the others see.</summary>
    public string Line => $"{Name} skipped the briefing";
}
