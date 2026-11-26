namespace I76.Game.Shell;

/// <summary>
/// Smoke Lighting ([port] smoke_lighting, Graphic Detail > Effects, the modern look's): On lights the smoke and dust by the mission's sun
/// (its colour as its height gives it, warm low in the sky, and its direction) and the sky's ambient colour, so a dusk's smoke is warm on the
/// sun's side and dark on the other and a night's is dim and blue rather than a bright grey. Off (the default) keeps today's white light,
/// one level for the mission (SmokeLight). The original's smoke is unlit sprites; the 1997 look is unchanged either way.
/// </summary>
public static class SmokeLightingRow
{
    public static readonly string[] Names = ["Off", "On" + ShellSettings.PortMark];
    public const string Label = "Smoke Lighting";
    public static int Index(ShellSettings s) => s.SmokeLighting ? 1 : 0;
    public static void Put(ShellSettings s, int v) => s.SmokeLighting = v != 0;
    public static void Step(ShellSettings s) => s.SmokeLighting = !s.SmokeLighting;
}

/// <summary>
/// Heat Shimmer ([port] heat_shimmer, Graphic Detail > Effects, the modern look's): Off (the default), Fires (the air wobbling over fires,
/// burning wrecks and flame streams) or Fires + Road (also a shimmer low over the ground from 75 m out on a hot day, and the road's mirage:
/// HeatShimmer, HotDay). Not in the original; the 1997 look is unchanged.
/// </summary>
public static class HeatShimmerRow
{
    public const int Off = 0, Fires = 1, Road = 2;
    public static readonly string[] Names = ["Off", "Fires" + ShellSettings.PortMark, "Fires + Road" + ShellSettings.PortMark];
    public const string Label = "Heat Shimmer";
    private static readonly string[] Words = ["off", "fires", "road"];

    public static int Index(ShellSettings s) => Math.Clamp(s.HeatShimmer, Off, Road);
    public static void Put(ShellSettings s, int v) => s.HeatShimmer = Math.Clamp(v, Off, Road);
    public static void Step(ShellSettings s) => s.HeatShimmer = (Index(s) + 1) % Names.Length;

    /// <summary>The file's word; none, or one this version does not know, is Off.</summary>
    public static int FromFile(string? word) => Math.Max(0, Array.IndexOf(Words, word?.Trim().ToLowerInvariant()));
    public static string Word(int v) => Words[Math.Clamp(v, Off, Road)];
}

/// <summary>
/// Shimmer Strength ([port] heat_strength, Graphic Detail > Effects, under Heat Shimmer, the modern look's): how strongly the air bends,
/// fire, ripple and mirage together (HeatShimmer.Strength; the Developer panel's sliders on top): Low half of Medium, Medium* (the
/// default), High 1.6 times it, Extreme 2.5. Does nothing while Heat Shimmer is Off. Written only when not Medium; a file without the key
/// (every file before the row) is Medium, the strength it had.
/// </summary>
public static class HeatStrengthRow
{
    public const int Low = 0, Medium = 1, High = 2, Extreme = 3, Default = Medium;
    public static readonly string[] Names = ["Low" + ShellSettings.PortMark, "Medium" + ShellSettings.PortMark, "High" + ShellSettings.PortMark, "Extreme" + ShellSettings.PortMark];
    public static readonly float[] Factors = [0.5f, 1f, 1.6f, 2.5f];
    public const string Label = "Shimmer Strength";
    private static readonly string[] Words = ["low", "medium", "high", "extreme"];

    public static int Clamp(int v) => Math.Clamp(v, Low, Extreme);
    public static int Index(ShellSettings s) => Clamp(s.HeatStrength);
    public static void Put(ShellSettings s, int v) => s.HeatStrength = Clamp(v);
    public static void Step(ShellSettings s) => s.HeatStrength = (Index(s) + 1) % Names.Length;
    public static float Factor(int v) => Factors[Clamp(v)];

    /// <summary>The file's word; none, or one this version does not know, is Medium.</summary>
    public static int FromFile(string? word) => Array.IndexOf(Words, word?.Trim().ToLowerInvariant()) is var i and >= 0 ? i : Default;
    public static string Word(int v) => Words[Clamp(v)];

    /// <summary>The step's name for a factor (the Developer panel's readout): the nearest step's word, or "custom".</summary>
    public static string NameOf(float factor)
    {
        for (int i = 0; i < Factors.Length; i++) if (Math.Abs(Factors[i] - factor) < 0.01f) return Words[i];
        return "custom";
    }
}
