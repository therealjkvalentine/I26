using System.Numerics;
using I76.Sim.Ai;
using I76.Sim.Missions;
using I76.Sim.Session;
using I76.Sim.Trip;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// Trip+'s three rules in a single-player trip mission (the play fork's; docs/deviations.md "Trip+"): Posse's copies of the
/// enemy cars (Co-op Scaling's copy code without a co-op game), Sharp's skill tiers, Loot up's salvage level.
/// </summary>
public class TripPlusTests(ITestOutputHelper output)
{
    private static (SimSession S, MissionSetup Setup) Make(SnapshotRig.Loaded l, string mission, TripPlusRules? rules) =>
        SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, mission) with { TripPlus = rules }, SimRate.R20, 1);

    private static void Idle(SimSession s, int ticks)
    {
        for (int i = 0; i < ticks; i++) { s.Tick(new CarInput[s.Drivers.Count]); SnapshotRig.TakeEvents(s); }
    }

    private static List<Entity> Enemies(ScriptHost h) =>
        h.Entities.Where(e => e.ExtraOf is null && e.Vehicle is { IsHeli: false, IsEmplacement: false } && e.Team == 2).ToList();

    /// <summary>T01 alone with one copy each: each of the three enemy cars has one copy beside it, which the script never sees.</summary>
    [Fact]
    public void PosseGivesEachEnemyCarItsCopiesInSinglePlayer()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var (plain, ps) = Make(l, "t01", null);
        var (s, setup) = Make(l, "t01", new TripPlusRules(1, 0, 0));
        var h = setup.Script!.Host;
        Assert.Null(h.Coop);
        Assert.NotNull(h.Solo);
        int cars = setup.World.Vehicles.Count;
        Idle(plain, 20); Idle(s, 20);
        var enemies = Enemies(h);
        Assert.Equal(3, enemies.Count);
        foreach (var e in enemies)
        {
            var x = Assert.Single(h.ExtrasOf(e));
            Assert.Equal(e.Vehicle!.Definition, x.Vehicle!.Definition);
            Assert.Equal(e.Team, x.Team);
            Assert.Empty(x.Id);
            Assert.InRange(Vector3.Distance(e.Position, x.Position), 3f, 60f);
            Assert.Equal(e.Ai.Skill, x.Ai.Skill);                 // it drives and shoots as its original
            Assert.Equal(e.Ai.FireAccuracy, x.Ai.FireAccuracy);
            Assert.DoesNotContain(x, h.AiVehicles);
            Assert.DoesNotContain(x, h.Vehicles);
        }
        Assert.Equal(cars + 3, setup.World.Vehicles.Count);
        // what the script counts is the plain mission's
        Assert.Equal(ps.Script!.Host.AiVehicles.Count(), h.AiVehicles.Count());
        Assert.Equal(ps.Script!.Host.Vehicles.Count(), h.Vehicles.Count());
        Assert.All(h.Entities.Where(e => e.Vehicle is not null && e.Team != 2 && e.ExtraOf is null), e => Assert.Empty(h.ExtrasOf(e)));
    }

    /// <summary>T02 with two copies: the objective that follows the enemy cars' deaths succeeds when the originals die, as without Trip+.</summary>
    [Fact]
    public void TheScriptsCountersFollowTheOriginals()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        int[] Run(TripPlusRules? rules, out int copies)
        {
            var (s, setup) = Make(l, "t02", rules);
            var h = setup.Script!.Host;
            setup.Damage.UnlimitedArmour = setup.Damage.UnlimitedChassis = true;
            Idle(s, 100);
            foreach (var e in Enemies(h)) { h.Destroy(e); Idle(s, 60); }
            copies = h.Entities.Count(e => e.ExtraOf is not null);
            return [.. h.Objectives.Flags];
        }
        var plain = Run(null, out int none);
        var posse = Run(new TripPlusRules(2, 0, 0), out int copies);
        output.WriteLine($"objectives {string.Join(",", plain)} / posse {string.Join(",", posse)}; {copies} copies");
        Assert.Equal(0, none);
        Assert.Equal(6, copies);
        Assert.Equal(plain, posse);
    }

    /// <summary>T09: Callisto, the car the mission is about, is not copied; as under Co-op Scaling he gets escort cars in its place.</summary>
    [Fact]
    public void TheCarTheMissionIsAboutIsNotCopied()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var (s, setup) = Make(new SnapshotRig.Loaded(dir), "t09", new TripPlusRules(1, 0, 0));
        var h = setup.Script!.Host;
        Idle(s, 100);
        var callisto = Assert.Single(Enemies(h));
        Assert.Equal("t09dl01", callisto.Name, ignoreCase: true);
        Assert.True(h.KeyCars.ContainsKey(callisto));
        Assert.DoesNotContain(h.ExtrasOf(callisto), x => x.Vehicle!.Definition == callisto.Vehicle!.Definition);
        Assert.Single(h.EscortsOf(callisto));
    }

    /// <summary>T16's van, which the mission fails on losing, takes 1/1.5 under Posse, as with Extra Enemies; without Trip+ the original's.</summary>
    [Fact]
    public void AnEscortedCarTakesLessUnderPosse()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var (_, on) = Make(l, "t16", new TripPlusRules(1, 0, 0));
        var (_, off) = Make(l, "t16", null);
        var van = on.Script!.Vm!.Actors.Where(a => a.Label.Equals("van", StringComparison.OrdinalIgnoreCase)).Select(a => (a.Handle as Entity)?.Vehicle).First(v => v is not null)!;
        Assert.Equal(1f / CoopSetup.EscortHealth, on.Damage.EscortFactor);
        Assert.True(on.Damage.Escorted!(van));
        Assert.False(on.Damage.Escorted!(on.World.Player!));
        Assert.Equal(1f, off.Damage.EscortFactor);
        Assert.Null(off.Damage.Escorted);
    }

    /// <summary>
    /// Sharp: every enemy's AI tables are one tier better than the script's setSkill (or the default 5), held at 1; the script's own
    /// values stay on the entity; a friend's are the original's.
    /// </summary>
    [Fact]
    public void SharpMovesTheEnemiesTiersAndLeavesTheFriends()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var (ps, plain) = Make(l, "t01", null);
        var (ss, sharp) = Make(l, "t01", new TripPlusRules(0, -1, 0));
        Idle(ps, 200); Idle(ss, 200);
        var ph = plain.Script!.Host; var sh = sharp.Script!.Host;
        int enemies = 0;
        foreach (var (p, s) in ph.Entities.Zip(sh.Entities))
        {
            if (s.Vehicle is null || s == sh.Player) continue;
            Assert.Equal(p.SkillA, s.SkillA);   // the script's values, as it gave them
            Assert.Equal(p.SkillB, s.SkillB);
            bool enemy = s.Team != 0 && s.Team != sh.Player!.Team;
            int tierA = Array.IndexOf(AiBlock.DriveSkill, p.Ai.Skill), tierB = Array.IndexOf(AiBlock.Accuracy, p.Ai.FireAccuracy);
            Assert.Equal(AiBlock.DriveSkill[enemy ? System.Math.Max(1, tierA - 1) : tierA], s.Ai.Skill);
            Assert.Equal(AiBlock.Accuracy[enemy ? System.Math.Max(1, tierB - 1) : tierB], s.Ai.FireAccuracy);
            if (enemy) enemies++;
            output.WriteLine($"{s.Name} team {s.Team}: tiers {tierA},{tierB} -> {Array.IndexOf(AiBlock.DriveSkill, s.Ai.Skill)},{Array.IndexOf(AiBlock.Accuracy, s.Ai.FireAccuracy)}");
        }
        Assert.True(enemies >= 3);
    }

    /// <summary>The tier offset is held to 1..5, and leaves a tier outside them (the script's 0) alone.</summary>
    [Theory]
    [InlineData(5, -1, 4)]
    [InlineData(3, -2, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(2, -4, 1)]
    [InlineData(4, 3, 5)]
    [InlineData(3, 0, 3)]
    [InlineData(0, -1, 0)]
    public void TheTierIsHeldToOneToFive(int tier, int offset, int expected) => Assert.Equal(expected, TripPlusRules.Tier(tier, offset));

    /// <summary>
    /// Loot up: the clamp reads the mission number plus the offset. A medium machine gun (from T03) salvaged in T01 comes back as
    /// a light one; with +4 it is let through, and a heavy one (from T09) is still held back.
    /// </summary>
    [Fact]
    public void LootUpRaisesTheClampsLevel()
    {
        Assert.Equal(2, new SalvagePool("t01").Level);
        Assert.Equal(6, new SalvagePool("t01", false, 4).Level);
        Assert.Equal(21, new SalvagePool("t16", false, 4).Level);
        var plain = new SalvagePool("t01");
        var loot = new SalvagePool("t01", false, 4);
        Assert.True(plain.Add("gmmedium.gdf", 10, false));
        Assert.True(loot.Add("gmmedium.gdf", 10, false));
        Assert.True(loot.Add("gmheavy.gdf", 10, false));
        Assert.Equal("gmlight.gdf", plain.Items.Single().Name);
        Assert.Equal(["gmmedium.gdf", "gclight.gdf"], loot.Items.Select(i => i.Name));
    }

    /// <summary>Rules off (null, or every value 0) build the original's mission: no copies, every car where it is.</summary>
    [Fact]
    public void RulesOffChangeNothing()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var (a, sa) = Make(l, "t01", null);
        var (b, sb) = Make(l, "t01", new TripPlusRules(0, 0, 0));
        Assert.Null(sb.Script!.Host.Solo);
        Idle(a, 400); Idle(b, 400);
        Assert.Equal(sa.World.Vehicles.Select(v => v.Pos), sb.World.Vehicles.Select(v => v.Pos));
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    /// <summary>A Trip+ session's snapshot restores into a session built by the same rules, the copies made again, and runs on alike.</summary>
    [Fact]
    public void ASnapshotWithCopiesRestoresIntoTheSameRules()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var rules = new TripPlusRules(2, -1, 4);
        var (a, sa) = Make(l, "t01", rules);
        Idle(a, 300);
        var snap = a.Snapshot();
        var (b, sb) = SnapshotRig.Make(l, SnapshotRig.BuildOf(dir, "t01") with { TripPlus = rules }, SimRate.R20, 77);
        var r = b.Restore(snap);
        Assert.Equal(sa.Script!.Host.Entities.Count(e => e.ExtraOf is not null), sb.Script!.Host.Entities.Count(e => e.ExtraOf is not null));
        Assert.NotEmpty(r.Created);
        Assert.Equal(a.StateHash(), b.StateHash());
        Idle(a, 200); Idle(b, 200);
        Assert.Equal(a.StateHash(), b.StateHash());
        var (c, _) = SnapshotRig.Make(l, SnapshotRig.BuildOf(dir, "t01"), SimRate.R20, 77);
        Assert.Throws<I76.Sim.Save.SnapshotMismatchException>(() => c.Restore(snap));
    }
}
