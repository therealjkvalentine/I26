namespace I76.Formats.Remix;

/// <summary>
/// The original's salvage tier table (docs/trip.md "Field salvage": the clamp each salvaged part goes through), per salvage type: where
/// in the part's file key the entry names are compared, and the entries in table order, each with the trip mission level and the melee
/// level from which it is let through ("always" rows start a family). The table is the engine's; <c>SalvagePool.Clamp</c> walks it.
/// The roguetrip also reads it as a ladder (docs/remix-missions.md "Rosters and salvage"): a part's trip level is how far into the trip
/// the original first lets it out of a wreck, so a car's parts' levels say how far up the game's own ladder the car is.
/// </summary>
public static class PartTiers
{
    public sealed record Tier(string Name, int TripMin, int MeleeMin, bool Always);

    /// <summary>Salvage types: 0 wheel (.wdf, the letter at 7), 1 weapon (.gdf, by stem), 2 engine, 3 suspension, 4 brakes (eng/sus/bra NN), 5 special (spc NN).</summary>
    public static readonly (int Offset, Tier[] Entries)[] Tables =
    [
        (7, [new("a", 1, 1, true), new("b", 3, 1, false), new("c", 7, 3, false), new("d", 11, 5, false)]),
        (0, [
            new("gmlight", 1, 1, true), new("gmmedium", 3, 1, false), new("gclight", 5, 2, false), new("gcmedium", 7, 3, false), new("gmheavy", 9, 4, false), new("gcheavy", 12, 6, false), new("gchades", 15, 7, false),
            new("tmlight", 7, 3, true), new("tmmedium", 7, 3, false), new("tclight", 14, 7, false), new("tcmedium", 14, 7, false), new("tmheavy", 9, 4, false), new("tcheavy", 14, 7, false), new("tchades", 15, 7, false),
            new("gdumb", 1, 1, true), new("gsheat", 5, 2, false), new("gsradar", 10, 5, false), new("gscherub", 14, 7, false),
            new("tdumb", 9, 4, true), new("tsheat", 9, 4, false), new("tsradar", 10, 5, false), new("tscherub", 14, 7, false),
            new("gflight", 2, 1, true), new("gfmedium", 4, 2, false), new("gfheavy", 8, 4, false), new("gfpyro", 12, 6, false),
            new("tflight", 14, 7, true), new("tfmedium", 14, 7, false), new("tfheavy", 14, 7, false), new("tfpyro", 14, 7, false),
            new("ggrenade", 2, 1, true), new("gwhiteph", 6, 3, false), new("gcluster", 10, 5, false), new("gezkill", 13, 6, false),
            new("goilslck", 1, 1, true), new("gblox", 4, 2, false), new("glandmin", 9, 4, false), new("gfirdrop", 13, 6, false), new("gceracer", 16, 7, false),
        ]),
        (3, [new("01", 1, 1, true), new("02", 5, 2, false), new("03", 9, 4, false), new("04", 13, 6, false)]),
        (3, [new("01", 1, 1, true), new("02", 4, 2, false), new("03", 8, 4, false), new("04", 12, 6, false)]),
        (3, [new("01", 1, 1, true), new("02", 2, 1, false), new("03", 6, 3, false), new("04", 10, 5, false)]),
        (3, [new("05", 1, 1, true), new("02", 3, 1, false), new("04", 5, 2, false), new("07", 6, 3, false), new("01", 7, 3, false),
             new("06", 8, 4, false), new("09", 10, 5, false), new("03", 11, 5, false), new("08", 13, 6, false)]),
    ];

    /// <summary>The type from the file key: .wdf 0 wheel, .gdf 1 weapon, eng… 2, sus… 3, bra… 4, spc… 5; −1 unknown.</summary>
    public static int TypeOf(string path)
    {
        var p = path.ToLowerInvariant();
        if (p.EndsWith(".wdf")) return 0;
        if (p.EndsWith(".gdf")) return 1;
        if (p.Contains('.')) return -1;
        return p.StartsWith("eng") ? 2 : p.StartsWith("sus") ? 3 : p.StartsWith("bra") ? 4 : p.StartsWith("spc") ? 5 : -1;
    }

    /// <summary>The table entry a file key matches, or null (a part the table does not know: a tank's gun, a story car's engine number).</summary>
    public static Tier? Of(string key)
    {
        int type = TypeOf(key);
        if (type < 0) return null;
        var (off, entries) = Tables[type];
        var name = key.ToLowerInvariant();
        if (name.Length < off) return null;
        return Array.Find(entries, e => string.CompareOrdinal(name, off, e.Name, 0, e.Name.Length) == 0);
    }

    /// <summary>A part's trip level (1 the first mission's .. 16), 0 for one the table does not know.</summary>
    public static int Level(string key) => Of(key)?.TripMin ?? 0;
}
