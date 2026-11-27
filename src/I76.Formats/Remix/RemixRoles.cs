namespace I76.Formats.Remix;

/// <summary>
/// Who is who in a remix description, by template: the cars the script sets on the player (they must be on the enemy's side,
/// so the target keys cycle to them and the radar shows them as a stock mission's enemies are), and where each is meant to be
/// when the mission starts. The builder gives every hostile car the enemy's team; the headless check (i76 remix check, the
/// tests) holds a built mission to both.
/// </summary>
public static class RemixRoles
{
    /// <summary>The ODEF team bits of the sides ((flags >> 16) &amp; 7): 1 the player's, 2 the enemy's (every stock mission's).</summary>
    public const int PlayerTeam = 1, EnemyTeam = 2;

    /// <summary>A car's part in the job: its name (a cars or bind entry, or a stock actor key) and what it does.</summary>
    public sealed record Role(string Car, string Part);

    /// <summary>Every car the template turns on the player, each once, in the description's order.</summary>
    public static List<Role> Hostiles(RemixSpec spec)
    {
        var o = spec.Objective;
        var l = new List<Role>();
        void Add(string car, string part) { if (car.Length > 0 && !l.Any(r => r.Car == car)) l.Add(new Role(car, part)); }
        foreach (var w in o.Waves) foreach (var c in w.Cars) Add(c, "raider");
        foreach (var s in o.Sleepers) Add(s.Car, "sleeper");
        if (o.Pursuit is { } p) foreach (var c in p.Cars) Add(c, "pursuer");
        if (o.Convoy is { } cv) { Add(cv.Lead, "lead"); foreach (var e in cv.Escorts) Add(e, "escort"); }
        foreach (var r in o.Reinforce) foreach (var c in r.Cars) Add(c, "reinforcement");
        foreach (var t in o.Targets) if (spec.Cars.ContainsKey(t)) Add(t, "target");
        return l;
    }

    /// <summary>The cars on the player's side: an escort's escorted car and its wingmen.</summary>
    public static List<Role> Friendlies(RemixSpec spec)
    {
        var l = new List<Role>();
        if (spec.Objective.Escort is { } e)
        {
            if (e.Car.Length > 0) l.Add(new Role(e.Car, "escorted"));
            foreach (var w in e.Wingmen) l.Add(new Role(w, "wingman"));
        }
        return l;
    }
}
