using System.Numerics;
using System.Runtime.CompilerServices;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// Auto handicap (docs/multiplayer.md "Auto handicap"): the game moves each player's level from the standings, kills minus deaths
/// against the table's average, a whole step (<see cref="Handicaps.AutoStep"/>) a level, one step a tick at most, given back only
/// once the standing has fallen <see cref="Handicaps.AutoBand"/> short of the line it was taken at, and nothing after the end.
/// </summary>
public class AutoHandicapTests
{
    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    /// <summary>A network combat melee of <paramref name="players"/> with Auto on, no arena: the rule alone is exercised.</summary>
    private static MeleeGame Game(int players, MeleeKind kind = MeleeKind.Combat, bool auto = true)
    {
        var world = new SimWorld(new Flat(), new CrtRand(5));
        var host = new ScriptHost(world, [], (Objectives)RuntimeHelpers.GetUninitializedObject(typeof(Objectives)), _ => 1f);
        var game = new MeleeGame
        {
            World = world, Host = host, Rand = world.Rand, Roster = [], PlayerCar = "x.vcf", Build = (_, _) => new Vehicle(),
            Rules = new MeleeRules { Network = true, AutoHandicaps = auto, Kind = kind },
        };
        for (int i = 0; i < players; i++) game.AddPlayer("x.vcf", ((char)('A' + i)).ToString());
        return game;
    }

    private static int[] Levels(MeleeGame g) => [.. g.Players.Select(p => p.Car.Handicap)];

    /// <summary>A kill of <paramref name="victim"/> by <paramref name="killer"/> as the scoreboard books it, then one tick of the rule.</summary>
    private static void Kill(MeleeGame g, int killer, int victim)
    {
        g.Players[killer].Kills++;
        if (victim >= 0) g.Players[victim].Deaths++;
        g.Frame(g.Host.FrameDelta);
    }

    /// <summary>The documented lines: a level is taken at 1, 2, 3, 4 and 5 net kills over the average (the same under), one step at a time, and given back at the line below.</summary>
    [Fact]
    public void TheRuleTakesALevelAtEachStepAndGivesItBackAtTheLineBelow()
    {
        Assert.Equal((1f, 1f, 5), (Handicaps.AutoStep, Handicaps.AutoBand, Handicaps.AutoCap));
        Assert.Equal(0, Handicaps.AutoNext(0, 0.99f));
        Assert.Equal(-1, Handicaps.AutoNext(0, 1f));
        Assert.Equal(-1, Handicaps.AutoNext(-1, 1.99f));
        Assert.Equal(-2, Handicaps.AutoNext(-1, 2f));
        Assert.Equal(-3, Handicaps.AutoNext(-2, 3f));
        Assert.Equal(-4, Handicaps.AutoNext(-3, 4f));
        Assert.Equal(-5, Handicaps.AutoNext(-4, 5f));
        Assert.Equal(-5, Handicaps.AutoNext(-5, 100f));      // the cap
        Assert.Equal(-1, Handicaps.AutoNext(0, 100f));       // one step a tick, however far ahead
        Assert.Equal(-5, Handicaps.AutoNext(-5, 4.01f));     // kept until the line below
        Assert.Equal(-4, Handicaps.AutoNext(-5, 4f));
        Assert.Equal(-1, Handicaps.AutoNext(-1, 0.01f));
        Assert.Equal(0, Handicaps.AutoNext(-1, 0f));         // never past even
        // and behind the average, the mirror
        Assert.Equal(0, Handicaps.AutoNext(0, -0.99f));
        Assert.Equal(1, Handicaps.AutoNext(0, -1f));
        Assert.Equal(2, Handicaps.AutoNext(1, -2f));
        Assert.Equal(5, Handicaps.AutoNext(4, -5f));
        Assert.Equal(5, Handicaps.AutoNext(5, -100f));
        Assert.Equal(1, Handicaps.AutoNext(1, -0.01f));
        Assert.Equal(0, Handicaps.AutoNext(1, 0f));
        Assert.Equal(1, Handicaps.AutoNext(2, -1f));
        // a level far from its standing comes back one step a tick too
        Assert.Equal(-4, Handicaps.AutoNext(-5, -100f));
        Assert.Equal(4, Handicaps.AutoNext(5, 100f));
        Assert.Equal("*** Taurus is now -1 (deals x0.75, takes x1.33)", Handicaps.Announce("Taurus", -1));
        Assert.Equal("*** Taurus is now -5 (deals x0.2, takes x3)", Handicaps.Announce("Taurus", -5));
    }

    /// <summary>A kill of B by A: A is −1 and B +1, said once each; a kill back gives both back at even, and each event moves a level one step at most.</summary>
    [Fact]
    public void EachKillMovesALevelOneStepAndEvenGivesItBack()
    {
        var g = Game(4);
        var said = new List<string>();
        g.Message += said.Add;
        Kill(g, 0, 1);
        Assert.Equal([-1, 1, 0, 0], Levels(g));
        Assert.Equal(["*** A is now -1 (deals x0.75, takes x1.33)", "*** B is now +1 (deals x1.33, takes x0.75)"], said);
        Kill(g, 1, 0);
        Assert.Equal([0, 0, 0, 0], Levels(g));
        Kill(g, 0, 1);
        Kill(g, 0, 1);
        Assert.Equal([-2, 2, 0, 0], Levels(g));
        // one back: A two over the average, at the line below -2: a step back, not two
        Kill(g, 1, 0);
        Assert.Equal([-1, 1, 0, 0], Levels(g));
        Assert.Equal(10, said.Count);
    }

    /// <summary>The standing is against the table's average: kills of the AI fill (nobody's death) raise everyone's average, and a table that scores alike stays at 0.</summary>
    [Fact]
    public void TheStandingIsAgainstTheTablesAverage()
    {
        var g = Game(3);
        Kill(g, 0, -1);
        Assert.Equal([0, 0, 0], Levels(g));                 // 0.67 over the average of 0.33: not yet
        Kill(g, 0, -1);
        Assert.Equal([-1, 0, 0], Levels(g));                // 1.33 over 0.67; the others 0.67 under: not yet
        var even = Game(3);
        for (int k = 0; k < 4; k++) for (int p = 0; p < 3; p++) Kill(even, p, (p + 1) % 3);
        Assert.Equal([0, 0, 0], Levels(even));
        Assert.All(even.Players, p => Assert.Equal((4, 4), (p.Kills, p.Deaths)));
    }

    /// <summary>
    /// A standing far ahead is taken one step a tick, to the cap and no further; the three it leaves behind are five under the
    /// table's average (20 over 4) and climb to +5 the same way; the counters themselves are untouched.
    /// </summary>
    [Fact]
    public void TheLevelMovesOneStepATickToTheCap()
    {
        var g = Game(4);
        g.Players[0].Kills = 20;
        var seen = new List<(string, int)>();
        g.HandicapChanged += p => seen.Add((p.Name, p.Car.Handicap));
        for (int t = 0; t < 6; t++) g.Frame(g.Host.FrameDelta);
        Assert.Equal([("A", -1), ("A", -2), ("A", -3), ("A", -4), ("A", -5)], seen.Where(s => s.Item1 == "A"));
        Assert.Equal([("B", 1), ("B", 2), ("B", 3), ("B", 4), ("B", 5)], seen.Where(s => s.Item1 == "B"));
        Assert.Equal([-5, 5, 5, 5], Levels(g));
        Assert.Equal(20, g.Players[0].Kills);
        Assert.Equal(20f, g.Players[0].Form);
    }

    /// <summary>A standing does not fade with time: the lead stands until the field catches up, so a level is held through a quiet spell.</summary>
    [Fact]
    public void TheStandingHoldsThroughAQuietSpell()
    {
        var g = Game(4);
        Kill(g, 0, 1); Kill(g, 0, 1);
        Assert.Equal([-2, 2, 0, 0], Levels(g));
        for (int t = 0; t < 20 * 600; t++) g.Frame(g.Host.FrameDelta);        // ten minutes of nothing
        Assert.Equal([-2, 2, 0, 0], Levels(g));
        Assert.Equal((2f, -2f), (g.Players[0].Form, g.Players[1].Form));
    }

    /// <summary>Nothing moves in a race (the kills column counts laps), with the rule off, nor once the game has ended.</summary>
    [Fact]
    public void NothingMovesOutsideACombatMeleeOrAfterTheEnd()
    {
        var race = Game(3, MeleeKind.Race);
        for (int k = 0; k < 6; k++) Kill(race, 0, 1);
        Assert.Equal([0, 0, 0], Levels(race));
        var off = Game(3, auto: false);
        for (int k = 0; k < 6; k++) Kill(off, 0, 1);
        Assert.Equal([0, 0, 0], Levels(off));
        var world = new SimWorld(new Flat(), new CrtRand(5));
        var over = new MeleeGame
        {
            World = world, Host = new ScriptHost(world, [], (Objectives)RuntimeHelpers.GetUninitializedObject(typeof(Objectives)), _ => 1f),
            Rand = world.Rand, Roster = [], PlayerCar = "x.vcf", Build = (_, _) => new Vehicle(),
            Rules = new MeleeRules { Network = true, AutoHandicaps = true, TimeLimit = 60 },
        };
        for (int i = 0; i < 3; i++) over.AddPlayer("x.vcf", $"{i}");
        over.Host.Now = 61;
        over.Frame(over.Host.FrameDelta);
        Assert.True(over.Ended);
        for (int k = 0; k < 6; k++) Kill(over, 0, 1);
        Assert.Equal([0, 0, 0], Levels(over));
    }
}
