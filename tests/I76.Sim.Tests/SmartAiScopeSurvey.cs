using System.Globalization;
using System.Numerics;
using I76.Formats.Assembly;
using I76.Sim.Fsm;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Session;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// The smarter opponents against what the player protects (docs/deviations.md "Smarter opponents"): every mission whose script fails
/// on losing an actor that is not the player and not an enemy (<see cref="CoopEscorts.Analyze"/>: an escorted car, an ally, a
/// building), run headless with the player's car kept whole and never shooting. It escorts the first protected car 25 m back along
/// its trail (the CLI's escort driver), or, with only buildings to protect, drives to the first on the AI's own goto and waits there;
/// T10 runs its lure first (as SmartAiVanToTatumTests). 24 Hz, solid cliffs (the owner's settings).
/// Measured a run: when the first protected thing died, its health at fixed times, the enemies' and allies' deaths, the outcome,
/// the damage the player took, and the attack-order seconds of the enemies on the player and on the protected things.
/// A survey, not a check: it runs only with I76_SMART_SCOPE_SURVEY set to a folder for its table (survey.tsv); I76_SMART_SCOPE_MISSIONS,
/// _RULES (';' between rules), _SEEDS and _SECONDS narrow it.
/// </summary>
public class SmartAiScopeSurvey(ITestOutputHelper output)
{
    internal sealed record Run(string Mission, string Rule, uint Seed, string Mode, float ProtDied, float[] ProtHp, int EnemiesDead, int AlliesDead,
        string Outcome, float EndAt, double PlayerDamage, double ProtDamage, double EnemyOnPlayer, double EnemyOnProt, double EnemyOnOther, double AllyOnEnemy, ulong Hash);

    internal static readonly float[] Marks = [60, 120, 240, 400];

    /// <summary>One run of <paramref name="mission"/> under <paramref name="rule"/>, the player as the summary says.</summary>
    internal static Run Drive(SnapshotRig.Loaded l, string mission, SmartAi rule, uint seed, float seconds, string? label = null, bool keep = false)
    {
        var w = new MissionWorld(l.Vfs, l.Asm, l.Install.FindMission(mission)!);
        var setup = MissionSetup.Build(l.Vfs, w, l.Asm, null, rand: new CrtRand(seed), smartAi: rule);
        setup.World.SolidCliffs = true;
        var script = setup.Script!;
        var h = script.Host;
        var session = new SimSession(setup, rate: SimRate.FromLabel(24));
        var player = setup.World.Player!;
        session.AddDriver(player);
        var vm = script.Vm!;
        var prot = vm.FailOnDeathSlots().Select(i => vm.Actors[i].Handle).OfType<Entity>()
            .Where(e => e.Vehicle != player && e.Team != 2 && e.Vehicle?.Emplacement is null).Distinct().ToList();
        var protCar = prot.FirstOrDefault(e => e.Vehicle is not null);
        var protSet = prot.ToHashSet();
        string mode = mission == "t10" ? "lure+escort" : protCar is not null ? "escort" : prot.Count > 0 ? "goto" : "idle";
        var driver = new I76.Cli.EscortDriver(25);
        // T10: the lure up the dirt track on the AI's goto, a wait, then put behind the van at 100 s (SmartAiVanToTatumTests)
        var fsm = w.Mission.ReadFsmProgram()!;
        List<Vector3> lure = [];
        if (mission == "t10")
        {
            List<Vector3> Pts(string name) => fsm.Paths.First(p => p.Name == name).Points;
            lure.Add(Pts("roadblck")[1]);
            lure.AddRange(Pts("dirtnav").Take(5));
        }
        int step = 0;
        bool moved = mission != "t10";
        var protStruct = protCar is null ? prot.FirstOrDefault() : null;
        session.FrameStarting += () =>
        {
            if (h.View.Active || h.Of(player) is not { Dead: false } me) return;
            if (mission == "t10" && step < 2)
            {
                if (step == 0) { h.Order(me, null, AiOrder.Goto, lure, 35); if (h.ConsumeArrived(me)) step = 1; }
                else { h.Order(me, null, AiOrder.Sit, null, 0); if (h.Now >= 100) step = 2; }
            }
            else if (protStruct is not null && step == 0)
            {
                var at = protStruct.Position;
                if (Vector2.Distance(new(me.Position.X, me.Position.Z), new(at.X, at.Z)) < 60) step = 1;
                else h.Order(me, null, AiOrder.Goto, [at], 30);
            }
            else if (protStruct is not null) h.Order(me, null, AiOrder.Sit, null, 0);
        };
        bool AnyHuman(Entity? t) => t?.Vehicle is { } tv && setup.World.IsHuman(tv);
        var deadAtStart = setup.Entities.Where(e => e.Dead).ToHashSet();
        float died = -1;
        var hp = Marks.Select(_ => -1f).ToArray();
        double dmg = 0, protDmg = 0, onPlayer = 0, onProt = 0, onOther = 0, allyOn = 0;
        int frames = (int)(seconds / session.TickSeconds);
        float dt = session.TickSeconds;
        for (int f = 0; f < frames && script.Result is null; f++)
        {
            if (!moved && h.Now >= 100)
            {
                moved = true;
                var fwd = new Vector3(-1, 0, 0);
                player.Pos = new Math.Double3(4560, setup.Ground.BareHeight(4560, 47715), 47715);
                player.Rot = new Math.Rows(Vector3.Cross(Vector3.UnitY, fwd), Vector3.UnitY, fwd);
                player.Vel = default;
            }
            if (player.Health is { } ph)
            {
                for (int i = 0; i < 4; i++) dmg += ph.ArmourMax[i] - ph.Armour[i] + ph.ChassisMax[i] - ph.Chassis[i];
                Array.Copy(ph.ArmourMax, ph.Armour, 4); Array.Copy(ph.ChassisMax, ph.Chassis, 4);
            }
            if (keep)
                foreach (var pc in prot)
                    if (pc.Vehicle?.Health is { } qh && !pc.Dead)
                    {
                        for (int i = 0; i < 4; i++) protDmg += qh.ArmourMax[i] - qh.Armour[i] + qh.ChassisMax[i] - qh.Chassis[i];
                        Array.Copy(qh.ArmourMax, qh.Armour, 4); Array.Copy(qh.ChassisMax, qh.Chassis, 4);
                    }
            var input = new CarInput();
            if (protCar is not null && (mission != "t10" || step >= 2) && !h.View.Active && !protCar.Dead && protCar.Vehicle is { } pv)
                input = driver.Next(input, player, pv, protCar.Path, h.Now);
            session.Tick([input]);
            setup.Combat.Events.Clear();
            setup.Damage.Events.Clear();
            for (int m = 0; m < Marks.Length; m++)
                if (hp[m] < 0 && h.Now >= Marks[m]) hp[m] = prot.Count == 0 ? -1 : prot.Min(p => p.Dead ? 0 : p.Health);
            if (died < 0 && prot.Any(p => p.Dead)) died = h.Now;
            foreach (var e in setup.Entities)
            {
                if (e.Vehicle is not { } v || v == player || e.Dead || e.Removed || e.Order is not { } o || !Ptable.IsAttack((int)o) || e.OrderTarget is not { } t) continue;
                if (e.Team == 2)
                {
                    if (AnyHuman(t)) onPlayer += dt;
                    else if (protSet.Contains(t)) onProt += dt;
                    else onOther += dt;
                }
                else if (t.Team == 2) allyOn += dt;
            }
        }
        for (int m = 0; m < Marks.Length; m++)
            if (hp[m] < 0 && prot.Count > 0) hp[m] = prot.Min(p => p.Dead ? 0 : p.Health);
        var cars = setup.Entities.Where(e => e.Vehicle is { } v && v != player && v.Emplacement is null && !deadAtStart.Contains(e)).ToList();
        int enemies = cars.Count(e => e.Team == 2 && e.Dead), allies = cars.Count(e => e.Team == 1 && e.Dead);
        string outcome = script.Result switch { true => "won", false => "failed", null => "open" };
        return new Run(mission, label ?? SmartAiNames.Describe(rule), seed, mode, died, hp, enemies, allies, outcome, h.Now, dmg, protDmg, onPlayer, onProt, onOther, allyOn, session.StateHash());
    }

    internal static string Row(Run r) => string.Join('\t', new object[] { r.Mission, r.Rule, r.Seed, r.Mode, r.ProtDied.ToString("0.0", CultureInfo.InvariantCulture),
        string.Join(',', r.ProtHp.Select(x => x.ToString("0", CultureInfo.InvariantCulture))), r.EnemiesDead, r.AlliesDead, r.Outcome, r.EndAt.ToString("0", CultureInfo.InvariantCulture),
        r.PlayerDamage.ToString("0", CultureInfo.InvariantCulture), r.ProtDamage.ToString("0", CultureInfo.InvariantCulture), r.EnemyOnPlayer.ToString("0", CultureInfo.InvariantCulture), r.EnemyOnProt.ToString("0", CultureInfo.InvariantCulture),
        r.EnemyOnOther.ToString("0", CultureInfo.InvariantCulture), r.AllyOnEnemy.ToString("0", CultureInfo.InvariantCulture), r.Hash.ToString("x16") });

    internal const string Header = "mission\trule\tseed\tmode\tprot_died\tprot_hp@60,120,240,400\tenemies_dead\tallies_dead\toutcome\tend\tplayer_dmg\tprot_dmg\tenemy_on_player_s\tenemy_on_prot_s\tenemy_on_other_s\tally_on_enemy_s\thash";

    [Fact]
    public void Survey()
    {
        if (Environment.GetEnvironmentVariable("I76_SMART_SCOPE_SURVEY") is not { Length: > 0 } outDir) return;
        Directory.CreateDirectory(outDir);
        string? only = Environment.GetEnvironmentVariable("I76_SMART_SCOPE_MISSIONS");
        var rules = (Environment.GetEnvironmentVariable("I76_SMART_SCOPE_RULES") ?? "off;all;everything;n")
            .Split(';', StringSplitOptions.RemoveEmptyEntries).Select(t => (Label: t, Bits: SmartAiNames.Parse(t, out _))).ToArray();
        int seeds = int.TryParse(Environment.GetEnvironmentVariable("I76_SMART_SCOPE_SEEDS"), out var sd) ? sd : 8;
        bool keep = Environment.GetEnvironmentVariable("I76_SMART_SCOPE_KEEP") == "1";
        float seconds = float.TryParse(Environment.GetEnvironmentVariable("I76_SMART_SCOPE_SECONDS"), CultureInfo.InvariantCulture, out var sc) ? sc : 420;
        var jobs = new List<(string Dir, string Mission, (string Label, SmartAi Bits) Rule, uint Seed)>();
        foreach (var dir in new[] { SnapshotRig.Install, SnapshotRig.NitroInstall })
        {
            if (dir is null) continue;
            foreach (var (m, list) in CoopEscortTests.Survey())
            {
                if (only is not null && !only.Split(',').Contains(m)) continue;
                if (new I76.Formats.Vfs.GameInstall(dir).FindMission(m) is null || dir == SnapshotRig.NitroInstall && SnapshotRig.Install is { } b && new I76.Formats.Vfs.GameInstall(b).FindMission(m) is not null) continue;
                // a protected actor: not the player, not an enemy (team 2)
                if (!list.Split(',').Any(a => a.Contains("(team0)") || a.Contains("(team1)"))) continue;
                foreach (var r in rules) for (uint s = 1; s <= seeds; s++) jobs.Add((dir, m, r, s));
            }
        }
        var runs = new Run?[jobs.Count];
        var locals = new System.Collections.Concurrent.ConcurrentDictionary<int, Dictionary<string, SnapshotRig.Loaded>>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = 28 }, i =>
        {
            var mine = locals.GetOrAdd(Environment.CurrentManagedThreadId, _ => []);
            var (dir, m, r, s) = jobs[i];
            if (!mine.TryGetValue(dir, out var l)) mine[dir] = l = new SnapshotRig.Loaded(dir);
            try { runs[i] = Drive(l, m, r.Bits, s, seconds, r.Label, keep); }
            catch (Exception ex) { Console.Error.WriteLine($"{m} {r.Label} {s}: {ex}"); }
        });
        var lines = new List<string> { Header };
        lines.AddRange(runs.OfType<Run>().Select(Row));
        File.WriteAllLines(Path.Combine(outDir, keep ? "survey-keep.tsv" : "survey.tsv"), lines);
        output.WriteLine($"{runs.Count(r => r is not null)} runs of {jobs.Count}");
    }
}

/// <summary>
/// The fighting behaviours only against a human's car, on the stock missions (the survey's drives, a few seeds): where the computer
/// cars fight only the player (T13's raiders while the player waits at the truckstop) the scoped rule plays exactly as the rule's
/// first form (any_target), tick for tick; where an ally fights the enemies (T03's Taurus) it plays differently.
/// </summary>
public class SmartAiPlayerOnlyTests
{
    [Fact]
    public void AgainstThePlayerAloneTheScopedRuleIsTheFirstForm()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        for (uint seed = 1; seed <= 2; seed++)
        {
            var scoped = SmartAiScopeSurvey.Drive(l, "t13", SmartAi.All, seed, 150);
            var first = SmartAiScopeSurvey.Drive(l, "t13", SmartAi.All | SmartAi.AnyTarget, seed, 150);
            Assert.True(scoped.EnemyOnPlayer > 30, $"the raiders spent {scoped.EnemyOnPlayer:0} s on the player");
            Assert.Equal(0, scoped.EnemyOnProt + scoped.AllyOnEnemy);
            Assert.Equal(first.Hash, scoped.Hash);
        }
        var t03 = SmartAiScopeSurvey.Drive(l, "t03", SmartAi.All, 1, 200);
        var t03First = SmartAiScopeSurvey.Drive(l, "t03", SmartAi.All | SmartAi.AnyTarget, 1, 200);
        Assert.True(t03.AllyOnEnemy > 0, "Taurus never fought");
        Assert.NotEqual(t03First.Hash, t03.Hash);
    }
}
