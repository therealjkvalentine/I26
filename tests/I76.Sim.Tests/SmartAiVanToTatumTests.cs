using System.Numerics;
using I76.Formats.Assembly;
using I76.Sim.Fsm;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Session;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// The smarter opponents in T10, Van to Tatum (the owner's report: the smart A.I. is "still not close enough to Skeeter's van"),
/// driven headless as the CLI's run drives it (I76_RUN_AIDRIVE "goto:roadblck:1-1+dirtnav:0-4,35;sit:100", --player-at
/// 4560,47715,270@100, I76_RUN_ESCORT t10sv01,25): the player's car, on the AI's own goto, runs to the roadblock and up the dirt track
/// to the road point that sends the cops after it (objective 2), waits, is put behind the van as it sets off for Tatum, and escorts
/// it 25 m back; 24 Hz, solid cliffs (the owner's settings), the player kept whole. The jump is not driven (the headless player
/// falls at it), so the cops that follow the player up the track are left on the mesa over the canyon road, as a player who
/// skips the jump leaves them. Measured: how soon the cops kill the van when nobody shoots them, and, with the van kept whole too,
/// the cop-seconds within 100 and 300 m of it on its way to Tatum.
/// </summary>
public class SmartAiVanToTatumTests(ITestOutputHelper output)
{
    private const int Seeds = 12;

    private sealed record Run(float VanKilled, double Near100, double Near300);

    private static Run Drive(SnapshotRig.Loaded l, SmartAi rule, uint seed, bool keepVan)
    {
        var w = new MissionWorld(l.Vfs, l.Asm, l.Install.FindMission("t10")!);
        var setup = MissionSetup.Build(l.Vfs, w, l.Asm, null, rand: new CrtRand(seed), smartAi: rule);
        setup.World.SolidCliffs = true;
        var script = setup.Script!;
        var h = script.Host;
        var session = new SimSession(setup, rate: SimRate.FromLabel(24));
        var player = setup.World.Player!;
        session.AddDriver(player);
        var fsm = w.Mission.ReadFsmProgram()!;
        List<Vector3> Pts(string name) => fsm.Paths.First(p => p.Name == name).Points;
        var lure = new List<Vector3> { Pts("roadblck")[1] };
        lure.AddRange(Pts("dirtnav").Take(5));
        var van = setup.Entities.First(e => e.Vehicle is not null && e.Name.Contains("t10sv01"));
        int step = 0;   // 0 the lure's goto, 1 sitting until 100 s, 2 escorting
        bool moved = false;
        session.FrameStarting += () =>
        {
            if (step >= 2 || h.View.Active || h.Of(player) is not { Dead: false } me) return;
            if (step == 0)
            {
                h.Order(me, null, AiOrder.Goto, lure, 35);
                if (h.ConsumeArrived(me)) step = 1;
            }
            else
            {
                h.Order(me, null, AiOrder.Sit, null, 0);
                if (h.Now >= 100) step = 2;
            }
        };
        var driver = new I76.Cli.EscortDriver(25);
        float killed = -1;
        double near100 = 0, near300 = 0;
        int frames = (int)(420f / session.TickSeconds);
        for (int f = 0; f < frames && script.Result is null; f++)
        {
            if (!moved && h.Now >= 100)
            {
                // put behind the van (heading west), as --player-at 4560,47715,270@100
                moved = true;
                var fwd = new Vector3(-1, 0, 0);
                player.Pos = new Double3(4560, setup.Ground.BareHeight(4560, 47715), 47715);
                player.Rot = new Rows(Vector3.Cross(Vector3.UnitY, fwd), Vector3.UnitY, fwd);
                player.Vel = default;
            }
            foreach (var keep in keepVan ? new[] { player, van.Vehicle! } : [player])
                if (keep.Health is { } hd) { Array.Copy(hd.ArmourMax, hd.Armour, 4); Array.Copy(hd.ChassisMax, hd.Chassis, 4); }
            var input = new CarInput();
            if (step >= 2 && !h.View.Active && !van.Dead) input = driver.Next(input, player, van.Vehicle!, van.Path, h.Now);
            session.Tick([input]);
            setup.Combat.Events.Clear();
            setup.Damage.Events.Clear();
            if (van.Dead) { killed = h.Now; break; }
            if (f % 10 != 0 || h.Now < 101 || van.Order != AiOrder.Goto) continue;
            foreach (var e in setup.Entities)
            {
                if (e.Vehicle is null || e.Dead || e.Removed || !e.Name.StartsWith("t10cp")) continue;
                var d = e.Position - van.Position;
                float xz = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
                if (xz < 100) near100 += 10 * session.TickSeconds;
                if (xz < 300) near300 += 10 * session.TickSeconds;
            }
        }
        return new Run(killed, near100, near300);
    }

    private Run[] Runs(SmartAi rule, bool keepVan)
    {
        var dir = SnapshotRig.Install!;
        var runs = new Run[Seeds];
        // one install per worker: the VFS and the assembler are not thread-safe
        Parallel.For(0, Seeds, () => new SnapshotRig.Loaded(dir), (i, _, l) => { runs[i] = Drive(l, rule, (uint)(i + 1), keepVan); return l; }, _ => { });
        output.WriteLine(keepVan
            ? $"{SmartAiNames.Describe(rule),-14} the van kept whole: cop-seconds within 100 m {runs.Average(r => r.Near100),5:0} a run, within 300 m {runs.Average(r => r.Near300),5:0}"
            : $"{SmartAiNames.Describe(rule),-14} the van killed at {runs.Average(r => r.VanKilled < 0 ? 420 : r.VanKilled),5:0} s on the mean; per seed {string.Join(" ", runs.Select(r => r.VanKilled.ToString("0")))}");
        return runs;
    }

    /// <summary>
    /// With the rule on, the cops are at least the threat to the van the original's are: they kill it no later on the mean, and are
    /// near it (with the van kept whole) at least as long. Before the fix (the cops held on the mesa by Ledge caution and the
    /// terrain costs, and the chase aimed where the player had been) the van lived longer with the rule on: 48 seeds, killed at
    /// 214 s against the original's 207, and with the climb-aware probe alone at 265 s.
    /// </summary>
    [Fact]
    public void TheCopsThreatenTheVanAtLeastAsTheOriginalsDo()
    {
        if (SnapshotRig.Install is null) return;
        static double Killed(Run[] r) => r.Average(x => x.VanKilled < 0 ? 420 : x.VanKilled);
        var off = Runs(SmartAi.None, false);
        var on = Runs(SmartAi.All, false);
        var probe = Runs(SmartAi.ClimbProbe, false);
        Assert.True(Killed(on) <= Killed(off), $"on: the van killed at {Killed(on):0} s, off {Killed(off):0}");
        Assert.True(Killed(probe) <= Killed(off) * 1.05, $"climb probe: the van killed at {Killed(probe):0} s, off {Killed(off):0}");
        var offKept = Runs(SmartAi.None, true);
        var onKept = Runs(SmartAi.All, true);
        Assert.True(onKept.Average(r => r.Near300) >= offKept.Average(r => r.Near300),
            $"on: cop-seconds within 300 m of the van {onKept.Average(r => r.Near300):0}, off {offKept.Average(r => r.Near300):0}");
    }
}
