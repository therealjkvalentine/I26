using Godot;
using I76.Net;

namespace I76.Game.Shell;

/// <summary>
/// A co-op roguetrip's route map and debrief as one game sees them (docs/remix-missions.md "Roguetrip in co-op"): the host's run as its
/// server last told it, the host's choices sent back (in process: the listen host's server), and the way back to the lobby.
/// </summary>
public sealed class RoguetripCoopView
{
    public required Func<RoguetripState?> State { get; init; }
    /// <summary>This player's name in the game (whose car the sheet shows).</summary>
    public required Func<string> Me { get; init; }
    /// <summary>The host chooses (the highlight, CONTINUE, a new run); everyone else watches.</summary>
    public bool Host { get; init; }
    public Action<int>? Select { get; init; }
    public Action? Continue { get; init; }
    public Action? NewRun { get; init; }
    /// <summary>Back to the lobby (set by the lobby that opened the sheet).</summary>
    public Action Close { get; set; } = () => { };
}

public sealed partial class RoguetripScreen
{
    /// <summary>The co-op view this sheet shows (null: the single player's run).</summary>
    public RoguetripCoopView? CoopView { get; init; }

    private RoguetripState? _coopSeen;

    private string? CoopHelp => CoopView is not { } v ? null
        : v.Host ? "You choose for everyone. Enter: back to the lobby, then START."
        : "The host chooses (the red ring). Esc: back to the lobby.";

    private static string CoopWonNext => "Next: the salvage. Take parts from the wrecks in turns, then fit your car; what nobody takes is scrap plate (30 % at least).";

    /// <summary>The co-op sheet's entry: drawn from the host's run (true), or the single player's (false).</summary>
    private bool CoopEnter()
    {
        if (CoopView is null) return false;
        _sel = 0;
        CoopSync(force: true);
        return true;
    }

    /// <summary>The host's run as last told, when it changed: the run, the offers and the host's pick, the debrief's objectives.</summary>
    private void CoopSync(bool force)
    {
        var st = CoopView!.State();
        if (st is null || (!force && ReferenceEquals(st, _coopSeen))) return;
        _coopSeen = st;
        Run = st.ToRun(CoopView.Me());
        _offers = Mood == "choose" ? [.. st.Offers] : [];
        _sel = Math.Clamp(st.Selected, 0, Math.Max(0, _offers.Count - 1));
        _lastObjectives = [.. st.Objectives.Select(o => (o.Text, o.Done, o.Failed))];
        _jobMaps.Clear();
        Repaint();
    }

    /// <summary>The co-op sheet's frame (true: taken). The host's picks go to the server; a guest only goes back to the lobby.</summary>
    private bool CoopTick()
    {
        if (CoopView is not { } v) return false;
        CoopSync(false);
        // a won job's salvage has begun: back to the lobby, which opens the salvage screens
        if (Run.Phase == "salvage") { v.Close(); return true; }
        foreach (var k in Shell.Keys)
        {
            if (k is Key.Escape) { Shell.Sound(82); v.Close(); return true; }
            if (!v.Host) continue;
            if (Mood == "choose" && Count > 0)
            {
                int to = k switch { Key.Up or Key.Left => (_sel + Count - 1) % Count, Key.Down or Key.Right => (_sel + 1) % Count, >= Key.Key1 and <= Key.Key3 when (int)(k - Key.Key1) < Count => (int)(k - Key.Key1), _ => -1 };
                if (to >= 0) { CoopPick(to); continue; }
            }
            if (k is Key.Enter or Key.KpEnter or Key.Space) { CoopAct(); return true; }
        }
        if (!Shell.Clicked) return true;
        var m = Shell.Mouse;
        if (Later.HasPoint(m)) { Shell.Sound(82); if (Mood == "choose" || !v.Host) v.Close(); else CoopAct(); return true; }
        if (!v.Host) return true;
        if (Mood == "choose")
        {
            if (Take.HasPoint(m)) { CoopAct(); return true; }
            foreach (var (i, r) in _hits)
                if (r.HasPoint(m) && i < Count) { if (_sel == i) CoopAct(); else CoopPick(i); return true; }
            for (int i = 0; i < Count; i++)
                if (Box(i).HasPoint(m)) { if (_sel == i) CoopAct(); else CoopPick(i); return true; }
        }
        return true;
    }

    private void CoopPick(int i)
    {
        _sel = i;
        Shell.Sound(82);
        CoopView!.Select?.Invoke(i);
        Repaint();
    }

    /// <summary>The host's Enter: the job picked (back to the lobby, where START plays it), the debrief passed, or a new run after the last.</summary>
    private void CoopAct()
    {
        var v = CoopView!;
        Shell.Sound(82);
        switch (Mood)
        {
            case "choose": v.Close(); return;
            case "debrief": v.Continue?.Invoke(); return;
            default: v.NewRun?.Invoke(); return;
        }
    }
}
