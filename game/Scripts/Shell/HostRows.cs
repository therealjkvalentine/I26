namespace I76.Game.Shell;

/// <summary>
/// A host form's game type (the play fork's): each keeps the rows it was last hosted with apart from the others, in melee.cfg's
/// [host_melee], [host_race], [host_ctf] and [host_coop]. The base game's melee and the Nitro Pack's Combat are one type; the Nitro
/// Pack's CTF 2, 3 and 4 are one.
/// </summary>
public enum HostType { Melee, Race, Ctf, Coop }

/// <summary>
/// The rows a game type's host form was last hosted with (BROADCAST GAME, or a co-op lobby's APPLY). Each is null until that type has kept
/// it: the form then shows what it would have without (melee.cfg's older keys on opening, the form's values on a switch of type, else
/// the original's defaults). The password is never kept (it lives as long as the form). Hidden Weapons &amp; Cars, Share Maps and the
/// host's rules kept in shell.cfg (Car Reset, CB Radio, Ignition Stays On, Handicaps, Chase Crosshair, Turrets, Extra Enemies, Damage
/// Taken) are one value for every type, as they were: Play Options and the co-op lobby read and change the same settings.
/// </summary>
public sealed class HostRows
{
    public int? Drivers { get; set; }
    public bool? TeamPlay { get; set; }
    public bool? InviteOnly { get; set; }
    /// <summary>The base game's MAX SCORE: its box and its value.</summary>
    public bool? ScoreOn { get; set; }
    public int? MaxScore { get; set; }
    /// <summary>The Nitro form's LIMIT by its name (Points, Kills, Laps, Captures, Minutes, No limit) and its value (not an index: the lists may change).</summary>
    public string? Limit { get; set; }
    public int? LimitValue { get; set; }
    public int? Bots { get; set; }
    public int? BotSkill { get; set; }
    public List<string>? BotCars { get; set; }
    /// <summary>SIM RATE in Hz.</summary>
    public int? Rate { get; set; }
    /// <summary>UNLIM. TURRETS.</summary>
    public bool? Turrets { get; set; }
    /// <summary>WEAPON RESTRICTIONS as <see cref="I76.Sim.Trip.WeaponRestrictions.ToSpec"/> writes them ("" for none).</summary>
    public string? Restrictions { get; set; }

    public const int MinScore = 1000, MaxScoreTop = 100000, DefaultMaxScore = 20000;

    /// <summary>The section a type's rows are in.</summary>
    public static string Section(HostType t) => t switch { HostType.Race => "host_race", HostType.Ctf => "host_ctf", HostType.Coop => "host_coop", _ => "host_melee" };

    /// <summary>The type a game is: a co-op form's, else by the melee's kind.</summary>
    public static HostType Of(bool coop, I76.Sim.Missions.MeleeKind kind) =>
        coop ? HostType.Coop : kind switch { I76.Sim.Missions.MeleeKind.Race => HostType.Race, I76.Sim.Missions.MeleeKind.Ctf => HostType.Ctf, _ => HostType.Melee };

    /// <summary>Whether no row is kept.</summary>
    public bool IsEmpty => Drivers is null && TeamPlay is null && InviteOnly is null && ScoreOn is null && MaxScore is null && Limit is null && LimitValue is null
        && Bots is null && BotSkill is null && BotCars is null && Rate is null && Turrets is null && Restrictions is null;

    /// <summary>These rows with those <paramref name="newer"/> keeps laid over them (the base game's form keeps MAX SCORE, the Nitro form LIMIT: neither drops the other's).</summary>
    public void Merge(HostRows newer)
    {
        Drivers = newer.Drivers ?? Drivers;
        TeamPlay = newer.TeamPlay ?? TeamPlay;
        InviteOnly = newer.InviteOnly ?? InviteOnly;
        ScoreOn = newer.ScoreOn ?? ScoreOn;
        MaxScore = newer.MaxScore ?? MaxScore;
        Limit = newer.Limit ?? Limit;
        LimitValue = newer.LimitValue ?? LimitValue;
        Bots = newer.Bots ?? Bots;
        BotSkill = newer.BotSkill ?? BotSkill;
        BotCars = newer.BotCars is { } c ? [.. c] : BotCars;
        Rate = newer.Rate ?? Rate;
        Turrets = newer.Turrets ?? Turrets;
        Restrictions = newer.Restrictions ?? Restrictions;
    }

    public HostRows Copy()
    {
        var r = new HostRows();
        r.Merge(this);
        return r;
    }

    /// <summary>A type's rows as a file has them: each key only if it is there, held to what the form offers.</summary>
    public static HostRows Read(SettingsFile c, HostType t)
    {
        string s = Section(t);
        bool Has(string k) => c.HasSectionKey(s, k);
        var r = new HostRows();
        if (Has("drivers")) r.Drivers = MeleeSetup.HostDriversFrom((int)c.GetValue(s, "drivers"));
        if (Has("team_play")) r.TeamPlay = (bool)c.GetValue(s, "team_play");
        if (Has("invite_only")) r.InviteOnly = (bool)c.GetValue(s, "invite_only");
        if (Has("score_on")) r.ScoreOn = (bool)c.GetValue(s, "score_on");
        if (Has("max_score")) r.MaxScore = ScoreFrom((int)c.GetValue(s, "max_score"));
        if (Has("limit") && ((string)c.GetValue(s, "limit")).Trim() is { Length: > 0 } l) r.Limit = l;
        if (Has("limit_value")) r.LimitValue = Math.Max(0, (int)c.GetValue(s, "limit_value"));
        if (Has("bots")) r.Bots = Math.Clamp((int)c.GetValue(s, "bots"), 0, 63);
        if (Has("bot_skill")) r.BotSkill = Math.Clamp((int)c.GetValue(s, "bot_skill"), 0, 2);
        if (Has("bot_cars"))
        {
            var cars = ((string)c.GetValue(s, "bot_cars")).Split(',').Select(x => x.Trim()).Take(63).ToList();
            while (cars.Count > 0 && cars[^1].Length == 0) cars.RemoveAt(cars.Count - 1);
            r.BotCars = cars;
        }
        if (Has("rate") && I76.Sim.Session.SimRate.IsOffered((int)c.GetValue(s, "rate"))) r.Rate = (int)c.GetValue(s, "rate");
        if (Has("turrets")) r.Turrets = (bool)c.GetValue(s, "turrets");
        // a list this build cannot read is no list (nothing restricted, as a fresh form)
        if (Has("restrictions"))
            r.Restrictions = I76.Sim.Trip.WeaponRestrictions.Parse((string)c.GetValue(s, "restrictions"), null, out _) is { } w ? (w.IsEmpty ? "" : w.ToSpec()) : "";
        return r;
    }

    /// <summary>The rows kept into a file (only those kept: a type never hosted writes no section).</summary>
    public void Write(SettingsFile c, HostType t)
    {
        string s = Section(t);
        if (Drivers is { } d) c.SetValue(s, "drivers", d);
        if (TeamPlay is { } tp) c.SetValue(s, "team_play", tp);
        if (InviteOnly is { } io) c.SetValue(s, "invite_only", io);
        if (ScoreOn is { } so) c.SetValue(s, "score_on", so);
        if (MaxScore is { } ms) c.SetValue(s, "max_score", ms);
        if (Limit is { } l) c.SetValue(s, "limit", l);
        if (LimitValue is { } lv) c.SetValue(s, "limit_value", lv);
        if (Bots is { } b) c.SetValue(s, "bots", b);
        if (BotSkill is { } bs) c.SetValue(s, "bot_skill", bs);
        if (BotCars is { } bc) c.SetValue(s, "bot_cars", string.Join(",", bc));
        if (Rate is { } rt) c.SetValue(s, "rate", rt);
        if (Turrets is { } tu) c.SetValue(s, "turrets", tu);
        if (Restrictions is { } re) c.SetValue(s, "restrictions", re);
    }

    /// <summary>MAX SCORE held to the form's range and its steps of 1000 (1000 to 100000).</summary>
    public static int ScoreFrom(int stored) => Math.Clamp((int)Math.Round(stored / 1000.0) * 1000, MinScore, MaxScoreTop);

    /// <summary>
    /// The LIMIT row's choice and value index for a kept limit: the limit of that name among <paramref name="names"/> (else the first, the
    /// form's default), and its value among that limit's <paramref name="values"/> (else the first, the limit's default).
    /// </summary>
    public static (int Limit, int At) LimitFrom(IReadOnlyList<string> names, Func<int, int[]> values, string? name, int? value)
    {
        int l = name is null ? -1 : names.ToList().FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (l < 0) return (0, 0);
        int at = value is { } v ? Array.IndexOf(values(l), v) : 0;
        return (l, Math.Max(0, at));
    }
}
