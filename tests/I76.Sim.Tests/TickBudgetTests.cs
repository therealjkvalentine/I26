using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Vfs;
using I76.Sim.Collision;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Save;
using I76.Sim.Session;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The tick's speed work (docs/perf/tick-bench.md) changes nothing the sim computes. The missions' StateHash after every tick
/// is held to the one recorded before that work (tick-bench --hashes, the baseline build), on the install it was recorded
/// on (another install builds other missions: the tests skip there); the object grid's answers to the brute force's; and
/// the tick's steady garbage to its budget.
/// </summary>
public class TickBudgetTests
{
    private static string? InstallDir =>
        Environment.GetEnvironmentVariable("I76_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    /// <summary>
    /// Each mission from its start at 20 Hz for its first <see cref="Ticks"/> ticks (55 s), nobody at the wheel, as tick-bench
    /// plays it: the install it was recorded on (<see cref="SessionBuild.InstallIdentity"/>), an FNV-1a chain over every
    /// tick's StateHash, and the StateHash after ticks 0, 200, ..., 1000 (to say where a change first shows). The window
    /// ends before the first tick at which two machines were seen to differ on the same code (docs/perf/tick-bench.md,
    /// "Two machines"): T05's tick 1153.
    /// </summary>
    private const int Ticks = 1100;

    private static readonly Dictionary<string, (ulong Install, ulong Chain, ulong[] Every200)> Recorded = new()
    {
        ["t01"] = (0x8b239626afba8769UL, 0xa1a0b1d6cbffd45cUL, [0x06df1a5bafa27492UL, 0xa15b5506086fd218UL, 0x7fdfb1cfe8b197d3UL, 0x9168aa4757adae5bUL, 0xf8d944f3999d5644UL, 0xcb83802dc2e0470fUL]),
        ["t05"] = (0xa723cc84a0765255UL, 0x404d284d28b314e9UL, [0xa2018cb3ebbbbecdUL, 0xc60dae67e8341354UL, 0xcb97cd5c248bfff2UL, 0x25ddd16706594a91UL, 0xacb36af3a017f748UL, 0xfe51afb575cf633dUL]),
        ["t06"] = (0x60c4190c3a0bd095UL, 0xabbe5be13bf9f03cUL, [0xa29b32ccc6456dcdUL, 0x1eeeb7482f1d35c5UL, 0x9519cbdb6adaf5cfUL, 0xdb6f52557e393a47UL, 0xc06a3ec96d4d1cfaUL, 0x590b0fa0127bfc14UL]),
    };

    private static (SimSession Session, MissionSetup Setup)? Mission(string name)
    {
        if (InstallDir is not { } dir) return null;
        var install = new GameInstall(dir);
        if (install.FindMission(name) is not { } path) return null;
        if (SessionBuild.InstallIdentity(install, name) != Recorded[name].Install) return null;   // not the install the hashes are of
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var setup = MissionSetup.Build(vfs, new MissionWorld(vfs, asm, path), asm, null, melee: null, rand: new CrtRand(1));
        var session = new SimSession(setup, rate: SimRate.R20);
        if (setup.World.Player is { } p) session.AddDriver(p);
        return (session, setup);
    }

    private static void Tick(SimSession s, MissionSetup setup, CarInput[] none)
    {
        s.Tick(none);
        setup.Combat.Events.Clear();
        setup.Damage.Events.Clear();
    }

    [Theory]
    [InlineData("t01")]
    [InlineData("t05")]
    [InlineData("t06")]
    public void EveryTicksStateHashIsTheRecordedOne(string mission)
    {
        if (Mission(mission) is not { } m) return;
        var (session, setup) = m;
        var (_, chain, every) = Recorded[mission];
        CarInput[] none = [default];
        ulong h = 14695981039346656037UL;
        for (int t = 0; t < Ticks; t++)
        {
            Tick(session, setup, none);
            ulong x = session.StateHash();
            for (int i = 0; i < 8; i++) { h ^= (x >> (8 * i)) & 0xff; h *= 1099511628211UL; }
            if (t % 200 == 0) Assert.True(every[t / 200] == x, $"{mission}: tick {t}'s StateHash {x:x16}, recorded {every[t / 200]:x16}");
        }
        Assert.Equal(chain, h);
    }

    /// <summary>The tick's garbage once a mission has settled (allocation is as deterministic as the sim: the budget is the measured figure with room).</summary>
    [Theory]
    [InlineData("t01", 6 * 1024)]
    [InlineData("t05", 12 * 1024)]
    [InlineData("t06", 24 * 1024)]
    public void ASettledTickStaysInItsGarbageBudget(string mission, long budget)
    {
        if (Mission(mission) is not { } m) return;
        var (session, setup) = m;
        CarInput[] none = [default];
        for (int t = 0; t < 400; t++) Tick(session, setup, none);    // past the intro and the first-time paths
        var per = new List<long>();
        for (int t = 0; t < 400; t++)
        {
            long a = GC.GetAllocatedBytesForCurrentThread();
            Tick(session, setup, none);
            per.Add(GC.GetAllocatedBytesForCurrentThread() - a);
        }
        per.Sort();
        long median = per[per.Count / 2];
        Assert.True(median <= budget, $"{mission}: a settled tick allocates {median} bytes (median of 400), over its {budget}");
    }

    [Fact]
    public void TheObjectGridGivesEveryObjectWhoseBoundsMeetTheBoxInListOrder()
    {
        var rng = new Random(76);
        var world = new StaticWorld();
        for (int i = 0; i < 600; i++)
        {
            var c = new Vector3(rng.NextSingle() * 4000 - 2000, 0, rng.NextSingle() * 4000 - 2000);
            var half = new Vector3(rng.NextSingle() * 40 + 0.5f, 3, rng.NextSingle() * 40 + 0.5f);
            world.AddBox($"b{i}", 2, c - half, c + half);
        }
        // one huge, one far outside the others
        world.AddBox("huge", 2, new Vector3(-3000, 0, -3000), new Vector3(3000, 5, 3000));
        world.AddBox("far", 2, new Vector3(90000, 0, 90000), new Vector3(90010, 5, 90010));
        var into = new int[4];
        for (int q = 0; q < 2000; q++)
        {
            if (q == 1000)
            {
                // a placement's bounds move (as a collapse or a refresh moves them): the next answer follows
                foreach (var o in world.Objects.Take(50)) { o.Centre += new Vector3(700, 0, -300); o.Radius *= 1.5f; }
                StaticObject.BoundsChanged();
            }
            float x = rng.NextSingle() * 5000 - 2500, z = rng.NextSingle() * 5000 - 2500, w = rng.NextSingle() * 60, d = rng.NextSingle() * 60;
            if (q % 97 == 0) (x, z) = (90005, 90005);
            int n = world.Near(x, x + w, z, z + d, ref into);
            var want = Enumerable.Range(0, world.Objects.Count).Where(i =>
            {
                var o = world.Objects[i];
                return !(o.Centre.X + o.Radius < x || o.Centre.X - o.Radius > x + w || o.Centre.Z + o.Radius < z || o.Centre.Z - o.Radius > z + d);
            }).ToList();
            Assert.Equal(want, into.Take(n).ToList());
        }
        // a box that is not finite gets every object
        Assert.Equal(world.Objects.Count, world.Near(float.NaN, 0, 0, 1, ref into));
    }
}
