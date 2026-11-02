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
/// off and on, from one seed, so the behaviour is pinned: the rule moves levels, says so, the strong bots are the ones taken harder,
/// and the game stays deterministic.
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

    private sealed record Outcome(int[] Kills, int[] Deaths, int[] Levels, List<string> Said);

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
        host.Game.HandicapChanged += p => said.Add($"{Tiers[bots.FindIndex(b => b.Player == p)]}:{Handicaps.Label(p.Car.Handicap)}");
        int ticks = (int)(minutes * 60 / host.Session.TickSeconds);
        for (int t = 0; t < ticks && !host.Over; t++) host.Step();
        return new Outcome([.. bots.Select(b => b.Player.Kills)], [.. bots.Select(b => b.Player.Deaths)], [.. bots.Select(b => b.Player.Car.Handicap)], said);
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
        // the rule on: levels moved, each said once as it moved, only strong bots went harder and only weak or middling ones easier
        Assert.NotEmpty(auto.Said);
        Assert.All(auto.Said.Where(s => s.EndsWith("-1") || s.EndsWith("-2") || s.EndsWith("-3")), s => Assert.StartsWith("1:", s));
        Assert.All(auto.Said.Where(s => s.Contains(":+")), s => Assert.False(s.StartsWith("1:")));
        Assert.All(auto.Levels, l => Assert.InRange(l, -3, 3));
        // pinned: the numbers of this seed (a change here is a change of the rule or of the bots' game)
        Assert.Equal(auto.Said, Play(install, HandicapMode.Auto, Minutes).Said);
        Assert.Equal([4, 6, 0, 0, 1, 1], auto.Kills);
        Assert.Equal([-2, -2, 0, 1, 1, 0], auto.Levels);
        Assert.Equal(["1:-1", "3:+1", "1:-1", "1:-2", "5:+1", "1:-2"], auto.Said);
        Assert.Equal([4, 4, 3, 1, 0, 0], off.Kills);
    }
}
