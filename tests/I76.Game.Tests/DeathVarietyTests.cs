using I76.Game.Sim;
using I76.Sim.Damage;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's car death (DeathVariety): the stock death 100 ms late, its blast at half the size with half the sparks and
/// chunks, and a small event by cause at the moment of death.
/// </summary>
public class DeathVarietyTests
{
    /// <summary>The blast, the wreck and vexplode.wav come 100 ms after the moment of death.</summary>
    [Fact]
    public void TheDeathIsAHundredMillisecondsLate() => Assert.Equal(0.1f, DeathVariety.Delay);

    /// <summary>The blast's drawn size and its sparks and chunks are half the stock ones'.</summary>
    [Fact]
    public void TheBlastIsHalfTheStockOne()
    {
        Assert.Equal(0.5f, DeathVariety.SizeFactor);
        Assert.Equal(0.5f, DeathVariety.ShrapnelFactor);
        // the car blast is drawn at 6 m: 36 sparks stock, 18 here
        Assert.Equal(36, DeathVariety.Sparks(6f));
        Assert.Equal(18, DeathVariety.Sparks(6f, DeathVariety.ShrapnelFactor));
    }

    /// <summary>Every other chunk a dying car throws is drawn, from the first: half of any run.</summary>
    [Fact]
    public void HalfTheChunksAreDrawn()
    {
        Assert.True(DeathVariety.KeepChunk(1));
        Assert.False(DeathVariety.KeepChunk(0));
        foreach (int n in new[] { 2, 10, 25, 40 })
            Assert.Equal(n / 2, Enumerable.Range(0, n).Count(DeathVariety.KeepChunk));
    }

    /// <summary>The pre-event by cause: the hood for the engine, the wheel's burst, the cab's puff for the driver, a crack for the ammunition; nothing else.</summary>
    [Fact]
    public void ThePreEventFollowsTheCause()
    {
        Assert.Equal(DeathVariety.Pre.Hood, DeathVariety.PreFor(DeathCause.Engine));
        Assert.Equal(DeathVariety.Pre.Wheel, DeathVariety.PreFor(DeathCause.Wheel));
        Assert.Equal(DeathVariety.Pre.Cab, DeathVariety.PreFor(DeathCause.Driver));
        Assert.Equal(DeathVariety.Pre.Crack, DeathVariety.PreFor(DeathCause.Weapon));
        foreach (var c in new[] { DeathCause.Destroyed, DeathCause.Suspension, DeathCause.Brakes, DeathCause.Special, DeathCause.Fall, DeathCause.Rollover, DeathCause.SelfDestruct })
            Assert.Equal(DeathVariety.Pre.None, DeathVariety.PreFor(c));
    }

    /// <summary>The wheel that bursts is the one with the fewest hit points (on a tie the lower share of its own, then the first); none without wheels.</summary>
    [Fact]
    public void TheFailedWheelHasTheFewestHitPoints()
    {
        var slots = new Part?[20];
        Assert.Equal(-1, DeathVariety.FailedWheel(slots));
        for (int i = 0; i < 4; i++) slots[i] = new WheelPart(new Wheel(), 80);
        slots[0]!.Hp = 40; slots[1]!.Hp = 70; slots[2]!.Hp = 12; slots[3]!.Hp = 55;
        Assert.Equal(2, DeathVariety.FailedWheel(slots));
        slots[3]!.Hp = 0; slots[1]!.Hp = 0;
        Assert.Equal(1, DeathVariety.FailedWheel(slots));
        slots[3]!.Max = 40;   // the same 0 hit points, but of a smaller wheel: the first stays
        Assert.Equal(1, DeathVariety.FailedWheel(slots));
        // only the wheel slots count
        slots[7] = new WheelPart(new Wheel(), 80) { Hp = -5 };
        Assert.Equal(1, DeathVariety.FailedWheel(slots));
    }

    /// <summary>Off, or in another car than the modern look's own (a gun emplacement, a helicopter), the stock death.</summary>
    [Fact]
    public void OffIsTheStockDeath()
    {
        var car = new Vehicle();
        bool was = DeathVariety.Enabled;
        try
        {
            DeathVariety.Enabled = true;
            Assert.True(DeathVariety.Owns(car, modernCar: true));
            Assert.False(DeathVariety.Owns(car, modernCar: false));
            DeathVariety.Enabled = false;
            Assert.False(DeathVariety.Owns(car, modernCar: true));
        }
        finally { DeathVariety.Enabled = was; }
    }
}
