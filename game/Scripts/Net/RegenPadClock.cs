using I76.Sim.Missions;

namespace I76.Game.Net;

/// <summary>
/// The regen pad's hold as the player's own HUD follows it (I26's readout, docs/deviations.md "Regen pad readout"): the server's
/// rule (<see cref="MeleeGame"/>'s regen) replayed on this machine's clock from what it sees, its own car's place and the renewals
/// the server tells. A pad regenerates a car only 30 s after its last spawn or regen, and only once the car has left the pad
/// since: until then a pad "does nothing". Presentation only; the server decides.
/// </summary>
public sealed class RegenPadClock
{
    private double _at;
    private bool _offPad;

    /// <param name="now">The spawn: the hold runs from here.</param>
    public RegenPadClock(double now) => Renewed(now);

    /// <summary>A spawn, respawn or regen of the own car (the hold starts again, the car must leave the pad first).</summary>
    public void Renewed(double now) { _at = now; _offPad = false; }

    /// <summary>
    /// This frame: null when the car is off every pad or the pad is ready; else the seconds left of the hold, and whether the car
    /// must first drive off the pad (it has not left it since its last spawn or regen, which keeps the hold full).
    /// </summary>
    public (double Left, bool MustLeave)? Update(double now, bool onPad)
    {
        if (!onPad) { _offPad = true; return null; }
        if (now - _at >= MeleeGame.RegenInterval) return null;
        if (!_offPad) { _at = now; return (MeleeGame.RegenInterval, true); }
        return (MeleeGame.RegenInterval - (now - _at), false);
    }

    /// <summary>The HUD's words for a hold.</summary>
    public static string Words((double Left, bool MustLeave) hold) =>
        hold.MustLeave ? "Regen pad: drive off it and back on" : $"Regen pad: ready in {System.Math.Ceiling(hold.Left):0} s";
}
