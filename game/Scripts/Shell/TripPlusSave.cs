using I76.Sim.Trip;

namespace I76.Game.Shell;

/// <summary>
/// trip.cfg's Trip+ part (the play fork's; docs/deviations.md "Trip+"): whether the trip has been finished (a T17 win, [trip]
/// finished), and the [trip_plus] section: the lap being played (1 the original's trip), the laps finished, and the three rule
/// values the lap plays by. A file without them (every file written before Trip+) reads as an unfinished trip on lap 1, rules
/// off. Kept apart from <see cref="TripState"/>'s file handling so it reads and writes without the engine.
/// </summary>
/// <param name="Finished">A T17 win has been recorded (any lap).</param>
/// <param name="Lap">The lap being played: 1 the original's trip, 2 on Trip+.</param>
/// <param name="LapsDone">The highest lap whose T17 was won (0 none).</param>
/// <param name="Rules">The rules this lap plays by; null on lap 1.</param>
public sealed record TripPlusSave(bool Finished = false, int Lap = 1, int LapsDone = 0, TripPlusRules? Rules = null)
{
    public const string Section = "trip_plus";

    /// <summary>The rules a launch takes: this lap's, null when they change nothing.</summary>
    public TripPlusRules? Active => Lap > 1 && Rules is { IsOff: false } r ? r : null;

    /// <summary>The values to write, as (section, key, value); the Trip+ section only once the trip has a lap past the first or a lap done.</summary>
    public IEnumerable<(string Section, string Key, object Value)> Entries()
    {
        if (Finished) yield return ("trip", "finished", true);
        if (Lap <= 1 && LapsDone <= 0) yield break;
        yield return (Section, "lap", Lap);
        yield return (Section, "laps_done", LapsDone);
        if (Rules is { } r)
        {
            yield return (Section, "copies", r.Copies);
            yield return (Section, "skill", r.SkillOffset);
            yield return (Section, "salvage", r.SalvageOffset);
        }
    }

    /// <summary>The part as a file holds it: <paramref name="get"/> gives a key's value (a boxed number or bool) or null when the file has none.</summary>
    public static TripPlusSave Read(Func<string, string, object?> get)
    {
        static int Int(object? o, int fallback) => o switch
        {
            null => fallback,
            bool b => b ? 1 : 0,
            IConvertible c => System.Convert.ToInt32(c, System.Globalization.CultureInfo.InvariantCulture),
            _ => fallback,
        };
        bool finished = get("trip", "finished") is bool fb ? fb : Int(get("trip", "finished"), 0) != 0;
        int lap = System.Math.Max(1, Int(get(Section, "lap"), 1));
        int done = System.Math.Max(0, Int(get(Section, "laps_done"), 0));
        TripPlusRules? rules = null;
        if (lap > 1 && get(Section, "copies") is not null)
            rules = new TripPlusRules(System.Math.Clamp(Int(get(Section, "copies"), 0), 0, I76.Sim.Missions.CoopSetup.MaxExtraEnemies),
                System.Math.Clamp(Int(get(Section, "skill"), 0), -4, 4), System.Math.Clamp(Int(get(Section, "salvage"), 0), 0, 40));
        return new TripPlusSave(finished || done > 0, lap, done, rules);
    }

    /// <summary>A T17 win: the trip is finished, and this lap is done.</summary>
    public TripPlusSave Won17() => this with { Finished = true, LapsDone = System.Math.Max(LapsDone, Lap) };

    /// <summary>The lap TRIP+ starts: the one after the highest done (lap 2 after the original's trip), to <see cref="TripPlusRules.MaxLap"/>.</summary>
    public int NextLap => System.Math.Min(TripPlusRules.MaxLap, System.Math.Max(1, LapsDone) + 1);

    /// <summary>Playing lap <paramref name="lap"/> by the table's rules (<see cref="TripPlusRules.ForLap"/>); lap 1 is the original's trip.</summary>
    public TripPlusSave OnLap(int lap) => this with { Lap = System.Math.Max(1, lap), Rules = TripPlusRules.ForLap(lap) };

    /// <summary>
    /// The developer hook I76_TRIP_PLUS=LAP (docs/dev-hooks.md): that lap's rules, as if the laps before it were finished; null for
    /// no hook or a value that is not a lap past the first.
    /// </summary>
    public TripPlusSave? Hooked(string? value) =>
        int.TryParse(value, out int lap) && lap >= 2 ? OnLap(lap) with { Finished = true, LapsDone = System.Math.Max(LapsDone, lap - 1) } : null;
}
