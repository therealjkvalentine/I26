using System.Numerics;
using I76.Sim.Fsm;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Session;
using I76.Sim.Weapons;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// Co-op Scaling's copies of a car the mission is about (upstream #195; the owner's call: the copies stay, the script is linked
/// to the original alone, and the copies are the same build in paints of their own). The script's tests, references and thread
/// follow the original; a copy cannot complete or fail an objective, nor hurt the original or be named by whoShot; a copy does
/// not drive the script's path again but escorts its original; each copy wears its own respray. That is the play fork's earlier
/// rule, now the host's coop_copy_key_cars (CoopSetup.CopyKeyCars); upstream's, the default, leaves such a car one of a kind
/// (CoopKeyCarsTests, CoopEscortsTests).
/// </summary>
public class CoopExtrasScriptTests(ITestOutputHelper output)
{
    private static (SimSession S, MissionSetup Setup) Make(SnapshotRig.Loaded l, string mission, int humans, int extra) =>
        SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, mission, coop: CoopSetup.Of(humans) with { ExtraEnemies = extra, CopyKeyCars = true }), SimRate.R20, 1);

    /// <summary>A tick with the humans' keys at rest, the first seat pressing the skip key every 40 ticks (the intros end); then <paramref name="after"/>.</summary>
    private static void Tick(SimSession s, Action? after = null)
    {
        var inputs = new CarInput[s.Drivers.Count];
        if (s.Ticks % 40 == 0 && inputs.Length > 0) inputs[0] = new CarInput { Commands = CarCommands.Skip };
        s.Tick(inputs);
        SnapshotRig.TakeEvents(s);
        after?.Invoke();
    }

    private static bool Run(SimSession s, Func<bool> until, int ticks, Action? after = null)
    {
        for (int i = 0; i < ticks && !until(); i++) Tick(s, after);
        return until();
    }

    /// <summary>The humans kept 60 m off a car, still (so that the script's distance tests stay in reach and nobody rams it).</summary>
    private static void Near(MissionSetup setup, Entity car)
    {
        int k = 0;
        foreach (var s in setup.Script!.Host.Coop!.Seats)
        {
            var at = car.Position + new Vector3(60f + 12f * k++, 0, 0);
            at.Y = setup.World.Ground.BareHeight(at.X, at.Z);
            s.Car.Pos = new Double3(at.X, at.Y + 0.5f, at.Z);
            s.Car.Vel = s.Car.AngVel = Vector3.Zero;
            s.Car.Speed = 0;
        }
    }

    private static bool Succeeded(ScriptHost h, int n) => (h.Objectives.Flags[n - 1] & Objectives.Succeeded) != 0;

    /// <summary>
    /// T09 (disable Callisto's car without killing him), three humans, the cap at 3: Callisto gets two copies in paints of their
    /// own (the original keeps the mission's). They escort him instead of running his flight path. Killing one copy and
    /// disabling the other neither wins nor loses; a copy's hit on Callisto does him no damage and is no attack the script sees;
    /// a copy that shoots a human is named as Callisto by whoShot. Then disabling Callisto himself wins the objective.
    /// </summary>
    [Fact]
    public void T09sObjectiveFollowsOnlyTheOriginalAndTheCopiesWearTheirOwnPaint()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var (s, setup) = Make(l, "t09", 3, 3);
        var h = setup.Script!.Host;
        var cal = h.Entities.Single(e => e.ExtraOf is null && e.Name.StartsWith("t09dl01", StringComparison.OrdinalIgnoreCase));
        setup.Damage.UnlimitedArmour = setup.Damage.UnlimitedChassis = true;   // the humans stand still: their survival is not the test's
        Assert.True(Run(s, () => h.ExtrasOf(cal).Count() == 2, 2000), "Callisto was never given his copies");
        var copies = h.ExtrasOf(cal).ToList();

        // the same build, its own paint: the original's is the mission's; each copy a different colour, clear of the original's scheme
        var scheme = ExtraPaint.SchemeOf(l.Vfs, cal.Vehicle!.Definition!);
        output.WriteLine($"Callisto's scheme: {(scheme is { } sp ? $"hue {sp.H:0} sat {sp.S:0.00} val {sp.V:0.00} chromatic {sp.Chromatic}" : "none")}; copies {string.Join(", ", copies.Select(x => x.Vehicle!.Paint?.Primary?.Hex))}");
        Assert.True(cal.Vehicle.Paint is null || cal.Vehicle.Paint.IsNone);
        for (int k = 0; k < copies.Count; k++)
        {
            var x = copies[k];
            Assert.Equal(cal.Vehicle.Definition, x.Vehicle!.Definition);
            Assert.Equal(ScriptHost.ExtraName(cal.Name, k + 1), x.Name);
            Assert.Equal(k + 1, ScriptHost.ExtraIndex(x.Name));
            Assert.Equal(ExtraPaint.For(k + 1, null, scheme), x.Vehicle.Paint);
            if (scheme is { } sc)
                Assert.True(ExtraPaint.Clear(x.Vehicle.Paint!.Primary!.Value, new I76.Formats.Imaging.ResprayPaint.Hsv(sc.H, sc.Chromatic ? MathF.Max(sc.S, 0.3f) : MathF.Min(sc.S, 0.1f), sc.V)));
        }
        Assert.NotEqual(copies[0].Vehicle!.Paint, copies[1].Vehicle!.Paint);
        Assert.Equal(0, ScriptHost.ExtraIndex(cal.Name));

        // the intro over, Callisto drives his flight (evade on the flee path); his copies escort him, not on the path
        Assert.True(Run(s, () => !h.View.Active && cal.Order is AiOrder.Evade or AiOrder.Goto && cal.Path is not null, 3000, () => Near(setup, cal)), $"Callisto never took to his path ({cal.Order})");
        Run(s, () => false, 40, () => Near(setup, cal));
        for (int k = 0; k < copies.Count; k++)
        {
            var x = copies[k];
            output.WriteLine($"copy {k + 1}: {x.Order} {x.OrderTarget?.Name} at ({x.FollowF1}, {x.FollowF2}), {Vector3.Distance(x.Position, cal.Position):0} m from Callisto");
            Assert.Equal(AiOrder.Follow, x.Order);
            Assert.Same(cal, x.OrderTarget);
            Assert.Null(x.Path);
            Assert.Equal(ScriptHost.Station(k), (x.FollowF1, x.FollowF2));
        }

        // a copy's hit on Callisto: no damage, no attack the script sees
        float hp = cal.Health;
        var pk = new DamagePacket { Attacker = copies[1].Vehicle, Mask = 1, Kind = I76.Sim.Damage.DamageKind.Round };
        pk.Ch[0] = 400;
        setup.Damage.Apply(cal.Vehicle, pk, Vector3.Zero);
        Assert.Equal(hp, cal.Health);
        Assert.False(h.ConsumeAttacked(cal));
        // a copy that shoots a human is named as its original (no copy enters the script's actor table)
        var human = h.Coop!.Seats[1].Entity;
        h.RecordDamage(human, copies[0], ScriptHost.Hit.Shot);
        Assert.Same(cal, h.LastAttacker(human, 1));
        Assert.Same(cal, h.HumansLastAttacker(1));

        // a copy killed and the other disabled: neither wins nor loses
        var flags = h.Objectives.Flags.ToArray();
        h.Destroy(copies[0]);
        copies[1].Vehicle!.Health!.Armour[0] = 0;
        Assert.True(copies[1].Health < 32);
        Run(s, () => h.End is not null, 400, () => Near(setup, cal));
        output.WriteLine($"after a copy killed and one disabled: objectives {string.Join(",", h.Objectives.Flags)}, end {h.End}, Callisto {cal.Health:0} %");
        Assert.Null(h.End);
        Assert.Equal(flags, h.Objectives.Flags);
        Assert.False(cal.Dead);
        Assert.Contains(setup.Script.Vm!.Actors, a => ReferenceEquals(a.Handle, cal));
        Assert.DoesNotContain(setup.Script.Vm!.Actors, a => a.Handle is Entity { ExtraOf: not null });

        // Callisto disabled: the objective is won
        cal.Vehicle.Health!.Armour[0] = 0;
        Assert.True(cal.Health < 32);
        Assert.True(Run(s, () => Succeeded(h, 1), 600, () => Near(setup, cal)), $"disabling Callisto did not win: {string.Join(",", h.Objectives.Flags)}");
        Assert.True(Run(s, () => h.End is not null, 1200, () => Near(setup, cal)));
        Assert.True(h.End!.Value.Success);
    }

    /// <summary>
    /// T05 (the van escort), three humans: ENEMY6's follow of ENEMY5 (the script's own order) takes its copies along as ENEMY6's
    /// escort, not ENEMY5's; ENEMY5's copies on its goto escort ENEMY5 rather than drive the path; an attack on the van (the escort
    /// target, not a human) is the copies' attack on the van too.
    /// </summary>
    [Fact]
    public void T05sFollowAndEscortTargetsGoToTheCopiesAsTheirOwnPlan()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var (s, setup) = Make(l, "t05", 3, 3);
        var h = setup.Script!.Host;
        setup.Damage.UnlimitedArmour = setup.Damage.UnlimitedChassis = true;
        Entity Actor(string hex) => (Entity)h.FindByInstanceId(Convert.FromHexString(hex))!;
        var enemy5 = Actor("7430356679303300");
        var enemy6 = Actor("7430356679303380");
        var enemy7 = Actor("743035676f303100");
        var van = Actor("767376616e310000");
        Assert.True(Run(s, () => new[] { enemy5, enemy6, enemy7 }.All(e => h.ExtrasOf(e).Count() == 2), 400), "the enemies were not given their copies");

        // the script's lines: ENEMY5 drives its path, ENEMY6 follows it (follow(f0, f2, f2, 0, 0, -15)), ENEMY7 goes for the van
        var path = setup.Script.Vm!.PathsDebug.First(p => p.Count > 2).ToList();
        void Orders()
        {
            h.Order(enemy5, null, AiOrder.Goto, path, 50);
            h.Order(enemy6, enemy5, AiOrder.Follow, null, 0, 0, 0, -15, enemy5);
            h.Order(enemy7, van, AiOrder.Attack, null, 0);
        }
        // their own threads (T05's thread5, thread6 and thread8: script18, script19, script20) stopped, so that their sit does not
        // fight these lines each frame (an order is the AI's whole tick for the frame)
        foreach (int t in new[] { 5, 6, 8 }) setup.Script.Vm.Threads[t].Finished = true;
        var start = new[] { enemy5, enemy6 }.SelectMany(h.ExtrasOf).ToDictionary(x => x, x => Vector3.Distance(x.Position, x.ExtraOf!.Position));
        var from = enemy5.Position;
        Orders();
        Run(s, () => false, 300, Orders);
        output.WriteLine($"ENEMY5 drove {Vector3.Distance(from, enemy5.Position):0} m; the copies started {string.Join(", ", start.Values.Select(d => d.ToString("0")))} m from their originals");
        foreach (var (e, k) in h.ExtrasOf(enemy5).Select((x, i) => (x, i)))
        {
            Assert.Equal(AiOrder.Follow, e.Order);
            Assert.Same(enemy5, e.OrderTarget);
            Assert.Null(e.Path);
            Assert.Equal(ScriptHost.Station(k), (e.FollowF1, e.FollowF2));
            Assert.Equal(0, e.FollowFlag);                                   // a goto fires at nobody: nor does its escort
        }
        foreach (var e in h.ExtrasOf(enemy6))
        {
            Assert.Equal(AiOrder.Follow, e.Order);
            Assert.Same(enemy6, e.OrderTarget);                              // its own original, not the car the original follows
        }
        foreach (var e in h.ExtrasOf(enemy7))
        {
            Assert.Equal(AiOrder.Attack, e.Order);
            Assert.Same(van, e.OrderTarget);                                 // the escort target stays the van
        }
        foreach (var e in new[] { enemy5, enemy6 })
            foreach (var x in h.ExtrasOf(e))
            {
                float d = Vector3.Distance(x.Position, e.Position);
                output.WriteLine($"{x.Name}: {d:0} m from {e.Name}");
                Assert.InRange(d, 0f, 60f);
            }
        // their names and paints: numbered, each its own
        foreach (var e in new[] { enemy5, enemy6, enemy7 })
        {
            var xs = h.ExtrasOf(e).ToList();
            Assert.Equal(new[] { 1, 2 }, xs.Select(x => ScriptHost.ExtraIndex(x.Name)));
            Assert.NotEqual(xs[0].Vehicle!.Paint, xs[1].Vehicle!.Paint);
        }
    }
}
