using System.Numerics;
using I76.Server;
using I76.Sim.Missions;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

/// <summary>
/// Auto handicap on a table of mixed skill (docs/multiplayer.md "Auto handicap"), as <c>i76 melee-sim</c> plays it: six bots of three
/// strengths (a factor on the damage each lands, the test-only skill knob, with the AI tier) fight for five game-minutes with the rule
/// off and on, from one seed, so the behaviour is pinned: the rule moves levels, says so, by the standings (harder over the table's mean,
/// easier under it), the strong pair ends taken hardest, and the game
/// stays deterministic.
/// </summary>
public class AutoHandicapSimTests(ITestOutputHelper output)
{
    /// <summary>What a bot's rounds, splash and flame land, scaled by its strength (a copy: a splash packet may go to several cars).</summary>
    private sealed class Strength(IDamageSink inner, Func<object?, float> of) : IDamageSink
    {
        private static DamagePacket Scaled(DamagePacket p, float f)
        {
            if (f == 1f) return p;
            var q = new DamagePacket { Attacker = p.Attacker, Mask = p.Mask, Flags = p.Flags, Kind = p.Kind };
            for (int c = 0; c < 4; c++) q.Ch[c] = (int)(p.Ch[c] * f);
            return q;
        }
        public void Apply(object victim, DamagePacket p, Vector3 hitLocal) => inner.Apply(victim, Scaled(p, of(p.Attacker)), hitLocal);
        public void Apply(object victim, DamagePacket p, Vector3 hitLocal, int node) => inner.Apply(victim, Scaled(p, of(p.Attacker)), hitLocal, node);
        public void CaltropTyreHit(Vehicle v, Vector3 at) => inner.CaltropTyreHit(v, at);
    }

    private static readonly int[] Tiers = [1, 1, 3, 3, 5, 5];
    private static readonly float[] Strengths = [2f, 2f, 1f, 1f, 0.5f, 0.5f];
    private const double Minutes = 5;

    /// <summary>The game's numbers; <paramref name="Moves"/> each level move with the mover's kills minus deaths less the table's mean then.</summary>
    private sealed record Outcome(int[] Kills, int[] Deaths, int[] Levels, List<string> Said, List<(int Level, float Over)> Moves);

    private static Outcome Play(string install, HandicapMode mode, double minutes)
    {
        var host = new MeleeHost(new ServerOptions { Install = install, Arena = "m05", Seed = 3, Bots = 6, Ai = 4, MaxPlayers = 8, Handicaps = mode, TimeLimit = 900, BotChoices = [.. Enumerable.Repeat("vppirna1.vcf", 6)] });
        var bots = host.Bots.OrderByDescending(b => b.Slot).ToList();
        var strength = new Dictionary<Vehicle, float>();
        for (int k = 0; k < bots.Count; k++)
        {
            var p = bots[k].Player;
            host.Setup.Script!.Host.SetSkill(p.Entity, Tiers[k], Tiers[k]);
            p.Entity.Aggression = Tiers[k] - 1;
            strength[p.Car] = Strengths[k];
        }
        host.Setup.Combat.Damage = new Strength(host.Setup.Combat.Damage!, a => a is Vehicle v && strength.TryGetValue(v, out float f) ? f : 1f);
        var said = new List<string>();
        var moves = new List<(int, float)>();
        host.Game.HandicapChanged += p =>
        {
            said.Add($"{Tiers[bots.FindIndex(b => b.Player == p)]}:{Handicaps.Label(p.Car.Handicap)}");
            moves.Add((p.Car.Handicap, p.Kills - p.Deaths - (float)bots.Average(b => b.Player.Kills - b.Player.Deaths)));
        };
        int ticks = (int)(minutes * 60 / host.Session.TickSeconds);
        for (int t = 0; t < ticks && !host.Over; t++) host.Step();
        return new Outcome([.. bots.Select(b => b.Player.Kills)], [.. bots.Select(b => b.Player.Deaths)], [.. bots.Select(b => b.Player.Car.Handicap)], said, moves);
    }

    [Fact]
    public void SixBotsOfMixedSkillForFiveMinutesWithTheRuleOffAndOn()
    {
        if (HostTests.InstallDir is not { } install) return;
        var off = Play(install, HandicapMode.Off, Minutes);
        var auto = Play(install, HandicapMode.Auto, Minutes);
        output.WriteLine($"off:  kills {string.Join(",", off.Kills)} deaths {string.Join(",", off.Deaths)}");
        output.WriteLine($"auto: kills {string.Join(",", auto.Kills)} deaths {string.Join(",", auto.Deaths)} levels {string.Join(",", auto.Levels)} moves {string.Join(" ", auto.Said)}");
        // the rule off: every level stays 0, and the same seed plays the same game twice
        Assert.All(off.Levels, l => Assert.Equal(0, l));
        Assert.Equal(off.Kills, Play(install, HandicapMode.Off, Minutes).Kills);
        // the rule on: levels moved, each said once as it moved; a harder level only while over the table's mean and an easier one only
        // while under it (the rule reads the standings, not the skill: at 28.5 s the two best bots met, and the one killed, a kill under
        // the mean, went to +1 until 150 s); over the game the strong pair taken hardest
        Assert.NotEmpty(auto.Said);
        Assert.Equal(auto.Said.Count, auto.Moves.Count);
        Assert.All(auto.Moves, m => Assert.True(m.Level == 0 || (m.Level < 0) == (m.Over > 0), $"level {m.Level} at {m.Over:+0.00;-0.00} over the mean"));
        Assert.All(auto.Said.Where(s => s.Contains(":-")), s => Assert.StartsWith("1:", s));
        Assert.True(auto.Levels[0] + auto.Levels[1] < System.Math.Min(auto.Levels[2] + auto.Levels[3], auto.Levels[4] + auto.Levels[5]), string.Join(",", auto.Levels));
        Assert.All(auto.Levels, l => Assert.InRange(l, -3, 3));
        // pinned: the numbers of this seed on a stock install (a change here is a change of the rule or of the bots' game; re-pinned for
        // a level a net kill with the damage taken scaled too, on play, whose turret fix had changed the bots' game; re-pinned on the
        // stock install, whose Piranha is not the one an install's garage saved into addon\, which the first pins were measured with)
        Assert.Equal(auto.Said, Play(install, HandicapMode.Auto, Minutes).Said);
        if (!MathPlatform.LibmAsRecorded) return;   // the pins below are of Windows x64's sin and cos: another C library plays the bots' game out differently
        Assert.Equal([2, 4, 0, 2, 0, 0], auto.Kills);
        Assert.Equal([0, -2, 1, 0, 1, 0], auto.Levels);
        Assert.Equal(["1:-1", "1:+1", "1:0", "3:+1", "1:-1", "5:+1", "1:0", "1:-2"], auto.Said);
        Assert.Equal([2, 2, 1, 1, 1, 1], off.Kills);
    }
}
