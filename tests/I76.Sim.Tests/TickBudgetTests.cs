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
    /// Recorded again on 2026-11-26 (main 1cb0c5ce) on a stock install: the first record was made on an install whose addon\
    /// held garage-saved copies of vppirna1.vcf and the like (the player's car of these missions, not in the install identity),
    /// and every later change to what the sim computes moves these (the upstream merge of 2026-11-18 moved T05's and T06's at
    /// tick 0). A change that is meant to leave the sim alone keeps them; one that changes it on purpose records them again.
    /// </summary>
    private const int Ticks = 1100;

    private static readonly Dictionary<string, (ulong Install, ulong Chain, ulong[] Every200)> Recorded = new()
    {
        ["t01"] = (0x8b239626afba8769UL, 0x57ec2406c05bc3f9UL, [0x635a94d56429fd61UL, 0xf4436dda9d6b9abeUL, 0x6df801b9df77b556UL, 0x85523977b9b31553UL, 0x2c1209d252459f07UL, 0x2fb785ce410f5194UL]),
        ["t05"] = (0xa723cc84a0765255UL, 0x5d862317bca74f5eUL, [0x56b8dfa360f72f25UL, 0x57d690f2f5c71bccUL, 0x97e168f622ce1d0bUL, 0x56c6d2753ebc0720UL, 0x4297863c1d617558UL, 0x0ee14c52975ac3aeUL]),
        ["t06"] = (0x60c4190c3a0bd095UL, 0x4894692ca71a7996UL, [0xbfb679933afa151aUL, 0x9874c5d7fc2546b3UL, 0x94530efb8c292b55UL, 0xdc78f5389d65c653UL, 0xf503e6be6bb02a49UL, 0xfb72b31327b9aa59UL]),
    };

    /// <summary>
    /// The cars the hashes were recorded with (<see cref="Cars"/>): the install identity is the archive and the mission's files,
    /// and a car the garage saved into addon\ (vppirna1.vcf and the like) stands in for the archive's without changing it.
    /// </summary>
    private const ulong RecordedCars = 0x4ffc9a5da479848dUL;

    /// <summary>An FNV-1a hash over every car file the VFS resolves, by name and bytes.</summary>
    internal static ulong Cars(AssetVfs vfs)
    {
        ulong h = 14695981039346656037UL;
        void Add(ReadOnlySpan<byte> b) { foreach (byte x in b) { h ^= x; h *= 1099511628211UL; } }
        // (the files on disk and in the archive: not AssetVfs.Addon, which the process's other tests fill as they go)
        foreach (var n in vfs.Names.Where(n => n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase) && vfs.Locate(n) is not AssetSource.Memory).Select(n => n.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal))
        {
            Add(System.Text.Encoding.ASCII.GetBytes(n));
            Add(vfs.Read(n));
        }
        return h;
    }

    private static (SimSession Session, MissionSetup Setup)? Mission(string name)
    {
        if (InstallDir is not { } dir) return null;
        var install = new GameInstall(dir);
        if (install.FindMission(name) is not { } path) return null;
        if (SessionBuild.InstallIdentity(install, name) != Recorded[name].Install) return null;   // not the install the hashes are of
        var vfs = new AssetVfs(install);
        if (Cars(vfs) != RecordedCars) return null;   // nor its cars
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
