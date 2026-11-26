namespace I76.Game.Shell;

/// <summary>
/// Smoke Lighting ([port] smoke_lighting, Graphic Detail > Advanced, the modern look's): On lights the smoke and dust by the mission's sun
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
/// Heat Shimmer ([port] heat_shimmer, Graphic Detail > Advanced, the modern look's): Off (the default), Fires (the air wobbling over fires,
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
