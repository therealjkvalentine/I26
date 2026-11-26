namespace I76.Sim.Missions;

/// <summary>
/// Smarter opponents (the play fork's; docs/deviations.md "Smarter opponents"): one bit per behaviour, all off by default, which
/// is the original's AI. A session rule (<see cref="ScriptHost.SmartAi"/>, from the session's build: <see cref="Save.SessionBuild.SmartAi"/>),
/// the host's in a network game; a car may carry more of its own (<see cref="Entity.SmartAi"/>, a hook for a top skill tier). Each
/// behaviour acts only while the car's order is an attack (its tactics, on deck, too far and the escapes they push), so scripted gotos,
/// follows, races and evades drive as the original's. None changes grip, power or damage: only what the AI decides.
/// </summary>
[Flags]
public enum SmartAi : uint
{
    None = 0,
    /// <summary>Gun lay (T1): within reach of a loaded fixed forward gun, the nose is steered onto that gun's lead point.</summary>
    GunLay = 1 << 0,
    /// <summary>Settle (T5): while laying, the fixed forward guns wait until the nose has held on the lead point a reaction time.</summary>
    Settle = 1 << 1,
    /// <summary>Hill pursuit (N1): too far ends only within 30 m of height too, and too far's own route search costs steep steps.</summary>
    HillPursuit = 1 << 2,
    /// <summary>Climb-aware probe (N2): the avoidance probe's climb and cross-track limits from the grade the car can climb.</summary>
    ClimbProbe = 1 << 3,
    /// <summary>Ledge caution (N6): brake for a cliff's rim, never back over one, slow for a crest; too far's search keeps off steep faces.</summary>
    LedgeCaution = 1 << 4,
    /// <summary>Stuck recovery (N5): no progress while asking to go backs out on an arc toward the target, then forward; the cell is avoided a while.</summary>
    Unstick = 1 << 5,
    All = GunLay | Settle | HillPursuit | ClimbProbe | LedgeCaution | Unstick,
}

/// <summary>The <see cref="SmartAi"/> rule as written in the settings and the dev hook: names, groups or a number.</summary>
public static class SmartAiNames
{
    /// <summary>Each bit's key word, in bit order.</summary>
    public static readonly (string Name, SmartAi Bit)[] Bits =
    [
        ("gun_lay", SmartAi.GunLay), ("settle", SmartAi.Settle),
        ("hill_pursuit", SmartAi.HillPursuit), ("climb_probe", SmartAi.ClimbProbe), ("ledge_caution", SmartAi.LedgeCaution),
        ("unstick", SmartAi.Unstick),
    ];

    /// <summary>The groups of the research's catalogue: T lining up, N terrain, G coordination, D defence; and all, off.</summary>
    public static readonly (string Name, SmartAi Bits)[] Groups =
    [
        ("t", SmartAi.GunLay | SmartAi.Settle),
        ("n", SmartAi.HillPursuit | SmartAi.ClimbProbe | SmartAi.LedgeCaution | SmartAi.Unstick),
        ("all", SmartAi.All), ("off", SmartAi.None), ("none", SmartAi.None),
    ];

    /// <summary>
    /// A list of key words, group letters or numbers (decimal or 0x hex) separated by commas, spaces, '+' or '|'; the bits of all.
    /// Words it does not know go to <paramref name="unknown"/> and add nothing; bits past <see cref="SmartAi.All"/> are dropped.
    /// </summary>
    public static SmartAi Parse(string? text, out List<string> unknown)
    {
        unknown = [];
        var bits = SmartAi.None;
        if (string.IsNullOrWhiteSpace(text)) return bits;
        foreach (var raw in text.Split([',', ' ', '+', '|', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            string w = raw.Trim().ToLowerInvariant().Replace('-', '_');
            if (w.StartsWith("0x") && uint.TryParse(w[2..], System.Globalization.NumberStyles.HexNumber, null, out uint hx)) { bits |= (SmartAi)hx; continue; }
            if (uint.TryParse(w, System.Globalization.NumberStyles.None, null, out uint n)) { bits |= (SmartAi)n; continue; }
            int i = Array.FindIndex(Bits, b => b.Name == w);
            if (i >= 0) { bits |= Bits[i].Bit; continue; }
            i = Array.FindIndex(Groups, g => g.Name == w);
            if (i >= 0) { bits |= Groups[i].Bits; continue; }
            unknown.Add(raw);
        }
        return bits & SmartAi.All;
    }

    /// <summary>The bits as key words ("off" for none).</summary>
    public static string Describe(SmartAi bits) =>
        (bits & SmartAi.All) == SmartAi.None ? "off" : string.Join(",", Bits.Where(b => (bits & b.Bit) != 0).Select(b => b.Name));
}

public sealed partial class ScriptHost
{
    /// <summary>
    /// The session's smarter-opponents rule (<see cref="Missions.SmartAi"/>; the play fork's): set by the mission build from the
    /// session's build, the host's in a network game; <see cref="SmartAi.None"/>, the original's AI.
    /// </summary>
    public SmartAi SmartAi { get; set; }
}
