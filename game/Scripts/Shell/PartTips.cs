using System.Globalization;
using I76.Formats.Bwd2;
using I76.Formats.Vfs;
using I76.Sim.Damage;
using I76.Sim.Trip;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;

namespace I76.Game.Shell;

/// <summary>
/// One part's tooltip (the port's: the original's garage had no help): what it does in the game, a second line of its numbers, and
/// where each number came from (for the read-through table; never shown).
/// </summary>
public sealed record PartTip(string Effect, string? Numbers, string Source)
{
    /// <summary>The tooltip's text: the effect, then the numbers on a line of their own.</summary>
    public string Text => Numbers is null ? Effect : Effect + "\n" + Numbers;
}

/// <summary>
/// The garage's equipment tooltips (the port's): for every part the BUILD AND REPAIR form, its pickers, the CAR / VAN INVENTORY,
/// FIELD SALVAGE, the chassis forms and the melee and scenario forms can list, what it does in this game's simulation. The numbers
/// are read the way the simulation reads them, from the same files and constants (the engines, brakes and suspensions through
/// <see cref="VehicleFactory.ReadComponents"/>, the tyres through <see cref="ChassisModel.WheelStatsOf"/>, the weapons through
/// <see cref="GizmoDef.Read"/>, the specials from <see cref="Specials"/> and <see cref="I76.Sim.Damage.SpecialPart"/>), so a tooltip
/// cannot drift from the game; only the words are written here. Nothing here touches the engine (the tests build it without one).
/// </summary>
public sealed class PartTips
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly AssetVfs? _vfs;
    private readonly bool _nitro;
    private readonly List<VehicleFactory.Component> _engines = [], _brakes = [], _susps = [];
    private readonly Dictionary<string, PartTip?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The game's tooltips: <paramref name="vfs"/> is the install the parts come from (null: the specials, paint and the .45 only).</summary>
    public PartTips(AssetVfs? vfs, bool nitro)
    {
        _vfs = vfs;
        _nitro = nitro;
        if (vfs?.TryRead("compnent.cdf") is not null)
        {
            var (e, b, s) = VehicleFactory.ReadComponents(vfs);
            _engines.AddRange(e); _brakes.AddRange(b); _susps.AddRange(s);
        }
    }

    /// <summary>A part's tooltip, or null for none (a part type the lists never show).</summary>
    public PartTip? For(PartDef p)
    {
        if (p.IsEmpty) return Empty;
        string key = $"{p.Type}:{p.Stem}:{p.Ordinal}:{p.Id}";
        if (_cache.TryGetValue(key, out var hit)) return hit;
        var tip = p.Type switch
        {
            // the parts database's types (PartsDatabase): 2 engine, 3 suspension, 4 brakes, 5 tyre, 7 weapon, 8 dropper, 9 handgun, 13 special, 14 paint
            2 => Engine(p),
            3 => Suspension(p),
            4 => Brakes(p),
            5 => Tyre(p),
            7 or 8 or 9 => Weapon(p),
            13 => Special(p.Ordinal + 1, _nitro),
            14 => Paint,
            _ => null,
        };
        _cache[key] = tip;
        return tip;
    }

    /// <summary>The tooltip's text for a part (null for none).</summary>
    public string? Text(PartDef? p) => p is null ? null : For(p)?.Text;

    /// <summary>The tooltip of a part a form lists by its display name (the melee and scenario forms' PARTS and weapons).</summary>
    public string? TextByName(PartsDatabase db, string name, params int[] types)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (int t in types)
            if (db.ById(name.Trim(), t) is { } p) return Text(p);
        return null;
    }

    public static readonly PartTip Empty = new("Nothing fitted here.", null, "hand-written");
    public static readonly PartTip Paint = new("A paint scheme. Looks only: no effect on the car.", null, "hand-written (paint is a .vtf skin; the sim never reads it)");

    // ---- numbers as the forms print them ----

    private static string N(float x) => MathF.Abs(x - MathF.Round(x)) < 0.005f ? ((int)MathF.Round(x)).ToString("#,0", Inv) : x.ToString("0.##", Inv);
    private static string X(float x) => x.ToString("0.0#", Inv) + "x";
    private static string Lbs(float w) => $"{N(w)} lbs";

    // ---- the components (compnent.cdf through the vehicle factory) ----

    private PartTip? Engine(PartDef p)
    {
        int id = _vfs is null ? p.Ordinal : VehicleFactory.EngineCompId(_vfs, p.Ordinal);
        if (_engines.ElementAtOrDefault(id) is not { } e) return null;
        var first = _engines[0];
        string than = id == 0 ? "the base engine" : $"{X(e.A / first.A)} the base one";
        return new($"Engine: drive force {N(e.A)}, {than}.",
            $"{e.Hp} hit points, {Lbs(e.B)}. Hit, never under 40 % power for you.",
            $"compnent.cdf ENGN #{id} (engsnd.dat ENG COMP ID of ENG NUM {p.Ordinal}): +4 force {e.A.ToString(Inv)}, +0 hp {e.Hp}, +8 mass {e.B.ToString(Inv)}; the player's floor 0.4 is VehicleDamage.Create's");
    }

    private PartTip? Brakes(PartDef p)
    {
        if (_brakes.ElementAtOrDefault(p.Ordinal) is not { } b) return null;
        return new($"Brakes: {X(b.A)} braking in Instant Melee and network games.",
            $"Missions ignore it: only its {Lbs(b.B)} count. {b.Hp} hit points.",
            $"compnent.cdf BRAK #{p.Ordinal}: +4 strength {b.A.ToString(Inv)} (VehicleFactory.Create, meleeBrakes), +0 hp {b.Hp}, +8 mass {b.B.ToString(Inv)}");
    }

    private PartTip? Suspension(PartDef p)
    {
        if (_susps.ElementAtOrDefault(p.Ordinal) is not { } s) return null;
        var first = _susps[0];
        string vs = p.Ordinal == 0 || first.A == 1f ? "" : $" ({X(s.A / first.A)} the stock)";
        string crash = s.B > 0 ? $"Crashes count {N(s.B)} % less on it. " : "";
        return new($"Suspension: corners at {X(s.A)}{vs} before the car slides.",
            $"{crash}{s.Hp} hit points, {Lbs(s.C)}.",
            $"compnent.cdf SUSP #{p.Ordinal}: +4 cornering {s.A.ToString(Inv)} (ChassisGrip), +8 percent {s.B.ToString(Inv)} (impact scale (100 - p) %), +0 hp {s.Hp}, +0xc mass {s.C.ToString(Inv)}; the 0.5 floor is SuspensionPart's");
    }

    private PartTip? Tyre(PartDef p)
    {
        if (_vfs?.TryRead(p.Stem) is not { } d || ChassisModel.WheelStatsOf(d) is not { } w) return null;
        bool bonus = w.Grip > 1.001f;
        return new(bonus ? $"Tyres: grip {X(w.Grip)} on any ground, until the tyre is hit." : $"Tyres: grip {X(w.Grip)} on any ground; a hit tyre grips less.",
            bonus ? $"Then its health sets the grip. {w.Hp} hit points, {Lbs(2 * w.Mass)} a pair." : $"{w.Hp} hit points a tyre, {Lbs(2 * w.Mass)} a pair.",
            $"{p.Stem} WDFC: +0x30 grip {w.Grip.ToString(Inv)}, +0x28 hp {w.Hp}, +0x2c mass {w.Mass.ToString(Inv)} a wheel (x2 a pair, VehicleFactory.Create); the hit resets grip to hp/max (WheelPart.Apply)");
    }

    // ---- the weapons (the .gdf through GizmoDef, as the armory mounts them) ----

    private static string Channels(uint mask)
    {
        var l = new List<string>();
        if ((mask & 1) != 0) l.Add("bullet");
        if ((mask & 2) != 0) l.Add("blast");
        if ((mask & 4) != 0) l.Add("fire");
        if ((mask & 8) != 0) l.Add("crash");
        return l.Count == 0 ? "" : string.Join("/", l);
    }

    private PartTip? Weapon(PartDef p)
    {
        if (_vfs?.TryRead(p.Stem) is not { } d || GizmoDef.Read(p.Stem, d) is not { } g) return null;
        var src = new List<string> { $"{p.Stem} GDFC: +0x2c damage {g.Damage}, +0x4e rate {g.Rate.ToString(Inv)}, +0x52 burst {g.Burst}, +0x4a delay {g.Delay.ToString(Inv)}, +0x56 speed {g.Speed.ToString(Inv)}, +0x46 life {g.Life.ToString(Inv)}, +0x5e ammo {g.Ammo}, +0x30 hp {g.HitPoints}, +0x34 mass {g.Mass.ToString(Inv)}" };
        uint mask = g.Ordnance?.Mask ?? 0;
        // the vehicle impact's effect (the impact table's second entry) carries the blast that goes off around the hit
        EffectDef? blast = null;
        if (g.Ordnance is { } o && o.ImpactXdf.Length > 1 && o.ImpactXdf[1] is { Length: > 0 } fx && _vfs.TryRead(fx) is { } xd && EffectDef.Read(fx, xd) is { SplashDamage: > 0 } e)
        {
            blast = e;
            src.Add($"{fx} XDFC: +0x1c splash {e.SplashDamage.ToString(Inv)}, +0x20 radius {e.SplashRadius.ToString(Inv)}");
        }
        string ch = Channels(mask);
        bool lobbed = (g.Ordnance?.Gravity ?? 0) != 0;
        // the interval between shots: 1 / rate, or the pause after a burst (Combat's fire control)
        string rate = g.Rate <= 0 ? "" : g.Rate >= 1 ? $"{N(g.Rate)} a second" : $"one every {N(1 / g.Rate)} s";
        if (g.Burst > 1) rate = $"bursts of {g.Burst}" + (g.Delay > 0 ? $", {N(g.Delay)} s apart" : $" at {N(g.Rate)} a second");
        else if (g.Delay > 0 && g.Rate > 0 && g.Delay > 1 / g.Rate) rate = $"one every {N(g.Delay)} s";
        string what = g.Class switch
        {
            1 => "Handgun",
            5 => "Fire stream",
            6 => "Dropped behind",
            3 => "Mortar",
            4 => g.Kind switch { 3 => "Heat seeker", 8 => "Radar missile", 0x14 => "Lock-on missile", _ => "Rockets" },
            _ => "Gun",
        };
        string dmg = g.Damage > 0 ? $"{g.Damage} {ch} damage".Replace("  ", " ") : "no damage";
        if (g.Class == 6 && g.Damage == 0) { dmg = "no damage; a car on it loses its grip"; rate = ""; }
        // the Nitro Pack's caltrop: no damage channel; its probe blows the nearest tyre of a car over it
        if (g.Class == 6 && mask == 0 && g.Damage > 0)
        {
            dmg = "blows a tyre of the car that drives over it";
            rate = "";
            src.Add("DamageModel.CaltropTyreHit: the nearest wheel to 25 of 100 hit points, blown");
        }
        // the Nitro Pack's Chemical Mortar (ordnance kind 0x16): a hit that reaches the parts kills the driver outright
        bool chemical = _nitro && g.Kind == 0x16;
        if (chemical)
        {
            dmg = "a hit kills the driver outright, armor or not";
            src.Add("VehicleDamage.ComponentLoop: packet flag 0x10 (kind 0x16), the Nitro Pack only, after the Cup Holders' roll");
        }
        // the sim's own reach for a round that flies (Combat.Reach: its speed toward the ordnance's terminal speed over its life);
        // a lobbed shell's depends on the aim, a dropper's round lies where it fell, the fire stream is an emitter's
        float reachM = Combat.Reach(g);
        string reach = lobbed || g.Class is 5 or 6 || reachM <= 0 ? "" : $", reach {N(MathF.Round(reachM / 10) * 10)} m";
        if (reach.Length > 0) src.Add($"Combat.Reach {reachM.ToString("0.#", Inv)} m (ORDF terminal speed {g.Ordnance!.TerminalSpeed.ToString(Inv)})");
        // a blast of a point or two (the missiles' xmslc1) is left out of the words: it is in the source line
        string boom = blast is null || blast.SplashDamage < 10 || chemical ? "" : $" + {N(blast.SplashDamage)} within {N(blast.SplashRadius)} m";
        string line1 = $"{what}: {dmg}{boom}, {rate}{reach}.".Replace(", .", ".").Replace(":, ", ": ");
        string ammo = g.Ammo >= 0x0fffffff ? "Never runs dry" : $"{g.Ammo} rounds";
        string turret = p.IsTurret ? " Roof turret: aims itself." : "";
        string line2 = $"{ammo}, {g.HitPoints} hit points, {Lbs(g.Mass)}.{turret}";
        return new(line1, line2, string.Join("; ", src));
    }

    /// <summary>The driver's .45 (Armory.HandgunDef: built in, no file).</summary>
    public static PartTip Handgun
    {
        get
        {
            var g = Armory.HandgunDef(1);
            float reach = Combat.Reach(g);
            return new($"Driver's .45: 5 points a hit, {N(g.Rate)} a second, reach {N(MathF.Round(reach / 10) * 10)} m.",
                "On a car under a third whole, it hits the driver. Never runs dry.",
                $"Armory.HandgunDef: rate {g.Rate.ToString(Inv)}, Combat.Reach {reach.ToString("0.#", Inv)} m, ammo 0x0fffffff; DamageModel.Apply: a kind-0x12 round does 5 to a car over 33 % health, else it goes for the driver (hand-written: literals there)");
        }
    }

    // ---- the specials (Specials, SpecialPart, VehicleDamage, DamageModel) ----

    private static string P(float f) => N(MathF.Round(f * 100f)) + " %";
    private static string F(float f) => f.ToString("0.0#", Inv);

    /// <summary>
    /// A special by its type (spc01 = 1 ... spc11 = 11). Every one is a 10-point part; shot off it stops working, bar the ones set as the
    /// car is built (the blower, Heated Seats, and the Nitro Pack's Dashboard Mary and Tire Covers) and a latched X-Aust Brake.
    /// </summary>
    public static PartTip? Special(int type, bool nitro) => type switch
    {
        1 => new($"Radar Jammer: blanks radar within {N(Combat.ContactRange)} m, shakes radar missiles.",
            $"Keyed: {SpecialPart.FullCharges(1)} uses of {N(Specials.JamSeconds)} s. Pressed while on, it still spends one.",
            "SpecialPart.FullCharges(1); Specials.JamSeconds; Combat.ContactRange (Combat.RadarBlank), Combat.AiRadarBlind, a radar missile drops a jammed target"),
        2 => new($"Nitrous Oxide: keyed, {N(Specials.NitroForce)} times the drive force for {N(Specials.NitroSeconds)} s.",
            $"{SpecialPart.FullCharges(2)} charges. No quicker than the rear tyres can grip.",
            "Specials.NitroForce, Specials.NitroSeconds (VehicleStep, AiDriver); SpecialPart.FullCharges(2)"),
        3 => new($"Blower: always on, engine power x{Specials.BlowerScale.ToString("0.00", Inv)}.",
            "Works on even after it is shot off.",
            "Specials.BlowerScale (Specials.AtLoad -> VehicleParams.EngineScale)"),
        4 => new($"X-Aust Brake: press once, the brakes bite {N(Specials.XAustBrakeScale)}x as hard.",
            "Stays on all mission. No harder than the tyres can grip.",
            "Specials.XAustBrakeScale (Specials.Activate type 4 latches it; VehicleStep scales a negative pedal)"),
        5 => new($"Structo Bumper: front and rear chassis stand crashes {N(Specials.StructoChassis)}x as well.",
            "Collisions only; armor is not touched.",
            "Specials.StructoChassis (VehicleDamage.ArmourStep: zones front and rear, the impact channel)"),
        6 => new($"Curb Feelers: armor stands bullets and blasts {F(Specials.CurbFeelersArmour)}x as well.",
            $"On every side: about {P(1 - 1 / Specials.CurbFeelersArmour)} less armor lost a hit.",
            "Specials.CurbFeelersArmour (VehicleDamage.ArmourStep: armor wears damage / 1.1)"),
        7 => new($"Mud Flaps: chassis stands crashes {F(1 + Specials.MudFlapsChassis)}x as well, every side.",
            $"{F(Specials.StructoChassis + Specials.MudFlapsChassis)}x front and rear with a Structo Bumper.",
            "Specials.MudFlapsChassis, added to the chassis multiplier (VehicleDamage.ArmourStep)"),
        8 => new($"Heated Seats: every mounted gun carries {P(Specials.TenthMore - 1)} more rounds.",
            "Rounded down. The driver's .45 is left as it is.",
            "Specials.TenthMore via Specials.PlusTenth (Specials.AtLoad type 8; CarState.Apply for carried rounds)"),
        9 => new($"Cup Holders: one hit in {100 / Specials.CupHoldersPercent} past the armor spares the parts.",
            "Holds a cup, too. Armor and chassis still take their share.",
            "Specials.CupHoldersPercent (VehicleDamage.ComponentLoop: rand() % 100 < 10 clears the hit's channels)"),
        10 => nitro
            ? new($"Dashboard Mary: every mounted gun hits {P(Specials.TenthMore - 1)} harder.",
                "Rounded down, set as the car is built.", "Specials.TenthMore via Specials.PlusTenth (Specials.AtLoad type 10, the Nitro Pack only)")
            : new("Dashboard Mary: watches over you. No effect on the car.", null, "Specials.AtLoad: type 10 acts in the Nitro Pack only"),
        11 => nitro
            ? new($"Tire Covers: every tyre has {Specials.TireCoversWheelHp}x the hit points.",
                $"A caltrop takes only {P(1 - Specials.TireCoversCaltropKeeps)} of a covered tyre.",
                "Specials.TireCoversWheelHp (Specials.AtLoad type 11), Specials.TireCoversCaltropKeeps (DamageModel.CaltropTyreHit), the Nitro Pack only")
            : new("Tire Covers: keep the weather off. No effect on the car.", null, "Specials.AtLoad: type 11 acts in the Nitro Pack only"),
        _ => null,
    };

    // ---- the DEFENSE block (the form's armor and chassis reinforcement) ----

    public const string ArmourTip = "Armor: stops bullets, blasts and fire on this side.\nShot through, hits reach the parts. Doubled for you off-line.";
    public const string ChassisTip = "Chassis reinforcement: soaks up crashes on this side.\nWorn through, crashes reach the parts. Doubled for you off-line.";
    public const string PoolTip = "Lbs. available: armor and chassis not yet on a side.\nBalance shares it all out evenly.";
}
