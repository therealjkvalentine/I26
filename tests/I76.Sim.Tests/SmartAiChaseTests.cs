using System.Numerics;
using I76.Formats.Assembly;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Session;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// The smarter opponents on a chase: T14 ("Escort and defend the van to the county line"), the player's car driven headless behind
/// the van as a player escorting it would (the CLI's I76_RUN_ESCORT driver, 25 m back along the van's trail), the player and the
/// van kept whole so the chase runs to the county line, at 24 Hz with solid cliffs (the owner's settings when the report came:
/// "the smarter AI struggles to pathfind and keep up with me / the van"). Measured over the cops' and SWAT vans' attack orders from
/// the van's start (50 s) to 340 s: the mean distance to the target, the share of the time within 60 m, and the time "trapped"
/// (150 m or more from the target and the last 20 s within 30 m of one spot). The scripted cars (the van's goto, the player's
/// follow at the end) are no attack: the rule never drives them.
/// </summary>
public class SmartAiChaseTests(ITestOutputHelper output)
{
    /// <summary>
    /// Seeds 1 to 32. Eight were too few: the per-seed mean gap spreads about 60 m, and on the stock install seeds 1 to 8 put the scoped
    /// rule (On) at 384.5 m against Off's 359.8 while seeds 1 to 32 put it at 347.6 against 373.4.
    /// </summary>
    private const int Seeds = 32;

    private sealed record Chase(double Gap, double Close, double Trapped, ulong Hash);

    private static Chase Run(SnapshotRig.Loaded l, SmartAi rule, uint seed)
    {
        var w = new MissionWorld(l.Vfs, l.Asm, l.Install.FindMission("t14")!);
        var setup = MissionSetup.Build(l.Vfs, w, l.Asm, null, rand: new CrtRand(seed), smartAi: rule);
        setup.World.SolidCliffs = true;
        var script = setup.Script!;
        var h = script.Host;
        var session = new SimSession(setup, rate: SimRate.FromLabel(24));
        var player = setup.World.Player!;
        session.AddDriver(player);
        var driver = new I76.Cli.EscortDriver(25);
        var van = setup.Entities.First(e => e.Vehicle is not null && e.Name.Contains("t14sv01"));
        var spots = new Dictionary<Entity, Queue<(float T, Vector2 P)>>();
        double gap = 0, close = 0, trapped = 0, n = 0;
        int frames = (int)(340f / session.TickSeconds);
        for (int f = 0; f < frames && script.Result is null; f++)
        {
            foreach (var keep in new[] { player, van.Vehicle! })
                if (keep.Health is { } hd) { Array.Copy(hd.ArmourMax, hd.Armour, 4); Array.Copy(hd.ChassisMax, hd.Chassis, 4); }
            var input = new CarInput();
            if (!h.View.Active && !van.Dead) input = driver.Next(input, player, van.Vehicle!, van.Path, h.Now);
            session.Tick([input]);
            setup.Combat.Events.Clear();
            setup.Damage.Events.Clear();
            if (f % 10 != 0 || h.Now < 50) continue;
            foreach (var e in setup.Entities)
            {
                if (e.Vehicle is not { } v || v == player || e.Dead || e.Removed || e.Order != Fsm.AiOrder.Attack || e.OrderTarget is not { } t)
                { spots.Remove(e); continue; }
                float d = Vector3.Distance(t.Position, e.Position);
                gap += d; n++;
                if (d < 60) close++;
                var q = spots.TryGetValue(e, out var have) ? have : spots[e] = new();
                var at = new Vector2((float)v.Pos.X, (float)v.Pos.Z);
                q.Enqueue((h.Now, at));
                while (q.Peek().T < h.Now - 20) q.Dequeue();
                if (d >= 150 && h.Now - q.Peek().T >= 19.5f && q.All(s => Vector2.Distance(s.P, at) < 30)) trapped += 10 * session.TickSeconds;
            }
        }
        return new Chase(gap / System.Math.Max(n, 1), close / System.Math.Max(n, 1), trapped, session.StateHash());
    }

    private Chase[] Runs(SmartAi rule)
    {
        var dir = SnapshotRig.Install!;
        var runs = new Chase[Seeds];
        // one install per worker: the VFS and the assembler are not thread-safe
        Parallel.For(0, Seeds, () => new SnapshotRig.Loaded(dir), (i, _, l) => { runs[i] = Run(l, rule, (uint)(i + 1)); return l; }, _ => { });
        output.WriteLine($"{SmartAiNames.Describe(rule),-14} gap {runs.Average(r => r.Gap),6:0.0} m  within 60 m {100 * runs.Average(r => r.Close),5:0.0} %  trapped {runs.Average(r => r.Trapped),5:0.0} s/run  " +
            $"per seed gap {string.Join(" ", runs.Select(r => r.Gap.ToString("0")))}");
        return runs;
    }

    /// <summary>
    /// With the rule on (every behaviour, the Play Options row's On: the fighting ones only against the player's car, so the cars after
    /// the van fight as the original's and only the terrain group drives them differently) the chasers keep up at least as well as the
    /// original's: no farther on the mean, no less often within 60 m, no longer trapped. Stock install, 32 seeds: Off 373.4 m, 13.1 %,
    /// 67.9 s trapped; On 347.6 m, 16.7 %, 49.5 s; ledge caution alone 378.8 m, 12.9 %; the rule's first form (everything) 280.7 m.
    /// Ledge caution alone stays within 10 % of the original's mean gap and 2 points of its time within 60 m: its first rule (a 2.5 m
    /// rim, a metre's crest) was 362 m against 333 and 12.0 % against 15.5 on seeds 1 to 8 with one install, this one 340 m and 15.8 %;
    /// another install of the same I76.ZFS read 386 m against 360 and 13.3 % against 14.0, so the bound is loose and the rim
    /// itself is held by LedgeCautionLetsABankBe.
    /// </summary>
    [Fact]
    public void TheChasersKeepUpWithTheVanAsWellAsTheOriginals()
    {
        if (SnapshotRig.Install is null) return;
        var off = Runs(SmartAi.None);
        var on = Runs(SmartAi.All);
        var ledge = Runs(SmartAi.LedgeCaution);
        static double Gap(Chase[] r) => r.Average(c => c.Gap);
        static double Close(Chase[] r) => r.Average(c => c.Close);
        static double Trapped(Chase[] r) => r.Average(c => c.Trapped);
        Assert.True(Gap(on) <= Gap(off), $"on: mean gap {Gap(on):0.0} m, off {Gap(off):0.0}");
        Assert.True(Close(on) >= Close(off), $"on: within 60 m {Close(on):P1}, off {Close(off):P1}");
        Assert.True(Trapped(on) <= Trapped(off), $"on: trapped {Trapped(on):0.0} s, off {Trapped(off):0.0}");
        Assert.True(Gap(ledge) <= Gap(off) * 1.10, $"ledge caution: mean gap {Gap(ledge):0.0} m, off {Gap(off):0.0}");
        Assert.True(Close(ledge) >= Close(off) - 0.02, $"ledge caution: within 60 m {Close(ledge):P1}, off {Close(off):P1}");
    }
}
