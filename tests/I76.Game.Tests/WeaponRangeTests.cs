using System.Globalization;
using System.Numerics;
using I76.Formats;
using I76.Formats.Bwd2;
using I76.Formats.Tests;
using I76.Formats.Vfs;
using I76.Game.Shell;
using I76.Sim;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Trip;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;
using Xunit.Abstractions;

namespace I76.Game.Tests;

/// <summary>
/// The weapons' range in their tooltips (<see cref="Combat.RangeOf"/>, <see cref="PartTips"/>) against the simulation itself: a car
/// standing on level ground fires the install's weapon through <see cref="Combat.Frame"/> (the fire timers, the spawn with its spread,
/// the flight, the hit tests), and where the round lands or runs out, or the furthest car a flame stream burns, is measured. The
/// tooltip's number must be within 10 % of it.
/// </summary>
public class WeaponRangeTests(ITestOutputHelper output)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const float Dt = 102 / 2048f;

    private static string? NitroDir =>
        Environment.GetEnvironmentVariable("I76_NITRO_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private sealed class Sink : IDamageSink
    {
        public readonly List<(object Victim, DamagePacket P)> Hits = [];
        public void Apply(object victim, DamagePacket p, Vector3 hitLocal) => Hits.Add((victim, p));
    }

    private sealed record Rig(SimWorld World, Combat Combat, Vehicle Car, Weapon Gun, Sink Sink, AssetVfs Vfs);

    /// <summary>The car put down with its box's floor on the ground; <see cref="Settle"/> lets it come to rest on its springs.</summary>
    private static void Stand(Vehicle v, float x, float z) => v.Pos = new Double3(x, -v.BoxY[1], z);

    /// <summary>Three seconds of the vehicle physics, the cars standing on level ground in neutral: their own ride height and pitch.</summary>
    private static void Settle(SimWorld world)
    {
        for (int i = 0; i < 60; i++) world.Frame(Dt);
        foreach (var v in world.Vehicles) { v.Vel = Vector3.Zero; v.Speed = 0; v.AngVel = Vector3.Zero; }
    }

    /// <summary>
    /// <paramref name="vcf"/> on level ground with <paramref name="gdf"/> alone on its first front hardpoint of <paramref name="style"/>
    /// (any front one for a turret gizmo), the player's car.
    /// </summary>
    private static Rig Make(AssetVfs vfs, string gdf, string? vcf = "vppirna1.vcf", uint style = 1)
    {
        vcf ??= CarWith(vfs, style);
        var world = new SimWorld(new Flat(), new CrtRand(1));
        var car = VehicleFactory.Create(vfs, vcf);
        Stand(car, 0, 0);
        world.Vehicles.Add(car);
        world.Player = car;
        var g = GizmoDef.Read(gdf, vfs.Read(gdf))!;
        var hps = Hardpoint.Read(new Bwd2File(vfs.Read(VdfOf(vfs, vcf))));
        var hp = g.Class == 6
            ? hps.First(h => h.Style == 4)
            : hps.First(h => h.Facing == 1 && (g.MountsAsTurret ? h.Style is 1 or 2 or 5 : h.Style == style));
        var a = new Armory();
        var w = Armory.Mount(g, hp);
        a.Weapons.Add(w);
        a.LoadOrder.Add(w);
        a.Selected = 0;
        car.Armory = a;
        Settle(world);
        var effects = new Dictionary<string, EffectDef?>(StringComparer.OrdinalIgnoreCase);
        var combat = new Combat(world, n => effects.TryGetValue(n, out var e) ? e : effects[n] = vfs.TryRead(n) is { } d ? EffectDef.Read(n, d) : null);
        var sink = new Sink();
        combat.Damage = sink;
        return new Rig(world, combat, car, w, sink, vfs);
    }

    private static string VdfOf(AssetVfs vfs, string vcf)
    {
        var f = new Bwd2File(vfs.Read(vcf));
        return Bin.CStr(f.Data, f.First("VCFC")!.Value.PayloadOffset + 0x10, 13);
    }

    /// <summary>The first player car (vp*.vcf) with a front hardpoint of <paramref name="style"/>.</summary>
    private static string CarWith(AssetVfs vfs, uint style) =>
        vfs.Names.Where(n => n.StartsWith("vp", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)
            .First(n => { try { return Hardpoint.Read(new Bwd2File(vfs.Read(VdfOf(vfs, n)))).Any(h => h.Facing == 1 && h.Style == style); } catch { return false; } });

    private static void Pull(Rig r) => r.Car.Armory!.PlayerTriggers(false, [true]);

    /// <summary>One shot: where its round came down or ran out, along the ground from the muzzle; and the muzzle's height.</summary>
    private static (float Distance, float Height, Round Round) Shoot(Rig r, Action? afterShot = null)
    {
        Pull(r);
        r.Combat.Frame(Dt);
        var shot = r.Combat.Events.OfType<ShotFired>().First();
        var round = r.Combat.Rounds.First();
        afterShot?.Invoke();
        var last = round.Pos;
        Vector3? landed = null;
        for (int i = 0; i < 20000 && r.Combat.Rounds.Contains(round) && landed is null; i++)
        {
            last = round.Pos;
            r.Combat.Events.Clear();
            r.Combat.Frame(Dt);
            if (round.Settled) landed = round.Pos;   // the Chemical Mortar's cloud
            else if (r.Combat.Events.OfType<Impact>().FirstOrDefault(e => e.Victim is null) is { } hit) landed = hit.At;
        }
        var at = landed ?? last;
        return (new Vector2(at.X - shot.Muzzle.X, at.Z - shot.Muzzle.Z).Length(), shot.Muzzle.Y, round);
    }


    /// <summary>The tooltip the garage shows for the part of <paramref name="gdf"/>.</summary>
    private static PartTip Tip(AssetVfs vfs, string gdf, bool nitro = false)
    {
        var part = PartsDatabase.Build(vfs).ByStem(gdf) ?? throw new InvalidOperationException($"no part {gdf}");
        return new PartTips(vfs, nitro).For(part)!;
    }

    /// <summary>The distances on a tooltip's range line, in metres, in order ("Lands 13-35 m out" gives 13 and 35).</summary>
    private static float[] Metres(PartTip t) =>
        t.Range is null ? [] : [.. System.Text.RegularExpressions.Regex.Matches(t.Range, @"[0-9][0-9,]*")
            .Select(m => float.Parse(m.Value.Replace(",", ""), Inv))];

    /// <summary>The tooltip's figure against the measured one: within 10 %.</summary>
    private void Near(string what, float tooltip, float measured)
    {
        output.WriteLine(string.Create(Inv, $"{what}: tooltip {tooltip:0.#} m, measured {measured:0.0} m ({(tooltip / measured - 1) * 100:+0.0;-0.0} %)"));
        Assert.InRange(tooltip, measured * 0.9f, measured * 1.1f);
    }

    /// <summary>The tooltip's one range figure against the measured one, and its range line's wording.</summary>
    private void TipNear(AssetVfs vfs, string gdf, float measured, string starts, bool nitro = false, int figure = 0)
    {
        var t = Tip(vfs, gdf, nitro);
        output.WriteLine($"{gdf}: \"{t.Range}\"");
        Assert.StartsWith(starts, t.Range);
        Assert.EndsWith(t.Range!, t.Text);
        var m = Metres(t);
        Near(gdf, m[System.Math.Min(figure, m.Length - 1)], measured);
    }

    private static AssetVfs Base => TestInstall.Vfs;

    // ---- one per kind ----

    [InstallTheory]
    [InlineData("gmheavy.gdf")]   // 7.62mm MG
    [InlineData("gmlight.gdf")]   // 30cal MG
    [InlineData("gclight.gdf")]   // 20mm Cannon
    [InlineData("gcheavy.gdf")]   // 30mm Cannon
    [InlineData("tmlight.gdf")]   // 30cal Turret: a shorter life than the fixed gun's, at a higher speed
    public void AGunsRoundFliesAsFarAsItsTooltipSays(string gdf)
    {
        var r = Make(Base, gdf);
        var (d, h, _) = Shoot(r);
        output.WriteLine(string.Create(Inv, $"muzzle {h:0.00} m up"));
        TipNear(Base, gdf, d, "Range ");
    }

    /// <summary>Past a kilometre the tooltip says only that: a tank shell's 6 km comes down wherever the mount's last fraction of a degree sends it.</summary>
    [InstallFact]
    public void ATankShellIsOverAKilometre()
    {
        var r = Make(Base, "gtktank.gdf");
        var (d, _, _) = Shoot(r);
        output.WriteLine(string.Create(Inv, $"gtktank.gdf: measured {d:0} m"));
        Assert.True(d > PartTips.LongRange);
        Assert.Equal("Range over 1 km.", Tip(Base, "gtktank.gdf").Range);
    }

    [InstallFact]
    public void ARocketFliesAsFarAsItsTooltipSays()
    {
        var r = Make(Base, "gdumb.gdf");
        var (d, _, _) = Shoot(r);
        TipNear(Base, "gdumb.gdf", d, "Range ");
    }

    [InstallTheory]
    [InlineData("gsheat.gdf")]    // Aim-Nein: heat seeker
    [InlineData("gsradar.gdf")]   // DrRadar: radar missile, its 8 s life
    [InlineData("gscherub.gdf")]
    public void AMissileWithNoTargetFliesAsFarAsItsTooltipSays(string gdf)
    {
        var r = Make(Base, gdf);
        var (d, _, _) = Shoot(r);
        TipNear(Base, gdf, d, "Range ");
    }

    /// <summary>A guided missile takes a car ahead for its target inside its tooltip's lock range, and none past it.</summary>
    [InstallTheory]
    [InlineData("gsheat.gdf")]
    [InlineData("gsradar.gdf")]
    public void AMissileLocksWithinItsTooltipsLockRange(string gdf)
    {
        var tip = Tip(Base, gdf);
        Assert.Contains("; locks on within ", tip.Range);
        float lockAt = Metres(tip)[1];
        foreach (var (at, locks) in new[] { (lockAt * 0.97f, true), (lockAt * 1.03f, false) })
        {
            var r = Make(Base, gdf);
            var target = VehicleFactory.Create(Base, "vppirna1.vcf");
            Stand(target, 0, at);
            target.Flags |= VehicleFlags.EngineOn;
            r.World.Vehicles.Add(target);
            // a radar missile takes the radar's target (the player picks a contact); a heat seeker finds its own
            r.Combat.RadarTarget[r.Car] = target;
            Pull(r);
            r.Combat.Frame(Dt);
            var round = r.Combat.Rounds.First();
            output.WriteLine(string.Create(Inv, $"{gdf} (\"{tip.Range}\"): a car {at:0} m ahead, target {(round.Target is null ? "none" : "it")}"));
            Assert.Equal(locks, round.Target == target);
        }
    }

    [InstallTheory]
    [InlineData("ggrenade.gdf", 1u)]   // HE Mortar on the roof (P)
    [InlineData("gwhiteph.gdf", 1u)]   // WP Mortar
    [InlineData("gcluster.gdf", 1u)]   // Cluster-Bomb
    [InlineData("ggrenade.gdf", 2u)]   // on a side mount (S), the barrel's lean turned up by the hardpoint
    public void AMortarLandsWhereItsTooltipSays(string gdf, uint style)
    {
        var r = Make(Base, gdf, style == 1 ? "vppirna1.vcf" : null, style);
        var (d, h, _) = Shoot(r);
        output.WriteLine(string.Create(Inv, $"{r.Car.Definition}: muzzle {h:0.00} m up, aimed {MathF.Asin(r.Gun.MuzzleRot.Forward.Y) * 180 / MathF.PI:0.0} deg, style {style}"));
        TipNear(Base, gdf, d, "Lands ");
    }

    /// <summary>The EZK sets its elevation for the target: a car far off gets its 45-degree shot, the furthest; one in front its lowest.</summary>
    [InstallFact]
    public void TheEzkLandsWithinItsTooltipsSpan()
    {
        var tip = Tip(Base, "gezkill.gdf");
        output.WriteLine($"gezkill.gdf: \"{tip.Range}\"");
        Assert.EndsWith(" out: it aims itself.", tip.Range);
        var span = Metres(tip);
        foreach (var (at, want, what) in new[] { (590f, span[1], "far"), (2f, span[0], "near") })
        {
            var r = Make(Base, "gezkill.gdf");
            var target = VehicleFactory.Create(Base, "vppirna1.vcf");
            Stand(target, 0, at);
            r.World.Vehicles.Add(target);
            r.Combat.RadarTarget[r.Car] = target;
            // the target only steers the elevation: it goes once the shot is away, so the round's own landing is measured
            var (d, _, _) = Shoot(r, () => { r.World.Vehicles.Remove(target); r.Combat.RadarTarget.Remove(r.Car); });
            output.WriteLine(string.Create(Inv, $"elevation {r.Gun.GunPitch * 180 / MathF.PI:0.0} deg"));
            Near($"gezkill.gdf {what}", want, d);
        }
    }

    /// <summary>The furthest a flame stream burns a car standing ahead (its near face's distance from the muzzle), on a roof mount (P, tipped 5 degrees down) and a side one (S, level).</summary>
    [InstallTheory]
    [InlineData("gfmedium.gdf", 1u)]   // Gas Launcher
    [InlineData("gfmedium.gdf", 2u)]
    [InlineData("gflight.gdf", 1u)]    // FlameThrower
    [InlineData("gflight.gdf", 2u)]
    [InlineData("gfheavy.gdf", 1u)]    // Napalm Hose
    [InlineData("tfmedium.gdf", 1u)]   // Gas Lnch Trt: aims itself, level at rest
    public void AFlameStreamBurnsAsFarAsItsTooltipSays(string gdf, uint style)
    {
        float furthest = 0;
        for (float gap = 10; gap <= 30; gap += 0.5f)
        {
            var r = Make(Base, gdf, style == 1 ? "vppirna1.vcf" : null, style);
            var muzzle = r.Car.Pos.ToVector3() + r.Car.Rot.ToWorld(r.Gun.MuzzlePos);
            var target = VehicleFactory.Create(Base, "vppirna1.vcf");
            Stand(target, 0, 0);
            // ahead of the muzzle (a side mount's is off the car's middle); a turret aims itself at the radar's target
            target.Pos = target.Pos with { X = muzzle.X, Z = muzzle.Z + gap - target.BoxZ[0] };
            r.World.Vehicles.Add(target);
            r.Combat.RadarTarget[r.Car] = target;
            for (int i = 0; i < 60; i++) { Pull(r); r.Combat.Frame(Dt); }
            if (r.Sink.Hits.Any(h => h.Victim == target)) furthest = gap;
            if (gap == 10) output.WriteLine(string.Create(Inv, $"{r.Car.Definition} style {style}: muzzle {(float)r.Car.Pos.Y + r.Car.Rot.ToWorld(r.Gun.MuzzlePos).Y:0.00} m up, aimed {MathF.Asin(r.Gun.MuzzleRot.Forward.Y) * 180 / MathF.PI:0.0} deg"));
        }
        Assert.True(furthest > 0, "the stream never reached the car");
        // "Range 17-20 m.": the roof's figure first, the level mounts' second
        TipNear(Base, gdf, furthest, "Range ", figure: style == 1 ? 0 : 1);
    }

    /// <summary>A dropper's round is laid behind the car: its tooltip says so and gives no range line.</summary>
    [InstallTheory]
    [InlineData("glandmin.gdf")]
    [InlineData("goilslck.gdf")]
    [InlineData("gfirdrop.gdf")]
    public void ADroppersRoundIsLaidBehind(string gdf)
    {
        var r = Make(Base, gdf);
        r.Car.Vel = new Vector3(0, 0, 25);   // the base game's droppers dribble below 20
        r.Car.Speed = 25;
        Pull(r);
        r.Combat.Frame(Dt);
        var round = r.Combat.Rounds.First();
        for (int i = 0; i < 40; i++) r.Combat.Frame(Dt);
        output.WriteLine(string.Create(Inv, $"{gdf}: at z {round.Pos.Z - (float)r.Car.Pos.Z:0.0} (the car's back {r.Car.BoxZ[0]:0.0})"));
        Assert.True(round.Pos.Z < r.Car.Pos.Z, $"{gdf}: laid at z {round.Pos.Z}");
        var tip = Tip(Base, gdf);
        Assert.StartsWith("Dropped behind:", tip.Effect);
        Assert.Null(tip.Range);
    }

    [Fact]
    public void TheNitroPacksChemicalMortarLandsWhereItsTooltipSays()
    {
        if (NitroDir is null) return;
        var vfs = new AssetVfs(new GameInstall(NitroDir));
        var r = Make(vfs, "ggchem.gdf");
        var (d, _, _) = Shoot(r);
        TipNear(vfs, "ggchem.gdf", d, "Lands ", nitro: true);
    }

    [Fact]
    public void TheNitroPacksBallOFlameFliesAsFarAsItsTooltipSays()
    {
        if (NitroDir is null) return;
        var vfs = new AssetVfs(new GameInstall(NitroDir));
        var r = Make(vfs, "gsball.gdf");
        var (d, _, _) = Shoot(r);
        TipNear(vfs, "gsball.gdf", d, "Range ", nitro: true);
    }

    /// <summary>A turret's mortar (the howitzer) pitches along its line to the target: no one landing, so no range line rather than a wrong one.</summary>
    [InstallFact]
    public void ATurretMortarHasNoRangeLine()
    {
        Assert.Equal(RangeKind.Aimed, Combat.RangeOf(GizmoDef.Read("tthowitz.gdf", Base.Read("tthowitz.gdf"))!).Kind);
        Assert.Null(Tip(Base, "tthowitz.gdf").Range);
    }
}
