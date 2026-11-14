using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>The player's own car is not drawn from the cockpit or the hood view (F6); a script camera always shows it.</summary>
public class OwnCarTests
{
    [Theory]
    [InlineData(false, false, false, true)]    // F2 and the other outside views
    [InlineData(true, false, false, false)]    // the cockpit
    [InlineData(false, true, false, false)]    // the hood view: glancing back shows the road, not the body
    [InlineData(true, false, true, true)]      // a cut-scene from the cockpit
    [InlineData(false, true, true, true)]      // a cut-scene from the hood view
    public void TheOwnCarIsSkippedInTheCockpitAndTheHoodView(bool cockpit, bool hood, bool script, bool drawn) =>
        Assert.Equal(drawn, OwnCar.Drawn(cockpit, hood, script));

    /// <summary>A helicopter's rotor from the seat: the main one, which the interior carries, with the 3D dash; never the tail's.</summary>
    [Theory]
    [InlineData(false, false, false, true, true, true)]     // F2: both rotors with the body
    [InlineData(false, false, false, true, false, true)]
    [InlineData(true, false, false, true, true, true)]      // F1, 3D dash: the main rotor overhead
    [InlineData(true, false, false, true, false, false)]    // F1: not the tail rotor (not in the interior)
    [InlineData(true, false, false, false, true, false)]    // F1, 2D dash: no interior drawn
    [InlineData(false, true, false, true, true, false)]     // F6: neither
    [InlineData(true, false, true, true, false, true)]      // a cut-scene: the body's
    public void AHelicoptersMainRotorShowsFromTheSeat(bool cockpit, bool hood, bool script, bool dash3d, bool inInterior, bool drawn) =>
        Assert.Equal(drawn, OwnCar.RotorDrawn(cockpit, hood, script, dash3d, inInterior));
}
