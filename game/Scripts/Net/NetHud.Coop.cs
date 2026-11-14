using Godot;
using I76.Net;

namespace I76.Game.Net;

/// <summary>
/// The fork's co-op notices over the network HUD (docs/multiplayer.md "Waiting for the others"): while a co-op mission's start is held,
/// whom it waits for and what each is doing, from the host's word (<see cref="MeleeClient.StartWait"/>); from a host that does not say
/// (an upstream host), an unnamed line once the start has stood still a moment.
/// </summary>
public partial class NetHud
{
    /// <summary>The server's tick when this game first drew (the start's), and whether the mission has run since.</summary>
    private uint? _startTick;
    private bool _started;
    /// <summary>How long this game has been drawn while the start stood still.</summary>
    private double _stillFor;

    public const string StandBy = "PLEASE STAND BY";

    /// <summary>The co-op mission's outcome once the server has said it (won or failed), for the notice of what follows.</summary>
    private bool? _coopWon;
    /// <summary>A co-op mission's title by its stem ("Scene 6: Out of Gas"), for the notice of what follows.</summary>
    public Func<string, string>? MissionTitle { get; init; }

    /// <summary>The co-op notice for this frame, or null: what follows the mission once the host has a plan (the fork's), else the start's wait.</summary>
    private NetHudFrame.NoticeInfo? CoopNotice(double delta)
    {
        if (Coop && _coopWon is { } won && Client.Has(PeerFeatures.CoopNext) && Client.Plan is { } plan)
            return new NetHudFrame.NoticeInfo(CoopPlan.Head(won),
                plan.Lines(MissionTitle?.Invoke(plan.Mission) ?? plan.Mission.ToUpperInvariant(), plan.Seconds - (Client.Now - Client.PlanAt), Server is not null));
        if (!Coop || Client.State != ClientState.Joined || _started) return null;
        // the start is over once the server's tick moves on from where this game found it (a movie's stand-still is not the start's)
        _startTick ??= Client.NewestTick;
        if (Client.NewestTick > _startTick.Value + 1) { _started = true; _stillFor = 0; return null; }
        if (Client.MovieName is not null) return null;
        _stillFor += delta;
        if (Client.StartWait is { } w)
        {
            if (w.Players.Count == 0) { _started = true; return null; }
            var lines = w.Lines(w.SecondsLeft - (Client.Now - Client.StartWaitAt));
            return new NetHudFrame.NoticeInfo(StandBy, lines);
        }
        // a host that does not say whom it waits for: the start has stood still a moment, so say it is held
        if (!Client.Has(PeerFeatures.CoopWait) && _stillFor > 2) return new NetHudFrame.NoticeInfo(StandBy, CoopWait.Unnamed);
        return null;
    }
}
