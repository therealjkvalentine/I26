using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Mission;
using I76.Sim.Ai;
using I76.Sim.Fsm;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Session;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// The smarter opponents (the play fork's; <see cref="SmartAi"/>, docs/deviations.md "Smarter opponents"): the rule's words, the
/// original's AI untouched with the rule off, each behaviour on cars placed by hand on a flat 640 m tile laid into M01's empty grid
/// (as the headless probe that measured them lays it), and no change to what a car is with the rule on.
/// </summary>
public class SmartAiTests(ITestOutputHelper output)
{
    // ---- the rule's words ----

    [Fact]
    public void TheRuleReadsNamesGroupsAndNumbers()
    {
        Assert.Equal(SmartAi.None, SmartAiNames.Parse("", out _));
        Assert.Equal(SmartAi.None, SmartAiNames.Parse("off", out _));
        Assert.Equal(SmartAi.GunLay | SmartAi.Settle, SmartAiNames.Parse("t", out _));
        Assert.Equal(SmartAi.GunLay | SmartAi.Settle, SmartAiNames.Parse("gun_lay, settle", out _));
        Assert.Equal(SmartAi.All, SmartAiNames.Parse("all", out _));
        Assert.Equal((SmartAi)3, SmartAiNames.Parse("3", out _));
        Assert.Equal((SmartAi)1, SmartAiNames.Parse("5", out _));
        Assert.Equal(SmartAi.All, SmartAiNames.Parse("0xffffffff", out _));
        Assert.Equal(SmartAi.Settle, SmartAiNames.Parse("settle,bogus", out var bad));
        Assert.Equal(["bogus"], bad);
        Assert.Equal("off", SmartAiNames.Describe(SmartAi.None));
        Assert.Equal("gun_lay,settle", SmartAiNames.Describe(SmartAi.GunLay | SmartAi.Settle));
        foreach (var (name, bit) in SmartAiNames.Bits) Assert.Equal(bit, SmartAiNames.Parse(SmartAiNames.Describe(bit), out _));
    }

    // ---- the rig: a melee on a flat tile ----

    private sealed record Rig(SimSession Session, MissionSetup Setup, ScriptHost Host, Vehicle Player, List<Entity> Ais, Vector2 Centre);

    /// <summary>
    /// M01 as a single-player melee with <paramref name="roster"/>, its stock grid's empty tile (row 60, column 10) laid flat at 5 m
    /// (or by <paramref name="height"/>, metres at a point), the opponents on tier <paramref name="tier"/>.
    /// </summary>
    private static Rig? Make(string[] roster, SmartAi rule = SmartAi.None, Func<float, float, float>? height = null, int tier = 3, uint seed = 1)
    {
        if (SnapshotRig.Install is not { } dir) return null;
        var l = new SnapshotRig.Loaded(dir);
        var w = new MissionWorld(l.Vfs, l.Asm, l.Install.FindMission("m01")!);
        const int T = TerrainTiles.Tile, row = 60, col = 10;
        if (w.Terrain.TileAt(row, col) == MissionFile.ZmapEmpty && w.Terrain.AddTile(row, col, 0) < 0) return null;
        int sx0 = col * T, sz0 = row * T;
        for (int j = 0; j < T; j++)
            for (int i = 0; i < T; i++)
            {
                float x = (sx0 + i) * 5f, z = (sz0 + j) * 5f;
                w.Terrain.SetSample(sx0 + i, sz0 + j, (ushort)System.Math.Clamp((int)MathF.Round((height?.Invoke(x, z) ?? 5f) * 10f), 0, 65535));
            }
        w.ForgetOutside();
        var setup = MissionSetup.Build(l.Vfs, w, l.Asm, "vppirna1.vcf", melee: roster.ToList(), rand: new CrtRand(seed), smartAi: rule);
        var melee = setup.Melee!;
        melee.Start();
        var s = new SimSession(setup, SessionOptions.SinglePlayer, SimRate.R20);
        var player = setup.World.Player!;
        s.AddDriver(player);
        var host = melee.Host;
        var ais = host.Entities.Where(e => e.Vehicle is { } v && v != player).ToList();
        foreach (var e in ais) { host.SetSkill(e, tier, tier); e.Aggression = tier - 1; }
        // the melee orders nobody: each test orders its cars itself, after every tick, as the melee's frame would
        ((List<Entity>)typeof(MeleeGame).GetField("_opponents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(melee)!).Clear();
        return new Rig(s, setup, host, player, ais, new Vector2((sx0 + 64) * 5f, (sz0 + 64) * 5f));
    }

    /// <summary>A car on the ground at <paramref name="at"/>, heading <paramref name="heading"/> (rad, 0 along +z), at <paramref name="speed"/>.</summary>
    private static void Place(Rig r, Vehicle v, Vector2 at, float heading, float speed)
    {
        var g = r.Setup.World.Ground;
        float H(double x, double z) => g.BareHeight(x, z);
        var fwd2 = new Vector3(MathF.Sin(heading), 0, MathF.Cos(heading));
        float dhx = (H(at.X + 1, at.Y) - H(at.X - 1, at.Y)) / 2, dhz = (H(at.X, at.Y + 1) - H(at.X, at.Y - 1)) / 2;
        var up = Vector3.Normalize(new Vector3(-dhx, 1, -dhz));
        var fwd = Vector3.Normalize(fwd2 - up * Vector3.Dot(fwd2, up));
        v.Pos = new Double3(at.X, H(at.X, at.Y), at.Y);
        v.Rot = new Rows(Vector3.Cross(up, fwd), up, fwd);
        v.Vel = fwd * speed; v.Speed = speed;
        var (gh, gn) = g.Query(at.X, H(at.X, at.Y), at.Y);
        v.GroundNormal = gn; v.GroundHeight = gh; v.NormalLoad = gn.Y * gn.Y * gn.Y * 9.8f;
        VehicleStep.PrimeWheels(v, g);
    }

    private static float HeadingOf(Vector3 f) => MathF.Atan2(f.X, f.Z);

    /// <summary>Every field of an object, as text.</summary>
    private static string Fields(object o) =>
        string.Join(";", o.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Select(f => $"{f.Name}={f.GetValue(o)}"));

    /// <summary>One tick, the player parked (or <paramref name="input"/>), then each of <paramref name="orders"/> attacking its target.</summary>
    private static void Step(Rig r, params (Entity Car, Entity Target)[] orders) => Step(r, new CarInput { Buttons = CarButtons.EBrake }, orders);

    private static void Step(Rig r, CarInput input, params (Entity Car, Entity Target)[] orders)
    {
        r.Session.Tick([input]);
        r.Session.Combat?.Events.Clear();
        foreach (var (car, target) in orders) if (!car.Dead) r.Host.Order(car, target, AiOrder.Attack, null, 0);
    }

    private static int Ticks(float seconds) => (int)(seconds / SimRate.R20.TickSeconds);

    // ---- off is the original's ----

    /// <summary>With the rule off, an opponent fighting another for a minute never touches the smarter opponents' state.</summary>
    [Fact]
    public void OffTheFightNeverTouchesTheSmartState()
    {
        if (Make(["vppirna1.vcf", "vppirna1.vcf"]) is not { } r) return;
        var (a, b) = (r.Ais[0], r.Ais[1]);
        Place(r, a.Vehicle!, r.Centre, 0, 0);
        Place(r, b.Vehicle!, r.Centre + new Vector2(30, 80), 2f, 0);
        for (int t = 0; t < Ticks(60); t++) Step(r, (a, b), (b, a));
        foreach (var e in r.Ais)
        {
            Assert.Equal(SmartAi.None, e.SmartAi);
            Assert.False(e.LayActive);
            Assert.Equal(-1f, e.LayAlignedAt);
            Assert.Equal(0f, e.ProgressTime);
            Assert.False(e.Unsticking);
            Assert.Equal(-1f, e.BlockedUntil);
            Assert.Equal(-100f, e.LastDropAt);
            Assert.Equal(-1f, e.HoldLineUntil);
        }
    }

    /// <summary>
    /// What a car is (its physics, wheels, engine, armour and weapons) does not depend on the rule: the smarter opponents change only
    /// what the AI decides (the research's "no hidden cheats").
    /// </summary>
    [Fact]
    public void TheRuleChangesNothingACarIs()
    {
        if (Make(["vppirna1.vcf", "vcpolic1.vcf", "vdstag1.vcf"]) is not { } off || Make(["vppirna1.vcf", "vcpolic1.vcf", "vdstag1.vcf"], SmartAi.All) is not { } on) return;
        Assert.Equal(SmartAi.All, on.Host.SmartAi);
        for (int i = 0; i < off.Ais.Count; i++)
        {
            Vehicle a = off.Ais[i].Vehicle!, b = on.Ais[i].Vehicle!;
            Assert.Equal(Fields(a.P), Fields(b.P));
            Assert.Equal(a.Wheels.Select(w => (w.Grip, w.Rolling)), b.Wheels.Select(w => (w.Grip, w.Rolling)));
            Assert.Equal(a.Engine.Force, b.Engine.Force);
            Assert.Equal(a.Health!.Chassis, b.Health!.Chassis);
            Assert.Equal(a.Armory!.Weapons.Select(w => (w.Def.Speed, w.Damage, w.Ammo, w.MaxHp)), b.Armory!.Weapons.Select(w => (w.Def.Speed, w.Damage, w.Ammo, w.MaxHp)));
            Assert.Equal((off.Ais[i].Ai.Skill, off.Ais[i].Ai.FireSkill, off.Ais[i].Ai.FireAccuracy), (on.Ais[i].Ai.Skill, on.Ais[i].Ai.FireSkill, on.Ais[i].Ai.FireAccuracy));
        }
    }

    /// <summary>A fight with every behaviour on, saved halfway and restored into a fresh session, goes on exactly as the unbroken one.</summary>
    [Fact]
    public void AFightWithTheRuleOnRestoresFromASnapshotExactly()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = new SnapshotRig.Loaded(dir);
        var build = SnapshotRig.BuildOf(dir, "m01", "vppirna1.vcf", melee: ["vppirna1.vcf", "vppirna1.vcf", "vcpolic1.vcf"]) with { SmartAi = SmartAi.All };
        var (a, _) = SnapshotRig.Make(l, build, SimRate.R20, 5);
        for (int t = 0; t < Ticks(25); t++) SnapshotRig.Tick(a, hotkeys: false);
        var snap = a.Snapshot();
        var (b, _) = SnapshotRig.Make(new SnapshotRig.Loaded(dir), build, SimRate.R20, 5);
        b.Restore(snap);
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int t = 0; t < Ticks(20); t++) { SnapshotRig.Tick(a, hotkeys: false); SnapshotRig.Tick(b, hotkeys: false); }
        Assert.Equal(a.StateHash(), b.StateHash());
        // and the restored session's rule is the snapshot's
        Assert.Equal(SmartAi.All, Assert.IsType<MissionScript>(b.Script).Host.SmartAi);
    }

    // ---- T1 gun lay, T5 settle ----

    /// <summary>
    /// Gun lay: a target 60 m off and 50° to the right of a moving car's nose is within the cone of a tier-3 car (0.97 rad) and its
    /// reach (93 m): the car lays, the wheel turned right as the steering law turns it for that heading; one 150 m off, or behind it,
    /// is left to the tactic. Off, nothing.
    /// </summary>
    [Fact]
    public void GunLaySteersTheNoseOntoTheLeadPointWithinItsConeAndReach()
    {
        if (Make(["vppirna1.vcf", "vppirna1.vcf"]) is not { } r) return;
        var (a, b) = (r.Ais[0], r.Ais[1]);
        for (int t = 0; t < Ticks(2); t++) Step(r);
        var p = r.Host.Ptable;
        bool Lays(SmartAi bits, float bearing, float range, out float steer, float speed = 12)
        {
            a.SmartAi = bits; a.LayActive = false;
            Place(r, a.Vehicle!, r.Centre, 0, 10);
            a.Vehicle!.Gear = 1;
            Place(r, b.Vehicle!, r.Centre + new Vector2(MathF.Sin(bearing), MathF.Cos(bearing)) * range, MathF.PI / 2, speed);
            r.Host.Order(a, b, AiOrder.Attack, null, 0);
            a.LayActive = false; a.Vehicle!.Steer = 0;
            p.GunLay(a, b);
            steer = a.Vehicle!.Steer;
            return a.LayActive;
        }
        Assert.True(Lays(SmartAi.GunLay, 0.87f, 60, out float s1));
        // the steering law's own turn toward that side (AiDriver.Steer writes −error × gain: right is negative)
        Assert.True(s1 < -0.05f, $"steer {s1}");
        Assert.True(Lays(SmartAi.GunLay, -0.87f, 60, out float s2));
        Assert.True(s2 > 0.05f, $"steer {s2}");
        Assert.False(Lays(SmartAi.GunLay, 0.87f, 150, out _));
        Assert.False(Lays(SmartAi.GunLay, MathF.PI, 60, out _));
        Assert.False(Lays(SmartAi.None, 0.87f, 60, out _));
        // on the lead point (a still target dead ahead): the settle clock starts
        Lays(SmartAi.GunLay, 0f, 60, out _, 0);
        Assert.Equal(r.Host.Now, a.LayAlignedAt);
    }

    /// <summary>Settle holds the laid fixed guns until the nose has held on the lead point 0.4 s (tier 5) to 0.2 s (tier 1); without a lay it holds nothing.</summary>
    [Fact]
    public void SettleWaitsAReactionTimeOnceTheNoseIsOn()
    {
        if (Make(["vppirna1.vcf", "vppirna1.vcf"], SmartAi.GunLay | SmartAi.Settle, tier: 5) is not { } r) return;
        var (a, b) = (r.Ais[0], r.Ais[1]);
        Place(r, a.Vehicle!, r.Centre, 0, 5);
        Place(r, b.Vehicle!, r.Centre + new Vector2(0, 50), 0, 0);
        for (int t = 0; t < Ticks(2); t++) Step(r, (a, b));
        var p = r.Host.Ptable;
        Assert.Equal(0.4f, Ptable.SettleTime(a), 3);
        a.LayActive = true; a.LayAlignedAt = r.Host.Now - 0.3f;
        Assert.True(p.SettleHolds(a));
        a.LayAlignedAt = r.Host.Now - 0.45f;
        Assert.False(p.SettleHolds(a));
        a.LayAlignedAt = -1;
        Assert.True(p.SettleHolds(a));
        a.LayActive = false;
        Assert.False(p.SettleHolds(a));
        r.Host.SetSkill(a, 1, 1);
        Assert.Equal(0.2f, Ptable.SettleTime(a), 3);
        // a car whose order is not an attack is never held
        a.LayActive = true;
        r.Host.Order(a, null, AiOrder.Sit, null, 0);
        Assert.False(p.SettleHolds(a));
    }







}
