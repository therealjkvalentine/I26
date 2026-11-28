using System.Globalization;
using System.Text.Json;
using I76.Formats.Bwd2;
using I76.Formats.Vfs;

namespace I76.Formats.Remix;

/// <summary>
/// The roguetrip's rosters (docs/remix-missions.md "Rosters and salvage"): the base game's own cars ranked on a ladder by their parts'
/// trip levels (<see cref="PartTiers"/>: how far into the trip the original first lets a part out of a wreck), and the player's gear on
/// the same scale, so a job fields cars near the player's own and their wrecks are worth picking over. The ladder is a table of car file
/// stems and numbers (<see cref="Cars"/>, written by <c>i76 remix ladder --table</c> from a stock install and held to it by a test): no
/// game data, and the same on every machine whatever its install's ADDON holds, so a co-op host and guest field the same cars.
/// </summary>
public static class RemixRoster
{
    /// <summary>
    /// A car on the ladder: its file stem, its parts' trip levels (engine, suspension, brakes, wheels, its best gun), its strength in
    /// Piranhas (RemixChallenge's: sqrt of hit points x guns' damage a second, each over the melee Piranha's), and whether a job may field it.
    /// </summary>
    public sealed record Car(string Stem, int Engine, int Suspension, int Brakes, int Wheels, string Best, double Strength, bool Enemy = true)
    {
        /// <summary>The level of its best gun (or dropper): 1 for a car without one.</summary>
        public int Gun => Math.Max(1, PartTiers.Level(Best));
        /// <summary>The salvage key of the car's part in a slot (0 engine, 1 suspension, 2 brakes, 4 its best gun); the wheels' is the trip's plain style.</summary>
        public string Key(int slot) => slot switch
        {
            0 => $"eng{Ordinal(2, Engine):00}", 1 => $"sus{Ordinal(3, Suspension):00}", 2 => $"bra{Ordinal(4, Brakes):00}",
            3 => $"wauto_0{"abcd"[Math.Clamp(Array.FindIndex(PartTiers.Tables[0].Entries, e => e.TripMin == Wheels), 0, 3)]}.wdf", _ => Best,
        };
        private static int Ordinal(int type, int level) => Math.Max(0, Array.FindIndex(PartTiers.Tables[type].Entries, e => e.TripMin == level)) + 1;
        /// <summary>The car's level: the mean of its engine, suspension, brakes and best gun (the wheels left out: every enemy rolls on stock tyres).</summary>
        public double Level => (Engine + Suspension + Brakes + Gun) / 4.0;
        public Gear Gear => new(Engine, Suspension, Brakes, Wheels, Gun);
    }

    /// <summary>
    /// What the player drives, as trip levels per slot: engine, suspension, brakes, wheels and the best gun. Written "E.S.B.W.G" in a run
    /// file, an offer and on the wire (<see cref="ToString"/>, <see cref="Parse"/>); "" is no gear (the director's stock pools).
    /// </summary>
    public sealed record Gear(int Engine, int Suspension, int Brakes, int Wheels, int Gun)
    {
        public double Level => (Engine + Suspension + Brakes + Gun) / 4.0;
        public int Slot(int k) => k switch { 0 => Engine, 1 => Suspension, 2 => Brakes, 3 => Wheels, _ => Gun };
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Engine}.{Suspension}.{Brakes}.{Wheels}.{Gun}");

        public static Gear? Parse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var f = s.Split('.');
            if (f.Length != 5 || !f.All(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) return null;
            var n = f.Select(x => Math.Clamp(int.Parse(x, CultureInfo.InvariantCulture), 1, 16)).ToArray();
            return new Gear(n[0], n[1], n[2], n[3], n[4]);
        }

        /// <summary>A crew's gear: each slot the mean of the crew's, rounded (docs/remix-missions.md "Rosters and salvage": co-op).</summary>
        public static Gear? Mean(IEnumerable<Gear?> crew)
        {
            var l = crew.OfType<Gear>().ToList();
            if (l.Count == 0) return null;
            int M(Func<Gear, int> f) => (int)Math.Round(l.Average(f), MidpointRounding.AwayFromZero);
            return new Gear(M(g => g.Engine), M(g => g.Suspension), M(g => g.Brakes), M(g => g.Wheels), M(g => g.Gun));
        }
    }

    /// <summary>
    /// The ladder (i76 remix ladder --table on the base game; the Nitro Pack adds cars but changes none of these): every car file a player
    /// may pick or a job may field. Enemy false: the player's own Piranhas (the trip's and the melee's), the cars without guns, helicopters,
    /// tanks, emplacements and the scripted variants (their names end in f). The police and the convoy leads stay the director's own lists.
    /// </summary>
    public static readonly Car[] Cars =
    [
        new("valepre3", 1, 1, 1, 1, "gblox.gdf", 0.35),
        new("valepre1", 1, 4, 2, 1, "gmlight.gdf", 0.25),
        new("valepre2", 1, 4, 1, 1, "gmmedium.gdf", 0.32),
        new("vavikea1", 1, 4, 1, 1, "gblox.gdf", 0.50),
        new("vastryd3", 5, 1, 2, 1, "gmmedium.gdf", 0.31),
        new("vastryd2", 1, 4, 2, 1, "gclight.gdf", 0.30),
        new("vavikea2", 1, 4, 1, 1, "glandmin.gdf", 0.59),
        new("vccameo2", 9, 1, 2, 1, "gmmedium.gdf", 0.79),
        new("vastryd1", 9, 1, 2, 1, "gblox.gdf", 0.28),
        new("vccameo1", 5, 8, 2, 1, "gdumb.gdf", 0.65),
        new("vazamz1", 9, 1, 2, 1, "gclight.gdf", 0.56),
        new("vccameo3", 1, 4, 6, 1, "gmheavy.gdf", 0.83),
        new("vcmanta2", 1, 8, 2, 1, "gmheavy.gdf", 1.04),
        new("vavikea3", 5, 1, 2, 1, "gfirdrop.gdf", 0.97),
        new("vazamz2", 5, 1, 6, 1, "glandmin.gdf", 0.78),
        new("vpjrabt1", 9, 1, 2, 1, "gmheavy.gdf", 0.92),
        new("vfcoupe3", 9, 4, 2, 1, "tmlight.gdf", 0.31),
        new("vfdclyd2", 5, 4, 6, 1, "gcmedium.gdf", 1.10),
        new("vfypony1", 9, 4, 2, 1, "tmmedium.gdf", 0.48),
        new("vcgcour1", 1, 12, 6, 1, "gblox.gdf", 0.93),
        new("vcvan1", 9, 8, 1, 1, "gclight.gdf", 0.94),
        new("vazamz3", 9, 4, 2, 1, "glandmin.gdf", 0.75),
        new("vcroyal2", 5, 8, 2, 1, "gmheavy.gdf", 1.23),
        new("vpjrabt2", 9, 4, 2, 1, "glandmin.gdf", 0.91),
        new("vfypony3", 9, 1, 6, 1, "gmheavy.gdf", 0.63),
        new("vjsovrn2", 5, 1, 10, 1, "tdumb.gdf", 1.06),
        new("vfratlr3", 13, 4, 2, 1, "tmmedium.gdf", 0.39),
        new("vcgcour3", 1, 12, 1, 1, "gfirdrop.gdf", 1.54),
        new("vcroyal3", 13, 4, 1, 1, "glandmin.gdf", 0.94),
        new("vgoon3", 5, 8, 2, 1, "gcheavy.gdf", 0.64),
        new("vcgcour2", 5, 8, 6, 1, "glandmin.gdf", 1.03),
        new("vcmanta3", 5, 4, 10, 1, "glandmin.gdf", 0.98),
        new("vcroyal1", 5, 8, 10, 1, "gclight.gdf", 0.92),
        new("vdlight1", 9, 12, 2, 1, "gclight.gdf", 0.87),
        new("vfcoupe2", 5, 8, 6, 1, "tmheavy.gdf", 0.51),
        new("vcvan3", 9, 4, 2, 1, "tcmedium.gdf", 1.78),
        new("vfdclyd1", 13, 1, 6, 1, "glandmin.gdf", 1.84),
        new("vjsovrn3", 5, 8, 6, 1, "tsradar.gdf", 1.23),
        new("vfratlr1", 5, 8, 10, 1, "tmlight.gdf", 0.41),
        new("vgoon2", 13, 4, 1, 1, "gfirdrop.gdf", 0.89),
        new("vcmanta1", 5, 8, 10, 1, "glandmin.gdf", 0.90),
        new("vdlight3", 9, 8, 1, 1, "tcmedium.gdf", 1.42),
        new("vfpalmn1", 9, 12, 2, 1, "glandmin.gdf", 0.67),
        new("vfpalmn2", 5, 4, 10, 1, "gfirdrop.gdf", 1.05),
        new("vfypony2", 1, 12, 10, 1, "gmheavy.gdf", 0.52),
        new("vmxmarx3", 13, 8, 6, 1, "gsheat.gdf", 0.93),
        new("vdstag2", 13, 4, 2, 1, "tcmedium.gdf", 1.37),
        new("vdstag3", 5, 12, 2, 1, "tcheavy.gdf", 1.59),
        new("vmxmarx2", 13, 1, 6, 1, "gfirdrop.gdf", 1.70),
        new("vfcoupe1", 13, 8, 6, 1, "tmmedium.gdf", 0.42),
        new("vjsovrn1", 9, 12, 6, 1, "tmlight.gdf", 1.00),
        new("vfpalmn3", 9, 8, 6, 1, "gcheavy.gdf", 0.92),
        new("vfratlr2", 9, 12, 6, 1, "gmheavy.gdf", 0.51),
        new("vgoon1", 9, 4, 10, 1, "gfirdrop.gdf", 0.89),
        new("vdstag1", 9, 8, 6, 1, "tclight.gdf", 1.07),
        new("vfdclyd3", 9, 8, 6, 1, "tcmedium.gdf", 1.31),
        new("vmxmarx1", 13, 12, 1, 1, "gfirdrop.gdf", 1.81),
        new("vpjrabt3", 13, 8, 6, 1, "gfirdrop.gdf", 1.29),
        new("vsvan1", 13, 8, 10, 1, "glandmin.gdf", 1.23),
        new("vdlight2", 5, 12, 10, 1, "tcmedium.gdf", 0.83),
        new("vdillo1", 1, 1, 1, 1, "", 0.00, false),
        new("vehscn", 1, 1, 1, 1, "gdumb.gdf", 0.80, false),
        new("vppa01", 1, 1, 1, 1, "gdumb.gdf", 0.80, false),
        new("vppt01", 1, 1, 1, 1, "gdumb.gdf", 0.80, false),
        new("vppt02", 1, 1, 1, 1, "gdumb.gdf", 0.80, false),
        new("vxhowit1", 1, 1, 1, 1, "tthowitz.gdf", 0.09, false),
        new("vppt03", 1, 1, 1, 1, "gmmedium.gdf", 0.83, false),
        new("vppt04", 1, 1, 2, 3, "gmmedium.gdf", 0.83, false),
        new("vppt05", 1, 1, 2, 3, "gblox.gdf", 0.93, false),
        new("vppt06", 1, 1, 2, 3, "gblox.gdf", 0.93, false),
        new("vstank2", 1, 4, 2, 1, "gmmedium.gdf", 1.42, false),
        new("vuchuey1", 1, 1, 1, 1, "tmmedium.gdf", 0.81, false),
        new("vuchuey3", 1, 1, 1, 1, "tmheavy.gdf", 1.52, false),
        new("vxturr1", 1, 1, 1, 1, "tmheavy.gdf", 0.45, false),
        new("vcgcourf", 1, 1, 1, 1, "gsradar.gdf", 1.01, false),
        new("vugcobr1", 1, 1, 1, 1, "tsradar.gdf", 0.37, false),
        new("vstank1", 1, 4, 2, 1, "gcmedium.gdf", 1.46, false),
        new("vstankf", 1, 1, 1, 1, "gcheavy.gdf", 1.62, false),
        new("vppt08", 5, 4, 2, 3, "gclight.gdf", 0.88, false),
        new("vuchuey2", 1, 4, 2, 1, "tmheavy.gdf", 0.99, false),
        new("vgoonf", 1, 1, 1, 1, "gscherub.gdf", 0.87, false),
        new("vhelif", 1, 4, 2, 1, "gsradar.gdf", 1.30, false),
        new("vugcobr2", 1, 1, 1, 1, "tcmedium.gdf", 0.55, false),
        new("vppt07", 5, 4, 6, 3, "gclight.gdf", 0.88, false),
        new("vppt09", 5, 4, 6, 3, "gclight.gdf", 0.85, false),
        new("vxbus1no", 5, 4, 10, 1, "", 0.00, false),
        new("vppt10", 5, 4, 6, 3, "gcmedium.gdf", 0.90, false),
        new("vcvan2", 9, 4, 1, 1, "glandmin.gdf", 1.37, false),
        new("vppirna2", 5, 8, 1, 1, "glandmin.gdf", 1.18, false),
        new("vppirna3", 1, 8, 6, 1, "gmheavy.gdf", 0.83, false),
        new("vppirna1", 9, 8, 2, 1, "glandmin.gdf", 1.00, false),
        new("vppt11", 5, 8, 6, 7, "glandmin.gdf", 0.98, false),
        new("vxbus1", 13, 1, 6, 1, "glandmin.gdf", 1.27, false),
        new("vxmilk1", 13, 8, 2, 1, "tmheavy.gdf", 1.26, false),
        new("vsvan2", 13, 12, 10, 11, "", 0.00, false),
        new("vxktrac1", 13, 12, 10, 1, "", 0.00, false),
        new("vppt12", 9, 8, 6, 7, "tcmedium.gdf", 0.94, false),
        new("vxmilk3", 13, 8, 2, 1, "tcheavy.gdf", 1.86, false),
        new("vppt13", 9, 8, 10, 7, "gcheavy.gdf", 1.02, false),
        new("vppt14", 9, 8, 10, 7, "gfirdrop.gdf", 1.36, false),
        new("vcpolic1", 13, 8, 6, 1, "tcmedium.gdf", 1.06, false),
        new("vcpolic2", 13, 8, 6, 1, "tclight.gdf", 0.77, false),
        new("vcpolic3", 13, 8, 6, 1, "tclight.gdf", 0.77, false),
        new("vxmilk2", 13, 8, 10, 1, "tsradar.gdf", 1.39, false),
        new("vppt15", 9, 12, 10, 11, "gfirdrop.gdf", 1.43, false),
        new("vppt16", 13, 12, 10, 11, "gfirdrop.gdf", 1.43, false),
        new("vppt17", 13, 12, 10, 11, "gfirdrop.gdf", 1.55, false),
    ];

    private static readonly Dictionary<string, Car> ByStem = Cars.ToDictionary(c => c.Stem, StringComparer.OrdinalIgnoreCase);

    /// <summary>The ladder's entry for a car file (stem or file name), or null.</summary>
    public static Car? Of(string vcf) => ByStem.GetValueOrDefault(Stem(vcf));

    private static string Stem(string vcf) => Path.GetFileNameWithoutExtension(vcf.Trim()).ToLowerInvariant();

    /// <summary>The cars a job may field, by level.</summary>
    public static readonly Car[] Enemies = [.. Cars.Where(c => c.Enemy).OrderBy(c => c.Level).ThenBy(c => c.Stem, StringComparer.Ordinal)];

    // ---- the player's gear ----

    /// <summary>
    /// A car file's gear without the install: its ladder entry, else (a car saved from the garage under a new name) the ladder's first
    /// variant of the same model (vppirna5 → vppirna1), else the melee Piranha's.
    /// </summary>
    public static Gear GearOfCar(string vcf)
    {
        var s = Stem(vcf);
        if (ByStem.TryGetValue(s, out var c)) return c.Gear;
        var model = s.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (Cars.Where(x => x.Stem.StartsWith(model, StringComparison.Ordinal) && x.Stem.Length == s.Length).OrderBy(x => x.Stem, StringComparer.Ordinal).FirstOrDefault() is { } m) return m.Gear;
        return ByStem.TryGetValue("vppirna1", out var p) ? p.Gear : new Gear(9, 8, 2, 1, 9);
    }

    /// <summary>
    /// The gear of a trip inventory (TripInventory.Save's JSON: its nodes with State 1 are in the car, Stem their salvage key), or null
    /// when it has no car parts or cannot be read.
    /// </summary>
    public static Gear? GearOfInventory(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Nodes", out var nodes)) return null;
            var keys = new List<string>();
            foreach (var n in nodes.EnumerateArray())
                if (n.TryGetProperty("State", out var st) && st.GetInt32() == 1 && n.TryGetProperty("Stem", out var stem) && stem.GetString() is { Length: > 0 } k) keys.Add(k);
            return keys.Count == 0 ? null : GearOf(keys);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The gear of a set of salvage keys (eng04, sus04, bra04, wauto_1d.wdf, gmheavy.gdf, ...): each slot its best part's level, 1 where none.</summary>
    public static Gear GearOf(IEnumerable<string> keys)
    {
        var l = keys.ToList();
        int Best(Func<string, bool> f) => Math.Max(1, l.Where(f).Select(PartTiers.Level).DefaultIfEmpty(1).Max());
        bool Comp(string k, string p) => k.StartsWith(p, StringComparison.OrdinalIgnoreCase) && !k.Contains('.');
        return new Gear(Best(k => Comp(k, "eng")), Best(k => Comp(k, "sus")), Best(k => Comp(k, "bra")), Best(k => PartTiers.TypeOf(k) == 0), Best(k => PartTiers.TypeOf(k) == 1));
    }

    /// <summary>A car file's salvage keys read from the install (eng/sus/bra NN by the compnent ordinal, the wheels' .wdf, the guns' .gdf), and its armour and chassis total.</summary>
    public static (List<string> Keys, int Armour) Read(AssetVfs vfs, string vcf)
    {
        var d = vfs.TryRead(vcf) ?? throw new FileNotFoundException(vcf);
        var f = new Bwd2File(d);
        var c = f.First("VCFC") ?? throw new InvalidDataException("no VCFC");
        int p = c.PayloadOffset;
        int engine = EngineOrdinal(vfs, Bin.I32(d, p + 0x2a)), susp = Bin.I32(d, p + 0x2e), brakes = Bin.I32(d, p + 0x32);
        string[] wdf = [Bin.CStr(d, p + 0x36, 13), Bin.CStr(d, p + 0x43, 13), Bin.CStr(d, p + 0x50, 13)];
        int armour = Enumerable.Range(0, 8).Sum(z => Bin.I32(d, p + 0x5d + 4 * z));
        var keys = new List<string> { $"eng{engine + 1:00}", $"sus{susp + 1:00}", $"bra{brakes + 1:00}" };
        keys.AddRange(wdf.Where(w => w.Length > 0 && !w.Equals("null", StringComparison.OrdinalIgnoreCase)).Select(w => w.ToLowerInvariant()).Distinct());
        keys.AddRange(f.WeaponMounts().Select(m => m.Gizmo.ToLowerInvariant()).Where(g => g.Length > 0 && g != "null"));
        return (keys, armour);
    }

    /// <summary>engsnd.dat's ENG NUM to the compnent.cdf ordinal (VehicleFactory.EngineCompId's reading).</summary>
    public static int EngineOrdinal(AssetVfs vfs, int engNum)
    {
        if (vfs.TryRead("engsnd.dat") is not { } d) return 0;
        foreach (var line in Bin.Latin1(d).Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var f = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 2 && int.TryParse(f[0], CultureInfo.InvariantCulture, out var num) && num == engNum && int.TryParse(f[1], CultureInfo.InvariantCulture, out var id)) return id;
        }
        return 0;
    }

    /// <summary>
    /// Whether a job may field a car (null) or why not: not the player's own Piranhas (vpp, vehscn), helicopters (vh, vu), tanks (vstank),
    /// emplacements and the tractor (vxturr, vxhowit, vxktrac), scripted variants (a stem ending in f), the convoy leads (vxmilk, vxbus,
    /// vcvan2: the director's own), the police (vcpolic, the director's own), a car without guns, or one with a part the salvage table does not know
    /// (its wreck's salvage would stop at it).
    /// </summary>
    public static string? Eligible(string stem, IReadOnlyList<string> keys, double dps)
    {
        stem = stem.ToLowerInvariant();
        if (!stem.StartsWith('v')) return "not a stock car";
        foreach (var p in (string[])["vpp", "vehscn", "vh", "vu", "vstank", "vxturr", "vxhowit", "vxktrac", "vxmilk", "vxbus", "vcpolic", "vdillo"])
            if (stem.StartsWith(p, StringComparison.Ordinal)) return $"{p}: not fielded";
        if (stem.EndsWith('f')) return "a scripted variant";
        if (RemixDirector.Leads.Contains(stem)) return "a convoy lead";
        if (dps <= 0 || !keys.Any(k => PartTiers.TypeOf(k) == 1)) return "no guns";
        if (keys.FirstOrDefault(k => PartTiers.Of(k) is null) is { } unknown) return $"{unknown} not in the salvage table";
        return null;
    }

    // ---- a job's roster ----

    /// <summary>The ladder's line of strength on level (least squares over the cars a job may field): what a gear is estimated to be worth.</summary>
    private static readonly (double A, double B) Line = Fit();

    private static (double, double) Fit()
    {
        var e = Cars.Where(c => c.Enemy).ToList();
        if (e.Count < 2) return (0.2, 0.1);
        double mx = e.Average(c => c.Level), my = e.Average(c => c.Strength);
        double sxy = e.Sum(c => (c.Level - mx) * (c.Strength - my)), sxx = e.Sum(c => (c.Level - mx) * (c.Level - mx));
        double b = sxx > 0 ? sxy / sxx : 0;
        return (my - b * mx, b);
    }

    /// <summary>The strength in Piranhas the ladder's line gives a level.</summary>
    public static double StrengthAt(double level) => Line.A + Line.B * level;

    /// <summary>The level the challenge was calibrated at: the melee Piranha's (Back to Spanner's 1.0 is played in it).</summary>
    public static double Reference => Of("vppirna1")?.Level ?? 7.0;

    /// <summary>
    /// A job's budget for a player's gear: the ladder line's strength at the gear's level over the melee Piranha's, held to 0.7..1.5 (a player
    /// in a better car is set more against him, a starter less), so the challenge stays the player's rather than the Piranha's.
    /// </summary>
    public static double BudgetScale(Gear g) => Math.Clamp(StrengthAt(g.Level) / StrengthAt(Reference), 0.7, 1.5);

    /// <summary>
    /// The cars of one job (docs/remix-missions.md "Rosters and salvage"): each car the director sets on the player is drawn from the ladder
    /// around a centre at the player's gear (half a level under it at challenge 1.0, 2.5 levels on per 1.0 of challenge), 55 % up to two levels
    /// under the centre, 30 % up to one over, 15 % one to two and a half over (the ones worth salvaging); a heavier group (a roadblock, a late
    /// crew) one level on. Its own SplitMix64 stream from the job's seed, so the rest of the roll is the stock one's.
    /// </summary>
    public sealed class Picker
    {
        public Gear Gear { get; }
        public double Challenge { get; }
        public int Job { get; }
        private readonly Mix _r;
        /// <summary>The stems picked, in order.</summary>
        public List<string> Picked { get; } = [];

        public Picker(Gear gear, double challenge, int job, int seed)
        {
            Gear = gear; Challenge = challenge; Job = Math.Max(1, job);
            _r = new Mix(unchecked(seed * 0x5851F42D4C957F2DL + 0x14057B7EF767814FL));
        }

        public double Centre => Gear.Level - 0.5 + 2.5 * (Challenge - 1.0);

        /// <summary>A car for a group; <paramref name="step"/> 1 for a heavier one.</summary>
        public string Pick(int step = 0)
        {
            int u = _r.Next(100);
            double f = _r.Next(1000) / 1000.0;
            double want = (u < 55 ? Centre - 2 * f : u < 85 ? Centre + f : Centre + 1 + 1.5 * f) + step;
            var c = Near(want);
            Picked.Add(c.Stem);
            return c.Stem;
        }

        private Car Near(double want)
        {
            // the cars within three quarters of a level, at least the six nearest (the ladder's ends are thin)
            var near = Enemies.Where(c => Math.Abs(c.Level - want) <= 0.75).ToList();
            if (near.Count < 6) near = [.. Enemies.OrderBy(c => Math.Abs(c.Level - want)).ThenBy(c => c.Stem, StringComparer.Ordinal).Take(6)];
            return near[_r.Next(near.Count)];
        }

        /// <summary>Whether a job has a lead car a step above the rest: from the second job, or at challenge 1.0 and over.</summary>
        public bool HasBoss => Job >= 2 || Challenge >= 1.0;

        /// <summary>The lead's level: a level and a half over the centre, half a level more each job after the second.</summary>
        public double BossLevel => Centre + 1.5 + 0.5 * Math.Max(0, Job - 2);

        /// <summary>
        /// The lead car: among the cars near <see cref="BossLevel"/> (the eight nearest) one that carries a part at or above the player's in some
        /// slot (engine, suspension, brakes, wheels better than stock, its best gun), the nearest such first; its prize the part in the slot
        /// where it is furthest above the player's (engine, suspension, brakes, gun on a tie).
        /// </summary>
        public (string Stem, string Prize) Boss()
        {
            var by = Enemies.OrderBy(c => Math.Abs(c.Level - BossLevel)).ThenBy(c => c.Stem, StringComparer.Ordinal).ToList();
            var ok = by.Where(c => Margin(c).Best >= 0).ToList();
            var pool = (ok.Count > 0 ? ok : by).Take(Math.Min(3, Math.Max(1, (ok.Count > 0 ? ok : by).Count))).ToList();
            var car = pool[_r.Next(pool.Count)];
            var (slot, _) = Margin(car);
            Picked.Add(car.Stem);
            return (car.Stem, car.Key(slot));
        }

        /// <summary>The slot where a car's part is furthest above (or least below) the player's, and by how many levels.</summary>
        public (int Slot, int Best) Margin(Car c)
        {
            int best = int.MinValue, slot = 0;
            foreach (int k in (int[])[0, 1, 2, 4, 3])
            {
                if (k == 3 && c.Wheels <= 1) continue;   // stock tyres are nobody's prize
                int m = c.Gear.Slot(k) - Gear.Slot(k);
                if (m > best) { best = m; slot = k; }
            }
            return (slot, best);
        }

        /// <summary>The roster's strength, for the director's budget: the ladder's measured strength, the director's pools' for any other car.</summary>
        public static double Strength(string vcf) => Of(vcf) is { Enemy: true } c ? c.Strength : RemixChallenge.PoolStrength(vcf);
    }

    /// <summary>SplitMix64, the roster's own stream.</summary>
    private sealed class Mix(long seed)
    {
        private ulong _s = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL);

        public int Next(int n)
        {
            ulong z = _s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z % (ulong)Math.Max(1, n));
        }
    }
}
