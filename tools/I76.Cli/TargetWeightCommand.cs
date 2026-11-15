using System.Globalization;
using System.Text;
using I76.Formats.Assembly;
using I76.Formats.Bwd2;
using I76.Formats.Vfs;
using I76.Sim.Trip;
using I76.Sim.Vehicles;

namespace I76.Cli;

/// <summary>
/// A mission's target weight (offline, nothing in the game reads it yet): what the mission's opposition weighs and what a
/// fair player car for it weighs, from the mission files alone.
///
///   i76 target-weight [--install DIR] [--nitro-install DIR] [--buffer 0.10] [--tsv OUT.tsv] [--md OUT.md] [--cars OUT.tsv] [MISSION...]
///
/// Default: the base game's 17 trip missions (I76_INSTALL) and the Nitro Pack's scenarios (I76_NITRO_INSTALL, when set).
///
/// A car's weight is W = sqrt(EHP x DPS): EHP its armour and chassis points over the four zones, DPS what its weapons deal a
/// second sustained (a round's damage plus its blast's splash, at the rate a burst and its delay allow). Twice the armour and
/// twice the guns is twice the weight; a car with no gun weighs nothing. The opposition's weight is the sum over the cars the
/// mission places on a team other than the player's and nobody's (helicopters and gun emplacements counted, listed apart).
///
/// The player's side, for a trip mission N: the trip's car is the Piranha the trip starts with, and what the trip can have
/// given it by N is every part the field salvage of the missions before could offer (each earlier mission's enemy cars'
/// parts and its .fsi list, clamped by the salvage tier table at that mission's level, as <see cref="SalvagePool"/> does; T04
/// and T12 give none). The trip ceiling is the Piranha with the strongest of those weapons on each of its mounts (the garage's
/// mount rule; its own armour; the weight limit not applied). The budget is that ceiling plus the buffer, but below the
/// ceiling one mission on (the parts mission N's own salvage adds are next level). A Nitro scenario has no trip: its
/// reference is the mission's own player car. The mission's own player car (what a co-op seat drives by default) is listed
/// for both.
/// </summary>
public static class TargetWeightCommand
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>One car's numbers.</summary>
    public sealed record Car(string Def, string Name, int Team, uint Class, int Armour, int Chassis, float Dps, float Mass, IReadOnlyList<string> Weapons)
    {
        public int Ehp => Armour + Chassis;
        public float Weight => MathF.Sqrt(Ehp * MathF.Max(0, Dps));
        public string Kind => Class == 9 ? "heli" : Class == 8 ? "gun" : "car";
    }

    /// <summary>One mission's row.</summary>
    public sealed record Row(string Mission, bool Nitro, IReadOnlyList<Car> Enemies, Car? Own, Car? Ceiling, float? NextCeiling, int Level,
        IReadOnlyList<string> Pool, IReadOnlyList<string> NextParts, float Buffer)
    {
        public float Opposition => Enemies.Sum(e => e.Weight);
        public float Reference => Ceiling?.Weight ?? Own?.Weight ?? 0;
        public float Budget
        {
            get
            {
                float b = Reference * (1 + Buffer);
                // below the next mission's ceiling: what the next level gives is not in the budget
                if (NextCeiling is { } n && n > Reference) b = MathF.Min(b, Reference + 0.5f * (n - Reference));
                return b;
            }
        }
        public float Ratio => Reference > 0 ? Opposition / Reference : 0;
    }

    public static int Run(List<string> argv, string? installDir)
    {
        string? Opt(string name)
        {
            int i = argv.IndexOf(name);
            if (i < 0 || i + 1 >= argv.Count) return null;
            var v = argv[i + 1];
            argv.RemoveRange(i, 2);
            return v;
        }
        var nitroDir = Opt("--nitro-install") ?? Environment.GetEnvironmentVariable("I76_NITRO_INSTALL");
        installDir = Opt("--install") ?? installDir;
        float buffer = float.Parse(Opt("--buffer") ?? "0.10", Inv);
        string? tsv = Opt("--tsv"), md = Opt("--md"), cars = Opt("--cars");
        var only = argv.Skip(1).Select(a => a.ToLowerInvariant()).ToHashSet();
        var rows = new List<Row>();
        if (installDir is { Length: > 0 } && Directory.Exists(installDir)) rows.AddRange(Trip(new GameInstall(installDir), buffer, only));
        if (nitroDir is { Length: > 0 } && Directory.Exists(nitroDir)) rows.AddRange(Nitro(new GameInstall(nitroDir), buffer, only));
        if (rows.Count == 0) { Console.Error.WriteLine("no install: --install / --nitro-install, or I76_INSTALL / I76_NITRO_INSTALL"); return 2; }

        var table = Markdown(rows);
        Console.Write(table);
        if (md is not null) File.WriteAllText(md, table);
        if (tsv is not null) File.WriteAllText(tsv, Tsv(rows));
        if (cars is not null) File.WriteAllText(cars, CarsTsv(rows));
        return 0;
    }

    // ---- the base game's trip ----

    private static IEnumerable<Row> Trip(GameInstall install, float buffer, HashSet<string> only)
    {
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var db = PartsDatabase.Build(vfs);
        var stems = Enumerable.Range(1, 17).Select(n => $"t{n:00}").Where(s => install.FindMission(s) is not null).ToList();
        var enemies = new Dictionary<string, (List<Car> Cars, List<Vehicle> Built, Car? Own)>();
        foreach (var s in stems) enemies[s] = Placed(vfs, asm, install, s);

        // the pool before each mission: the start car's parts, then each earlier mission's salvage at its level
        const string start = "vppirna1.vcf";
        var pool = new List<HashSet<string>>();
        var have = new HashSet<string>(StartParts(vfs, start), StringComparer.OrdinalIgnoreCase);
        foreach (var s in stems)
        {
            pool.Add([.. have]);
            foreach (var part in Salvage(vfs, s, enemies[s].Built)) have.Add(part);
        }
        pool.Add([.. have]);   // after the last

        var ceilings = pool.Select(p => Ceiling(vfs, db, start, p)).ToList();
        for (int i = 0; i < stems.Count; i++)
        {
            var s = stems[i];
            if (only.Count > 0 && !only.Contains(s)) continue;
            var next = pool[i + 1].Except(pool[i], StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            int level = int.Parse(s[1..], Inv);
            yield return new Row(s, false, enemies[s].Cars, enemies[s].Own, ceilings[i], i + 1 < ceilings.Count ? ceilings[i + 1]?.Weight : null, level,
                [.. pool[i].Order(StringComparer.OrdinalIgnoreCase)], next, buffer);
        }
    }

    // ---- the Nitro Pack's scenarios ----

    private static IEnumerable<Row> Nitro(GameInstall install, float buffer, HashSet<string> only)
    {
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var stems = Enumerable.Range(1, 30).Select(n => $"p{n:00}").Where(s => install.FindMission(s) is not null).ToList();
        foreach (var s in stems)
        {
            if (only.Count > 0 && !only.Contains(s)) continue;
            var (list, _, own) = Placed(vfs, asm, install, s);
            yield return new Row(s, true, list, own, null, null, int.Parse(s[1..], Inv), [], [], buffer);
        }
    }

    // ---- a mission's cars ----

    /// <summary>The mission's placed vehicles: the opposition's numbers and built cars (for their salvage), and the player placement's car.</summary>
    private static (List<Car> Cars, List<Vehicle> Built, Car? Own) Placed(AssetVfs vfs, ModelAssembler asm, GameInstall install, string stem)
    {
        var w = new MissionWorld(vfs, asm, install.FindMission(stem)!);
        var vehicles = w.Placements.Where(p => p.Definition?.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase) == true).ToList();
        // as Entity.Team: bits 16-18, the eighth team its own bit read with the player bit
        static int Team(I76.Formats.Mission.PlacedObject o) => (o.ObjectFlags & (I76.Sim.Missions.Entity.Team8 | 0x10)) == (I76.Sim.Missions.Entity.Team8 | 0x10) ? 8 : (int)((o.ObjectFlags >> 16) & 7);
        var player = vehicles.FirstOrDefault(p => p.Object.IsPlayer);
        int us = player is null ? 1 : Team(player.Object);
        var cars = new List<Car>();
        var built = new List<Vehicle>();
        Car? own = null;
        foreach (var p in vehicles)
        {
            int team = Team(p.Object);
            if (p != player && (team == 0 || team == us)) continue;
            if (Measure(vfs, p.Definition!, team) is not var (car, v)) { Console.Error.WriteLine($"{stem}: {p.Definition} does not build"); continue; }
            if (p == player) { own = car; continue; }
            cars.Add(car);
            built.Add(v);
        }
        return (cars, built, own);
    }

    /// <summary>A car file's numbers, and the car as the mission builds an AI car (its damage state, for its salvage).</summary>
    private static (Car, Vehicle)? Measure(AssetVfs vfs, string def, int team)
    {
        Vehicle v;
        try { v = VehicleFactory.Create(vfs, def); }
        catch (Exception) { return null; }   // a car this install cannot build: reported by the caller
        if (v.Build is { } b && v.ObjectClass is not (8 or 9)) v.Health = I76.Sim.Damage.VehicleDamage.Create(v, b, false, new I76.Sim.Rng.CrtRand(1));
        var weapons = v.Armory?.Weapons.Select(x => x.Def).ToList() ?? [];
        int armour = v.Build?.Armour.Sum() ?? 0, chassis = v.Build?.Chassis.Sum() ?? 0;
        string name = vfs.TryRead(def) is { } d ? TripCar.FromVcf(d).Variant : def;
        var car = new Car(def.ToLowerInvariant(), name, team, v.ObjectClass, armour, chassis, weapons.Sum(g => Dps(vfs, g)), v.P.Mass,
            [.. weapons.Select(g => Path.GetFileNameWithoutExtension(g.File).ToLowerInvariant())]);
        return (car, v);
    }

    private static readonly Dictionary<string, float> _splash = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A weapon's sustained damage a second: (a round's damage + its blast's splash) x the rate its bursts and delay allow.</summary>
    public static float Dps(AssetVfs vfs, GizmoDef g)
    {
        float splash = 0;
        if (g.Ordnance is { } o && o.ImpactXdf.Length > 1 && o.ImpactXdf[1] is { Length: > 0 } fx)
        {
            if (!_splash.TryGetValue(fx, out splash))
                _splash[fx] = splash = vfs.TryRead(fx) is { } xd && EffectDef.Read(fx, xd) is { } e ? e.SplashDamage : 0;
        }
        float rate = g.Rate;
        if (g.Burst > 1 && g.Rate > 0) rate = g.Burst / (g.Burst / g.Rate + MathF.Max(0, g.Delay));
        else if (g.Delay > 0 && (g.Rate <= 0 || g.Delay > 1 / g.Rate)) rate = 1 / g.Delay;
        return (g.Damage + splash) * MathF.Max(0, rate);
    }

    // ---- the trip's parts ----

    /// <summary>The start car's own parts, as salvage keys (its weapons' files, its components, its tyres).</summary>
    private static IEnumerable<string> StartParts(AssetVfs vfs, string vcf)
    {
        var c = TripCar.FromVcf(vfs.Read(vcf));
        foreach (var (_, gdf) in c.Weapons) if (gdf.Length > 0 && !gdf.Equals("null", StringComparison.OrdinalIgnoreCase)) yield return gdf.ToLowerInvariant();
        yield return $"eng{c.Engine + 1:00}";
        yield return $"sus{c.Suspension + 1:00}";
        yield return $"bra{c.Brakes + 1:00}";
        foreach (var w in c.Wdf) if (w.Length > 0 && !w.Equals("null", StringComparison.OrdinalIgnoreCase)) yield return w.ToLowerInvariant();
    }

    /// <summary>What a won mission's field salvage can offer: every enemy car's parts (all killed) and its .fsi list, clamped at its level.</summary>
    private static IEnumerable<string> Salvage(AssetVfs vfs, string stem, IEnumerable<Vehicle> enemies)
    {
        if (stem is "t04" or "t12") return [];          // the dream keeps nothing; T12 writes no state
        var pool = new SalvagePool(stem);
        foreach (var v in enemies) pool.AddDeadCar(v);
        if (vfs.TryRead($"{stem}.fsi") is { } fsi)
            foreach (var raw in Encoding.Latin1.GetString(fsi).Split('\n'))
            {
                var line = raw.Trim('\r', ' ', '\t');
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int sp = line.IndexOf(' ');
                pool.Add(sp < 0 ? line : line[..sp], -1, true);
            }
        return pool.Items.Select(x => x.Name.ToLowerInvariant()).Distinct();
    }

    /// <summary>
    /// The trip ceiling: the start car with, on each weapon mount, the strongest weapon of the pool the mount accepts (the
    /// garage's rule, <see cref="ChassisRules.Accepts"/>); its own armour and chassis.
    /// </summary>
    private static Car? Ceiling(AssetVfs vfs, PartsDatabase db, string vcf, IReadOnlySet<string> pool)
    {
        var car = TripCar.FromVcf(vfs.Read(vcf));
        var inv = TripInventory.FromCar(car, db, vfs);
        var guns = pool.Where(p => p.EndsWith(".gdf", StringComparison.OrdinalIgnoreCase))
            .Select(p => (Part: db.ByStem(p), Def: vfs.TryRead(p) is { } d ? GizmoDef.Read(p, d) : null))
            .Where(x => x.Part is not null && x.Def is not null).ToList();
        var fitted = new List<GizmoDef>();
        foreach (var slot in inv.Slots.Where(s => s.Type is 7 or 8))
        {
            var best = guns.Where(g => ChassisRules.Accepts(slot, g.Part!)).OrderByDescending(g => Dps(vfs, g.Def!)).ThenBy(g => g.Part!.Stem, StringComparer.Ordinal).FirstOrDefault();
            if (best.Def is not null) fitted.Add(best.Def);
        }
        var v = VehicleFactory.Create(vfs, vcf);
        return new Car(vcf, car.Variant + " (trip ceiling)", 1, v.ObjectClass, car.Armour.Sum(), car.Chassis.Sum(), fitted.Sum(g => Dps(vfs, g)),
            v.P.Mass - (v.Armory?.WeaponMass ?? 0) + fitted.Sum(g => g.Mass), [.. fitted.Select(g => Path.GetFileNameWithoutExtension(g.File).ToLowerInvariant())]);
    }

    // ---- output ----

    private static string F(float x) => x.ToString("0", Inv);

    private static string Markdown(IReadOnlyList<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Mission | Opposition (cars / helis / guns) | Opposition weight | Heaviest enemy | Own car (weight) | Reference | Budget | Opposition / reference | Ceiling's weapons | Next level (excluded) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
        {
            var heavy = r.Enemies.OrderByDescending(e => e.Weight).FirstOrDefault();
            string counts = $"{r.Enemies.Count(e => e.Kind == "car")} / {r.Enemies.Count(e => e.Kind == "heli")} / {r.Enemies.Count(e => e.Kind == "gun")}";
            string own = r.Own is { } o ? $"{o.Name} ({F(o.Weight)})" : "-";
            string reference = r.Ceiling is { } c ? $"{F(c.Weight)} (trip ceiling: EHP {c.Ehp}, DPS {F(c.Dps)})" : $"{F(r.Reference)} (own car)";
            string weapons = r.Ceiling is { } cc ? string.Join(" ", cc.Weapons) : r.Own is { } oo ? string.Join(" ", oo.Weapons) : "";
            string next = r.Nitro ? "n/a" : r.NextParts.Count == 0 ? "none" : string.Join(" ", r.NextParts.Take(8)) + (r.NextParts.Count > 8 ? $" (+{r.NextParts.Count - 8})" : "");
            sb.AppendLine($"| {r.Mission.ToUpperInvariant()} | {counts} | {F(r.Opposition)} | {(heavy is null ? "-" : $"{heavy.Name} {F(heavy.Weight)}")} | {own} | {reference} | {F(r.Budget)} | {r.Ratio.ToString("0.0", Inv)} | {weapons} | {next} |");
        }
        return sb.ToString();
    }

    private static string Tsv(IReadOnlyList<Row> rows)
    {
        var sb = new StringBuilder("mission\tnitro\tcars\thelis\tguns\topposition\town_car\town_weight\tceiling_ehp\tceiling_dps\tceiling_weight\treference\tbudget\tratio\tceiling_weapons\tnext_level\tpool\n");
        foreach (var r in rows)
            sb.Append(string.Join('\t', r.Mission, r.Nitro ? 1 : 0, r.Enemies.Count(e => e.Kind == "car"), r.Enemies.Count(e => e.Kind == "heli"), r.Enemies.Count(e => e.Kind == "gun"),
                F(r.Opposition), r.Own?.Def ?? "", r.Own is { } o ? F(o.Weight) : "", r.Ceiling?.Ehp.ToString(Inv) ?? "", r.Ceiling is { } c ? F(c.Dps) : "", r.Ceiling is { } c2 ? F(c2.Weight) : "",
                F(r.Reference), F(r.Budget), r.Ratio.ToString("0.00", Inv), string.Join(' ', r.Ceiling?.Weapons ?? []), string.Join(' ', r.NextParts), string.Join(' ', r.Pool))).Append('\n');
        return sb.ToString();
    }

    private static string CarsTsv(IReadOnlyList<Row> rows)
    {
        var sb = new StringBuilder("mission\tdef\tname\tteam\tkind\tarmour\tchassis\tdps\tweight\tmass\tweapons\n");
        foreach (var r in rows)
            foreach (var c in r.Enemies.Prepend(r.Own).OfType<Car>())
                sb.Append(string.Join('\t', r.Mission, c.Def, c.Name, c == r.Own ? "player" : c.Team.ToString(Inv), c.Kind, c.Armour, c.Chassis, F(c.Dps), F(c.Weight), F(c.Mass), string.Join(' ', c.Weapons))).Append('\n');
        return sb.ToString();
    }
}
