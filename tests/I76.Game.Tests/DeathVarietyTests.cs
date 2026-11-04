using I76.Game.Sim;
using I76.Sim.Damage;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's deaths by cause (DeathVariety): two stages, one tank blast, the drawn throw, the catastrophic award.</summary>
public class DeathVarietyTests
{
    private static readonly DeathCause[] Ordinary =
        [DeathCause.Engine, DeathCause.Wheel, DeathCause.Brakes, DeathCause.Suspension, DeathCause.Weapon, DeathCause.Fall, DeathCause.Rollover, DeathCause.SelfDestruct, DeathCause.Destroyed];

    /// <summary>The award: every special kill and about one death in eight of the others, never the driver's (no blast).</summary>
    [Fact]
    public void TheAwardIsOccasional()
    {
        int big = 0;
        for (int s = 0; s < 65536; s++) if (DeathVariety.Catastrophic(DeathCause.Engine, (ushort)s)) big++;
        Assert.InRange(big / 65536.0, 0.115, 0.135);
        Assert.True(DeathVariety.Catastrophic(DeathCause.Special, 1));
        for (int s = 0; s < 65536; s += 7) Assert.False(DeathVariety.Catastrophic(DeathCause.Driver, (ushort)s));
        Assert.Equal(DeathVariety.Catastrophic(DeathCause.Wheel, 7), DeathVariety.For(DeathCause.Wheel, 7).Award);
        Assert.False(DeathVariety.For(DeathCause.Wheel, 7, enabled: false).Award);
    }

    /// <summary>Every ordinary death: exactly one tank blast, 200 to 500 ms after stage 1 (at once for the self-destruct, which has no stage 1), at the ordinary size.</summary>
    [Fact]
    public void AnOrdinaryDeathHasOneTankBlast()
    {
        foreach (var c in Ordinary)
            for (int s = 0; s < 4000; s++)
            {
                var p = DeathVariety.For(c, (ushort)(s * 17));
                if (p.Award) continue;
                Assert.Equal(1, DeathVariety.TankBlasts(p));
                Assert.InRange(p.TankSize, 2.5f, 4f);
                if (p.First is { Length: > 0 }) Assert.InRange(p.TankAt, 0.2f, 0.5f);
                else Assert.Equal(0f, p.TankAt);
                // no stage-1 blast as big as the tank: the component goes first, the tank is the death's one big blast
                Assert.All(p.First!, b => Assert.True(b.Kind != DeathVariety.BlastKind.Fire || b.Size < p.TankSize));
                // only the cook-off adds blasts of its own
                if (c != DeathCause.Weapon) Assert.Empty(p.Blasts);
                Assert.Equal(c == DeathCause.Weapon ? DeathVariety.RollBlast.Pop : DeathVariety.RollBlast.Flare, p.Roll2);
                Assert.True(p.Debris < 1f);
            }
    }

    /// <summary>The award is the only death with more: a bigger tank sooner, a launch, parts thrown burning, a longer, bigger fire.</summary>
    [Fact]
    public void TheAwardIsUnlikeTheOthers()
    {
        foreach (var c in Ordinary.Append(DeathCause.Special))
            for (int s = 0; s < 4000; s++)
            {
                var p = DeathVariety.For(c, (ushort)(s * 17));
                if (!p.Award) continue;
                var row = DeathVariety.Table[c];
                Assert.Equal(DeathVariety.Award.Tank, p.TankSize);
                Assert.True(p.TankAt >= DeathVariety.Award.MinDelay - 1e-6f && p.TankAt <= System.MathF.Max(row.Delay, DeathVariety.Award.MinDelay));
                Assert.Equal(DeathVariety.ThrowKind.Launch, p.Throw.Kind);
                Assert.True(p.PanelsBurn && p.Panels >= DeathVariety.Award.Panels);
                Assert.True(p.FlamingWheels >= 3);
                Assert.True(p.FlamesFull > 10f && p.FireSize >= DeathVariety.Award.FireSize);
                Assert.NotEqual(DeathVariety.RollBlast.Flare, p.Roll2);
            }
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
            Assert.Equal(a.First, b.First);
            Assert.Equal(a.Throw, b.Throw);
            Assert.Equal(a.TankAt, b.TankAt);
        }
    }

    /// <summary>Each cause's throw and look, as the design has it.</summary>
    [Fact]
    public void EachCauseHasItsLook()
    {
        for (int s = 0; s < 2000; s++)
        {
            var seed = (ushort)(s * 31);
            // the wheel's death: its first thrown wheel burns; it slides
            var w = DeathVariety.For(DeathCause.Wheel, seed);
            if (!w.Award) { Assert.Equal(1, w.FlamingWheels); Assert.Equal(DeathVariety.ThrowKind.Slide, w.Throw.Kind); }
            // brakes: slides and spins out, no burning wheel
            var br = DeathVariety.For(DeathCause.Brakes, seed);
            if (!br.Award) { Assert.Equal(0, br.FlamingWheels); Assert.Equal(DeathVariety.ThrowKind.Slide, br.Throw.Kind); Assert.NotEqual(0f, br.Throw.Yaw); }
            // the engine: down and back at the tank, the hood thrown burning
            var e = DeathVariety.For(DeathCause.Engine, seed);
            if (!e.Award) { Assert.Equal(DeathVariety.ThrowKind.DownBack, e.Throw.Kind); Assert.True(e.Throw.AtTank && e.Throw.Back > 0); Assert.True(e.Panels == 1 && e.PanelsBurn); }
            // a weapon: launched, the ammunition cooking off in three to five more pops
            var wp = DeathVariety.For(DeathCause.Weapon, seed);
            Assert.InRange(wp.Blasts.Length, 3, 5);
            Assert.Equal(DeathVariety.ThrowKind.Launch, wp.Throw.Kind);
            // the driver's: no blast, no throw, a fire that catches 3 to 7 s later, thin smoke
            var d = DeathVariety.For(DeathCause.Driver, seed);
            Assert.Empty(d.Blasts);
            Assert.Empty(d.First!);
            Assert.Equal(0, DeathVariety.TankBlasts(d));
            Assert.Equal(DeathVariety.ThrowKind.None, d.Throw.Kind);
            Assert.InRange(d.FireDelay, 3f, 7f);
            Assert.True(d.Smoke < DeathVariety.For(DeathCause.Engine, seed).Smoke);
        }
        Assert.True(DeathVariety.For(DeathCause.Engine, 1).FireAt > 0);         // on the engine, at the front
        Assert.True(DeathVariety.For(DeathCause.Special, 1).FireAt < 0);        // at the rear
        Assert.True(DeathVariety.For(DeathCause.Suspension, 1).Sag > 0);
        Assert.Equal(DeathVariety.ThrowKind.Drop, DeathVariety.Table[DeathCause.Suspension].Throw.Kind);
        Assert.Equal(DeathVariety.ThrowKind.None, DeathVariety.Table[DeathCause.Rollover].Throw.Kind);   // the sim's tumble
        Assert.True(DeathVariety.Table[DeathCause.Rollover].Spread > 1f);                                // its flames spill out
        Assert.Empty(DeathVariety.Table[DeathCause.SelfDestruct].First);                               // the blast alone
        Assert.Contains(DeathVariety.Table[DeathCause.Fall].First, b => b.Kind == DeathVariety.BlastKind.Dust);
        // every launch ends the way up it started: whole turns of roll and pitch
        foreach (var row in DeathVariety.Table.Values.Where(r => r.Throw.Kind == DeathVariety.ThrowKind.Launch))
        {
            Assert.Equal(0f, System.MathF.IEEERemainder(row.Throw.Roll, System.MathF.Tau), 3);
            Assert.Equal(0f, System.MathF.IEEERemainder(row.Throw.Pitch, System.MathF.Tau), 3);
        }
    }

    /// <summary>Over the melee's mix of causes the wreck throws burning wheels at about 0.6 to 0.8 a death (the first pass: 1.3).</summary>
    [Fact]
    public void FewerWheelsBurn()
    {
        var mix = new (DeathCause C, double W)[] { (DeathCause.Wheel, 0.44), (DeathCause.Brakes, 0.25), (DeathCause.Rollover, 0.10), (DeathCause.Destroyed, 0.08), (DeathCause.Weapon, 0.06), (DeathCause.Suspension, 0.04), (DeathCause.Engine, 0.01), (DeathCause.Special, 0.01) };
        double sum = 0;
        foreach (var (c, wt) in mix)
        {
            double each = 0;
            for (int s = 0; s < 65536; s += 3) each += System.Math.Min(3, DeathVariety.For(c, (ushort)s).FlamingWheels);
            sum += wt * each / (65536 / 3.0);
        }
        Assert.InRange(sum, 0.55, 0.85);
    }

    /// <summary>Off: every death is the plain one, every thrown wheel as hot as the wreck.</summary>
    [Fact]
    public void OffIsThePlainDeath() => Assert.Same(DeathVariety.Plan.Plain, DeathVariety.For(DeathCause.Engine, 7, enabled: false));
}
