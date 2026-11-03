namespace I76.Sim.Views;

/// <summary>
/// How often the 1997 look's clouds step. The original scrolls them one fixed step each time the sky is drawn, and draws it
/// again for the rear-view mirror's full mode every frame (measured in the original: the scroll is called twice a frame with
/// the mirror in full mode in the cockpit view, once with it off), so the clouds drift twice as fast then. Here the step
/// runs once a sim tick, and a second one while the mirror pass is drawn.
/// </summary>
public static class SkyScroll
{
    public const int MirrorFull = 2;

    /// <summary>The scroll steps per sim tick: 2 with the cockpit view shown and the mirror in full mode (and present), else 1.</summary>
    public static int StepsPerTick(bool cockpitShown, bool mirrorPresent, int mirrorMode) =>
        cockpitShown && mirrorPresent && mirrorMode == MirrorFull ? 2 : 1;
}
