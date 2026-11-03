using I76.Game.Net;
using I76.Sim.Math;
using I76.Sim.Missions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The regen pad readout follows the server's rule: 30 s from the last spawn or regen, and the car must have left the pad since (#102).</summary>
public class RegenPadClockTests
{
    [Fact] public void OffThePadThereIsNothingToShow()
    {
        var c = new RegenPadClock(0);
        Assert.Null(c.Update(5, onPad: false));
    }

    [Fact] public void OnAPadInsideTheHoldItCountsDownFromTheSpawn()
    {
        var c = new RegenPadClock(0);
        c.Update(1, onPad: false);
        var h = c.Update(12, onPad: true);
        Assert.NotNull(h);
        Assert.False(h!.Value.MustLeave);
        Assert.Equal(18, h.Value.Left, 3);
        Assert.Equal("Regen pad: ready in 18 s", RegenPadClock.Words(h.Value));
        Assert.Null(c.Update(MeleeGame.RegenInterval, onPad: true));   // the server regenerates the car now
    }

    [Fact] public void ARegenStartsTheHoldAgain()
    {
        var c = new RegenPadClock(0);
        c.Update(1, onPad: false);
        Assert.Null(c.Update(40, onPad: true));
        c.Renewed(40);
        c.Update(41, onPad: false);
        Assert.Equal(25, c.Update(45, onPad: true)!.Value.Left, 3);
    }

    [Fact] public void ACarThatNeverLeftThePadMustDriveOffFirst()
    {
        // spawned on a pad: the server's timer starts again every frame until the car leaves, so waiting there does nothing
        var c = new RegenPadClock(0);
        for (int t = 1; t <= 60; t++) Assert.True(c.Update(t, onPad: true)!.Value.MustLeave);
        Assert.Null(c.Update(61, onPad: false));
        Assert.Equal(MeleeGame.RegenInterval - 2, c.Update(62, onPad: true)!.Value.Left, 3);   // from its last frame on the pad
    }

    [Fact] public void ThePadReachesTenMetres()
    {
        var pads = new[] { new MeleeGame.Spot(default, new Double3(100, 0, 100)) };
        Assert.True(MeleeGame.OnRegenPad(pads, new System.Numerics.Vector3(109.9f, 0, 100)));
        Assert.False(MeleeGame.OnRegenPad(pads, new System.Numerics.Vector3(110.1f, 0, 100)));
    }
}
