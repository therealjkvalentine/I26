using I76.Game.Sim;
using I76.Sim.Damage;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's deaths by cause (DeathVariety): the burning wheels' odds, and each cause's plan.</summary>
public class DeathVarietyTests
{
    /// <summary>Over every seed: no burning wheel 10 %, one 65 %, two 20 %, three or more 5 %.</summary>
    [Fact]
    public void TheBurningWheelsAreDrawnAtTheirOdds()
    {
        var n = new int[5];
        for (int s = 0; s < 65536; s++) n[DeathVariety.WheelCount((ushort)s)]++;
        Assert.InRange(n[0] / 65536.0, 0.09, 0.11);
        Assert.InRange(n[1] / 65536.0, 0.64, 0.66);
        Assert.InRange(n[2] / 65536.0, 0.19, 0.21);
        Assert.InRange((n[3] + n[4]) / 65536.0, 0.04, 0.06);
    }

    /// <summary>The original's full blast is kept for a special kill and about one death in eight of the others.</summary>
    [Fact]
    public void OnlyTheOccasionalDeathKeepsTheBigBlast()
    {
        int big = 0;
        for (int s = 0; s < 65536; s++) if (DeathVariety.Catastrophic(DeathCause.Engine, (ushort)s)) big++;
        Assert.InRange(big / 65536.0, 0.115, 0.135);
        Assert.True(DeathVariety.Catastrophic(DeathCause.Special, 1));
        Assert.True(DeathVariety.For(DeathCause.Wheel, 7).Calm != DeathVariety.Catastrophic(DeathCause.Wheel, 7));
        Assert.False(DeathVariety.For(DeathCause.Wheel, 7, enabled: false).Calm);
    }

    [Fact]
    public void APlanFollowsItsSeed()
    {
        foreach (DeathCause c in Enum.GetValues<DeathCause>())
        {
            var a = DeathVariety.For(c, 4242);
            var b = DeathVariety.For(c, 4242);
            Assert.Equal(a.FlamingWheels, b.FlamingWheels);
            Assert.Equal(a.FireDelay, b.FireDelay);
            Assert.Equal(a.Blasts, b.Blasts);
        }
    }

    [Fact]
    public void EachCauseHasItsLook()
    {
        for (int s = 0; s < 2000; s++)
        {
            var seed = (ushort)(s * 31);
            // a wheel's or a brake's death always throws one burning wheel
            Assert.True(DeathVariety.For(DeathCause.Wheel, seed).FlamingWheels >= 1);
            Assert.True(DeathVariety.For(DeathCause.Brakes, seed).FlamingWheels >= 1);
            // the driver's: no blast, a fire that catches 3 to 7 s later
            var d = DeathVariety.For(DeathCause.Driver, seed);
            Assert.Empty(d.Blasts);
            Assert.InRange(d.FireDelay, 3f, 7f);
            // the ammunition cooks off in four to six pops
            Assert.InRange(DeathVariety.For(DeathCause.Weapon, seed).Blasts.Length, 4, 6);
        }
        Assert.True(DeathVariety.For(DeathCause.Engine, 1).FireAt > 0);         // on the engine, at the front
        Assert.True(DeathVariety.For(DeathCause.Special, 1).FireAt < 0);        // at the rear
        Assert.True(DeathVariety.For(DeathCause.Suspension, 1).Sag > 0);
        Assert.Empty(DeathVariety.For(DeathCause.SelfDestruct, 1).Blasts);    // the original's blast alone
        Assert.Contains(DeathVariety.For(DeathCause.Fall, 1).Blasts, b => b.Kind == DeathVariety.BlastKind.Dust);
    }

    /// <summary>Off: every death is the plain one, every thrown wheel as hot as the wreck.</summary>
    [Fact]
    public void OffIsThePlainDeath() => Assert.Same(DeathVariety.Plan.Plain, DeathVariety.For(DeathCause.Engine, 7, enabled: false));
}
