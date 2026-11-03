using System.Numerics;
using I76.Game.Boot;
using I76.Game.Shell;
using I76.Sim;
using I76.Sim.Damage;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Play Options reach the mission's combat: the shell's Difficulty Level (0 Wimp, 1 Champion, 2 Badass) and the cheats.</summary>
public class PlayOptionsCombatTests
{
    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0f, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static (Combat Combat, DamageModel Damage) Rig(bool net = false)
    {
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var combat = new Combat(world, _ => null) { NetSession = net };
        return (combat, new DamageModel(world, combat));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheShellsDifficultyReachesTheCombat(int difficulty)
    {
        var (c, _) = Rig();
        PlayOptionsCombat.Apply(new ShellSettings { Difficulty = difficulty }, c);
        Assert.Equal(difficulty, c.Difficulty);
    }

    /// <summary>The Esc menu's change, applied again when it closes, replaces the mission start's.</summary>
    [Fact]
    public void AChangeInPlayIsApplied()
    {
        var (c, _) = Rig();
        PlayOptionsCombat.Apply(new ShellSettings { Difficulty = 0 }, c);
        PlayOptionsCombat.Apply(new ShellSettings { Difficulty = 2 }, c);
        Assert.Equal(2, c.Difficulty);
    }

    [Fact]
    public void TheCheatsStillReachTheCombat()
    {
        var (c, dm) = Rig();
        PlayOptionsCombat.Apply(new ShellSettings { PlayOptions = 0x1c }, c);
        Assert.True(c.UnlimitedAmmo);
        Assert.True(dm.UnlimitedArmour);
        Assert.True(dm.UnlimitedChassis);
        var (n, _) = Rig(net: true);
        PlayOptionsCombat.Apply(new ShellSettings { PlayOptions = 4 }, n);
        Assert.False(n.UnlimitedAmmo);
    }
}
