using System.Numerics;
using I76.Sim.Missions;
using I76.Sim.Session;
using I76.Sim.Vehicles;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// Co-op at a mission's start (docs/multiplayer.md "Co-op story missions"): a co-op game of 2 or 4 seats (all held, or
/// a network game's seats nobody has joined yet, CoopVacate) must not end, warn of leaving the map or put a human back
/// where the single player's run of the same mission does not.
/// </summary>
public class CoopStartSweepTests(ITestOutputHelper output)
{
    private sealed record Run(bool? Ended, float At, int Message, int Warnings, int PutBack);

    /// <summary>
    /// The mission for <paramref name="seconds"/>: single player (<paramref name="seats"/> 0) or co-op of that many seats,
    /// the ones from <paramref name="present"/> on nobody's (a network game's, CoopVacate). Every human armoured (the
    /// enemies' fire is not the test's); <paramref name="throttle"/> on seat 0, the others idle.
    /// </summary>
    private static Run Play(SnapshotRig.Loaded l, string mission, int seats, int present, float seconds, float throttle = 0, Action<string>? trace = null)
    {
        var coop = seats > 0 ? CoopSetup.Of(seats) : null;
        var (s, setup) = SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, mission, coop: coop), SimRate.R20, 1);
        var host = setup.Script!.Host;
        setup.Damage.UnlimitedArmour = setup.Damage.UnlimitedChassis = true;
        if (trace is not null) host.Cb.Started += c => trace($"{host.Now:0.00} cb {c.Name}");
        int warnings = 0, back = 0;
        if (host.Coop is { } g)
        {
            g.Happened += e => { if (e is CoopLeaveWarning) warnings++; else if (e is CoopRespawned { Why: CoopReturn.LeftMap }) back++; };
            for (int k = present; k < seats; k++) host.CoopVacate(k);
        }
        var inputs = new CarInput[s.Drivers.Count];
        if (throttle > 0) inputs[0] = new CarInput { Buttons = CarButtons.Analog, Throttle = (short)(throttle * 32767) };
        int ticks = s.Rate.TicksFor(seconds);
        for (int i = 0; i < ticks && host.End is null; i++) { s.Tick(inputs); SnapshotRig.TakeEvents(s); }
        return new Run(host.End?.Success, host.End?.At ?? 0, host.End?.FailMessage ?? 0, warnings, back);
    }

    private static string Show(Run r) => r.Ended is null ? $"runs (warn {r.Warnings}, back {r.PutBack})"
        : $"{(r.Ended.Value ? "WON" : "FAILED")} at {r.At:0.0} msg {r.Message} (warn {r.Warnings}, back {r.PutBack})";

    private static void Idle(SimSession s, float seconds)
    {
        for (int i = 0; i < s.Rate.TicksFor(seconds); i++) { s.Tick(new CarInput[s.Drivers.Count]); SnapshotRig.TakeEvents(s); }
    }

    /// <summary>
    /// T17, the final duel: co-op of 2, 4 and 8 seats, full and with seats nobody joined, does not fail in its first minute.
    /// Its leash (!isWithinSqNav(chicken, user, 1000 / 1250 / 1500) → failAllObj(8, 2), "you're a coward") was taken for
    /// the leave-map routine and run per seat: an empty seat's copy, with no car to be within, read "away" and failed it.
    /// </summary>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(2, 1)]
    [InlineData(4, 4)]
    [InlineData(4, 2)]
    [InlineData(8, 2)]
    public void T17DoesNotFailAtTheStartInCoop(int seats, int present)
    {
        if (SnapshotRig.Install is not { } dir) return;
        var r = Play(new SnapshotRig.Loaded(dir), "t17", seats, present, 60, trace: output.WriteLine);
        output.WriteLine(Show(r));
        Assert.Null(r.Ended);
        Assert.Equal(0, r.Warnings);
        Assert.Equal(0, r.PutBack);
    }

    /// <summary>T17's leash is the group's: one human far from the arena fails nothing; every human gone fails it, "coward" (failAllObj(8, 2)).</summary>
    [Fact]
    public void T17sLeashFailsWhenEveryHumanHasLeftTheArena()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var (s, setup) = SnapshotRig.Make(new SnapshotRig.Loaded(dir), SnapshotRig.BuildOf(dir, "t17", coop: CoopSetup.Of(2)), SimRate.R20, 1);
        var host = setup.Script!.Host;
        setup.Damage.UnlimitedArmour = setup.Damage.UnlimitedChassis = true;
        var vm = setup.Script.Vm!;
        Assert.Single(vm.Routines.LeaveMap);   // exit1's; the leash is not one
        Vector3 Point(string name) => vm.Program.Paths.First(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Points[0];
        var chicken = Point("chicken");
        var exit = Point("exit1");
        var away = Vector3.Normalize(new Vector3(chicken.X - exit.X, 0, chicken.Z - exit.Z));
        var far = chicken + away * 1700;
        void Put(Vehicle v, Vector3 at)
        {
            setup.World.Ai.Remove(v);
            v.Pos = new Math.Double3(at.X, setup.World.Ground.Query(at.X, 500, at.Z).Height + 0.5f, at.Z);
            v.Vel = Vector3.Zero; v.Speed = 0; v.AngVel = default;
        }
        Idle(s, 15);
        var (a, b) = (setup.Humans[0], setup.Humans[1]);
        Put(b, far);
        Idle(s, 40);
        Assert.Null(host.End);
        Put(a, far + new Vector3(10, 0, 0));
        Idle(s, 3);
        Assert.Equal((false, 1), (host.End?.Success, host.End?.FailMessage));
    }

    /// <summary>With every human dead at once T17 fails on the death (failAllObj(5, 1)), as single player's, not on the leash over it.</summary>
    [Fact]
    public void T17FailsOnTheDeathWhenEveryHumanIsDead()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var (s, setup) = SnapshotRig.Make(new SnapshotRig.Loaded(dir), SnapshotRig.BuildOf(dir, "t17", coop: CoopSetup.Of(2)), SimRate.R20, 1);
        var host = setup.Script!.Host;
        Idle(s, 20);
        foreach (var h in setup.Humans) setup.Damage.Destroy(h);
        Idle(s, 2);
        Assert.Equal((false, 0), (host.End?.Success, host.End?.FailMessage));
    }

    /// <summary>
    /// Every trip mission and scenario as co-op of 2 and 4 seats (full, and with seats nobody joined), idle and with seat 0
    /// driving off, for a minute: none ends, warns of leaving the map or puts a human back where the single player's run
    /// does not, nor ends sooner or with another message.
    /// </summary>
    [Fact]
    [Trait("Load", "Sensitive")]   // 24 missions × 10 one-minute games: long for the suite; a release check runs it
    public void NoMissionEndsOrWarnsAtTheStartInCoopWhereSinglePlayerDoesNot()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var missions = Enumerable.Range(1, 17).Select(i => $"t{i:00}").Concat(["s01", "s02", "s03", "s04", "s05", "s06", "s07"]);
        var bad = new List<string>();
        foreach (var throttle in new[] { 0f, 0.7f })
            foreach (var m in missions)
            {
                var solo = Play(l, m, 0, 0, 60, throttle);
                var line = $"{m} {(throttle > 0 ? "drive" : "idle ")}: solo {Show(solo)}";
                foreach (var (seats, present) in new[] { (2, 2), (2, 1), (4, 4), (4, 2) })
                {
                    var r = Play(l, m, seats, present, 60, throttle);
                    line += $" | {seats}/{present} {Show(r)}";
                    bool differs = r.Ended is not null && (solo.Ended is null || r.At < solo.At - 1 || r.Message != solo.Message || r.Ended != solo.Ended);
                    if (throttle == 0 && (differs || r.Warnings > 0 || r.PutBack > 0))
                        bad.Add($"{m} {seats}/{present}: {Show(r)} (solo {Show(solo)})");
                }
                output.WriteLine(line);
            }
        foreach (var x in bad) output.WriteLine("DIFFERS " + x);
        Assert.Empty(bad);
    }
}
