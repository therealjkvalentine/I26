using Godot;

namespace I76.Game.Boot;

/// <summary>
/// Young garbage collections at a small, steady size, each just after the renderer has been handed the frame. The runtime collects the
/// young generation when about 48 MB have been allocated (this machine's budget; the runtime sizes it from the processor's cache), and a
/// young collection's pause grows with the time since the last one (the survivors and the older objects written to meanwhile): 4-9 ms a
/// second while the main thread allocated 170 kB a frame, 15-60 ms every 10-20 s once it allocated a tenth of that, both of them slow frames.
/// Collecting the young generation whenever <see cref="BudgetMb"/> have been allocated (a check of a counter a frame) keeps each pause near
/// 1-3 ms, and doing it after the frame's draw is handed over lets it overlap the card's work on that frame where the card is the limit.
/// No effect on what is simulated or drawn. <c>[port]</c> nothing; I76_GC_PACE_MB=0 leaves the runtime's own schedule (for a measurement),
/// N sets the budget.
/// </summary>
public static class GcPacer
{
    /// <summary>Megabytes allocated (every thread) between collections; 0: off.</summary>
    public static readonly double BudgetMb = double.TryParse(System.Environment.GetEnvironmentVariable("I76_GC_PACE_MB"), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out double mb) ? mb : 8;

    private static long _last;
    private static int _seen;
    private static bool _installed;

    /// <summary>Once a run (a scene reload keeps it).</summary>
    public static void Install()
    {
        if (_installed || BudgetMb <= 0) return;
        _installed = true;
        _last = System.GC.GetTotalAllocatedBytes(false);
        RenderingServer.FramePostDraw += AfterDraw;
    }

    /// <summary>At most one paced collection in this long (seconds): a burst of allocation (a load's workers) is left to the runtime.</summary>
    private const double MinInterval = 0.5;
    private static double _at;

    private static void AfterDraw()
    {
        long now = System.GC.GetTotalAllocatedBytes(false);
        int count = System.GC.CollectionCount(0);
        // a collection the runtime made by itself since: count from there
        if (count != _seen) { _seen = count; _last = now; return; }
        if (now - _last < (long)(BudgetMb * 1048576)) return;
        // only in play: a mission's build (its workers allocate hundreds of MB) is left to the runtime's own schedule
        if (I76.Game.World.MissionBuilder.LastStage != "mission going") return;
        double t = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        if (t - _at < MinInterval) return;
        _at = t;
        System.GC.Collect(0, System.GCCollectionMode.Forced, blocking: true, compacting: false);
        _seen = System.GC.CollectionCount(0);
        _last = System.GC.GetTotalAllocatedBytes(false);
    }
}
