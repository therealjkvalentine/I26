using I76.Sim.Vehicles;

namespace I76.Game.Sim;

/// <summary>The radar's ping (CRADAR.WAV): the sweep's, and the steady beeping while a target is held.</summary>
public partial class Cockpit
{
    /// <summary>
    /// Whether <paramref name="v"/> may ping this frame. The sweep pings at its steps (not on a late frame's retry). With a
    /// target held the original's sweep stops but its ping test keeps running every frame with no time gate, the window from
    /// the step the sweep stopped on to the target's frame: every listed car in it but the target pings. Here that is once a
    /// sim tick.
    /// </summary>
    private bool PingsNow(Vehicle v, Vehicle? locked, bool retry) => locked is null ? !retry : v != locked && _steps > 0;

    /// <summary>CRADAR.WAV: its header's one voice, so a ping asked for while one sounds is dropped (a held target beeps at the sample's length).</summary>
    private void RadarPing()
    {
        if (Audio?.Get("cradar.wav") is not { } s) return;
        bool started = Node3D.OneShotSingle("cradar.wav", s);
        if (Voice3D.LogEnabled) Voice3D.Trace($"radar ping {(started ? "started" : "dropped (its one voice sounds)")}{(Combat?.RadarTarget.GetValueOrDefault(Car) is { } t ? $", target held: {t.Definition}" : "")}");
    }
}
