using System.Numerics;
using I76.Formats.Mission;
using I76.Formats.Vfs;
using I76.Sim.Damage;
using I76.Sim.Fsm;
using I76.Sim.Missions;
using I76.Sim.Session;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;
using CoopEscorts = I76.Sim.Fsm.CoopEscorts;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>Co-op's escorted cars (docs/multiplayer.md "Escorted cars"): the actors whose death fails the mission, and the damage they take.</summary>
public class CoopEscortTests(ITestOutputHelper output)
{
    /// <summary>Every stock mission's actors whose death fails it, by label and team, as the script analysis finds them.</summary>
    internal static Dictionary<string, string> Survey(ITestOutputHelper? output = null)
    {
        var found = new Dictionary<string, string>();
        foreach (var dir in new[] { SnapshotRig.Install, SnapshotRig.NitroInstall })
        {
            if (dir is null) continue;
            var install = new GameInstall(dir);
            foreach (var m in SnapshotRig.Missions(dir))
            {
                if (install.FindMission(m) is not { } path || new MissionFile(path) is var mf && mf.ReadFsmProgram() is not { Code.Length: > 0 } p) continue;
                var objs = mf.PlacedObjects();
                PlacedObject? Of(int a) => a < 0 || a >= p.Actors.Count ? null
                    : objs.LastOrDefault(o => o.Raw.Length >= 8 && o.Raw.AsSpan(0, 8).SequenceEqual(p.Actors[a].InstanceId.AsSpan(0, System.Math.Min(8, p.Actors[a].InstanceId.Length))));
                var list = CoopEscorts.Analyze(p).Select(a => Of(a) is { } o ? $"{p.Actors[a].Label}({(o.IsPlayer ? "player" : $"team{(o.ObjectFlags >> 16) & 7}")})" : $"{p.Actors[a].Label}(?)");
                found[m] = string.Join(",", list);
                output?.WriteLine($"{m}: {found[m]}");
            }
        }
        return found;
    }

    /// <summary>The script analysis finds the cars the stock missions fail on losing (and the player, whose death fails every one).</summary>
    [Fact]
    public void TheEscortsAreFoundByTheirBytecode()
    {
        var found = Survey(output);
        if (SnapshotRig.Install is not null)
        {
            Assert.Equal("groove(player),taurus(team1),wagon(team0)", found["t01"]);
            Assert.Equal("user(player),bus(team1)", found["s05"]);
            Assert.Equal("user(player),taurus(team1)", found["t03"]);
            Assert.Equal("user(player),van(team1)", found["t16"]);
            Assert.Contains("skeeter(team1)", found["t05"]);
            // T09's Callisto is to be disabled, not killed: an enemy the analysis finds and the escorts leave out
            Assert.Contains("calisto(team2)", found["t09"]);
            // T15's death waits for the radio before it fails
            Assert.Equal("user(player)", found["t15"]);
            foreach (var m in new[] { "t04", "t07", "t17", "s01", "s06", "s07", "a01" }) Assert.DoesNotContain("team", found[m]);
            // structures the script guards (no vehicle: the escorts leave them alone)
            Assert.Contains("postoffice(team0)", found["s04"]);
            Assert.Contains("oil20(team0)", found["s03"]);
        }
        if (SnapshotRig.NitroInstall is not null)
        {
            Assert.Equal("user(player),bus1(team1),bus2(team1),bus3(team1),taurus(team1)", found["p08"]);
            Assert.Contains("escape(team1)", found["p19"]);
        }
    }

    internal static (SimSession S, MissionSetup Setup) Make(SnapshotRig.Loaded l, string mission, int humans, int extra, uint seed = 1) =>
        SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, mission, coop: CoopSetup.Of(humans) with { ExtraEnemies = extra }), SimRate.R20, seed);

    /// <summary>Every stock mission's escorts as a co-op build with Extra Enemies finds them: the actors' vehicles, by label and car file.</summary>
    internal static Dictionary<string, List<(string Label, string Car, int Class)>> Escorts(ITestOutputHelper? output = null)
    {
        var found = new Dictionary<string, List<(string, string, int)>>();
        foreach (var dir in new[] { SnapshotRig.Install, SnapshotRig.NitroInstall })
        {
            if (dir is null) continue;
            var l = new SnapshotRig.Loaded(dir);
            foreach (var m in SnapshotRig.Missions(dir))
            {
                if (l.Install.FindMission(m) is not { } path || new MissionFile(path).ReadFsmProgram() is not { Code.Length: > 0 }) continue;
                var (_, setup) = Make(l, m, 2, 1);
                if (setup.Script?.Vm is not { } vm) continue;
                var set = setup.Script.Host.Escorts(vm);
                found[m] = [.. vm.Actors.Where(a => a.Handle is Entity e && set.Contains(e)).Select(a => (a.Label, ((Entity)a.Handle!).Vehicle!.Definition ?? "", (int)((Entity)a.Handle!).Vehicle!.ObjectClass)).Distinct()];
                l.Worlds.Clear();
                output?.WriteLine($"{m}: {string.Join(", ", found[m].Select(x => $"{x.Item1} ({x.Item2}, class {x.Item3})"))}");
            }
        }
        return found;
    }

    /// <summary>The escorts are the vehicles among them: the friends' and neutrals' cars and helicopters, no human, enemy or structure.</summary>
    [Fact]
    public void TheEscortsAreTheirVehicles()
    {
        var found = Escorts(output);
        string Labels(string m) => string.Join(",", found[m].Select(x => x.Label.ToLowerInvariant()).Order());
        if (SnapshotRig.Install is not null)
        {
            Assert.Equal("taurus", Labels("t01"));
            Assert.Equal("bus", Labels("s05"));
            Assert.Equal("van", Labels("t16"));
            Assert.Equal("", Labels("t09"));
            Assert.Equal("", Labels("s03"));
        }
        if (SnapshotRig.NitroInstall is not null)
            Assert.Equal("bus1,bus2,bus3,taurus", Labels("p08"));
    }

    private static Vehicle Actor(MissionSetup setup, string label) =>
        setup.Script!.Vm!.Actors.Where(a => a.Label.Equals(label, StringComparison.OrdinalIgnoreCase)).Select(a => (a.Handle as Entity)?.Vehicle).First(v => v is not null)!;

    /// <summary>The packet of the identical attack: a 20-point round on the car's front.</summary>
    private static DamagePacket Round()
    {
        var p = new DamagePacket { Mask = 1, Kind = DamageKind.Round };
        p.Ch[0] = 20;
        return p;
    }

    /// <summary>How many of the same rounds kill <paramref name="v"/> (the cap when it lives through them all).</summary>
    internal static int RoundsToKill(MissionSetup setup, Vehicle v, int cap = 20000)
    {
        for (int n = 1; n <= cap; n++)
        {
            setup.Damage.Apply(v, Round(), new Vector3(0, 0.5f, 2f));
            if (DamageModel.IsDead(v)) return n;
        }
        return cap;
    }

    /// <summary>
    /// An escort under the same fire lasts 1.5 times as long with Extra Enemies on (any level); off, as the original. What
    /// reaches the car is exactly two thirds of every round (the fractions carried); the rounds it dies at depend on which
    /// part the rand stream picks to fail, so over eight seeds the mean is half again the original's within a few percent.
    /// </summary>
    [Theory]
    [InlineData("t01", "taurus")]
    [InlineData("s05", "bus")]
    [InlineData("t16", "van")]
    [InlineData("t05", "skeeter")]
    public void AnEscortLastsHalfAgainAsLongWithExtraEnemies(string mission, string label)
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        foreach (int extra in new[] { 1, 3 })
        {
            float off = 0, on = 0;
            for (uint seed = 1; seed <= 8; seed++)
            {
                var (_, a) = Make(l, mission, 2, 0, seed);
                Assert.Null(a.Damage.Escorted);
                off += RoundsToKill(a, Actor(a, label));
                var (_, b) = Make(l, mission, 2, extra, seed);
                var v = Actor(b, label);
                Assert.True(b.Damage.Escorted!(v), $"{mission} {label}");
                if (seed == 1)
                {
                    // what reaches the car: 30 rounds of 20 points arrive as 400
                    int taken = 0;
                    b.Damage.Damaging += (car, p) => { if (car == v) taken += p.Ch[0]; };
                    for (int i = 0; i < 30; i++) b.Damage.Apply(v, Round(), new Vector3(0, 0.5f, 2f));
                    Assert.Equal(400, taken);
                    (_, b) = Make(l, mission, 2, extra, seed);
                    v = Actor(b, label);
                }
                on += RoundsToKill(b, v);
            }
            output.WriteLine($"{mission} {label}: {off / 8:0.0} rounds off, {on / 8:0.0} with +{extra} (x{on / off:0.00})");
            Assert.InRange(on / off, 1.4f, 1.7f);
        }
    }

    /// <summary>The humans, an enemy and a car the mission does not fail on take what they took; single player is the original's.</summary>
    [Fact]
    public void OnlyEscortsAreSpared()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var (_, off) = Make(l, "t01", 2, 0);
        var (_, on) = Make(l, "t01", 2, 2);
        // the second seat's car, and an enemy car
        Assert.Equal(RoundsToKill(off, off.Humans[1]), RoundsToKill(on, on.Humans[1]));
        Assert.Equal(RoundsToKill(off, Actor(off, "enemy1")), RoundsToKill(on, Actor(on, "enemy1")));
        Assert.False(on.Damage.Escorted!(Actor(on, "enemy1")));
        Assert.False(on.Damage.Escorted!(on.Humans[0]));
        Assert.Equal(1f / CoopSetup.EscortHealth, on.Damage.EscortFactor);
        // single player: no co-op, no escorts, whatever the mission
        var (_, solo) = SnapshotRig.Make(l, SnapshotRig.BuildOf(dir, "t01"), SimRate.R20, 1);
        Assert.Null(solo.Damage.Escorted);
        Assert.Equal(1f, solo.Damage.EscortFactor);
        // T09's Callisto is the enemy's: left alone
        var (_, t09) = Make(l, "t09", 2, 2);
        Assert.False(t09.Damage.Escorted?.Invoke(Actor(t09, "calisto")) ?? false);
    }

    /// <summary>The factor is the build's: a snapshot restored into a fresh build of the same game carries on the same; another level is another build.</summary>
    [Fact]
    public void TheEscortFactorFollowsTheBuild()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var (a, sa) = Make(l, "t01", 2, 1);
        for (int i = 0; i < 40; i++) { a.Tick(new CarInput[a.Drivers.Count]); SnapshotRig.TakeEvents(a); }
        // half the escort's health gone, the fractions carried mid-way
        var ta = Actor(sa, "taurus");
        for (int i = 0; i < 7; i++) sa.Damage.Apply(ta, Round(), new Vector3(0, 0.5f, 2f));
        var snap = a.Snapshot();
        var (b, sb) = Make(l, "t01", 2, 1);
        b.Restore(snap);
        Assert.Equal(a.StateHash(), b.StateHash());
        var tb = Actor(sb, "taurus");
        for (int i = 0; i < 9; i++) { sa.Damage.Apply(ta, Round(), new Vector3(0, 0.5f, 2f)); sb.Damage.Apply(tb, Round(), new Vector3(0, 0.5f, 2f)); }
        Assert.Equal(ta.Health!.HealthPercent(), tb.Health!.HealthPercent());
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
