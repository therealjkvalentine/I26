namespace I76.Game.Shell;

/// <summary>
/// Graphic Detail's help line (the port's: the original had no help text on its pads): for the row under the mouse on the shell's
/// sheet, or the in-game pad's selected row, what the setting does in plain words and what it costs. Only the eight rows that cost the
/// most have one; every other row shows a blank line. The strings live here, apart from the pages' table (GraphicsPages), so the
/// rows can move without touching them.
/// <para>
/// The costs are shares of a full-detail frame from the frame-cost survey (docs/perf/costs.md, "Features, sorted by frame cost"): the
/// T01 start in the cockpit, 3440x1440, a GTX 1080 Ti at 180 W, Godot 4.7.2 Forward+, every feature on, 16.9 ms of GPU time a frame;
/// each share is the feature's GPU ms there over 16.9, rounded: ambient occlusion 4.96 ms (29 %), the rear mirror at Full 2.88 (17 %),
/// MSAA 4x 2.07 over Off (12 %) and 1.52 for 2x (9 %; 4x over 2x 0.55, 3 %), the sun's shadows 1.34 (8 %), Terrain Detail High
/// 1.03 (6 %), Vegetation High 0.74 (4 %), Visibility Range Max 0.04 at 3440x1440 and 0.47 at 1920x1080 (under 3 %) with ~800 draw
/// calls, the 4x texture pack none (+723 MB of video memory, T01's load 11.6 s against 8.7 s). The 1997 look draws none of the modern
/// look's features, so its rows say so.
/// </para>
/// </summary>
public static class GraphicsHelp
{
    /// <summary>The help line for a row in a look; null for a row without one (the line is blank).</summary>
    public static string? Text(GraphicsRow row, bool look1997) => row switch
    {
        GraphicsRow.AmbientOcclusion => look1997
            ? "Ambient occlusion: the modern look's contact shadows. No cost in the 1997 look."
            : "Ambient occlusion: contact shadows under cars and shrubs. About 29 % of a full-detail frame.",
        GraphicsRow.RearMirror => look1997
            ? "Rear mirror: the original's small mirror, to 100 m. Little cost in the 1997 look."
            : "Rear mirror: a second view of the road behind, to the original's 100 m (Full, the whole map, in shell.cfg). Up to 17 % of a full-detail frame.",
        GraphicsRow.AntiAliasing => look1997
            ? "Anti-aliasing: smooths jagged edges in the modern look. No cost in the 1997 look."
            : "Anti-aliasing: smooths jagged edges. 4x is about 12 % of a full-detail frame, 2x about 9 %.",
        GraphicsRow.ShadowDetail => look1997
            ? "Shadow detail: the modern look's sun shadows (the 1997 look keeps the original's). No cost in the 1997 look."
            : "Shadow detail: the sun's shadows, Low to 600 m, High to 1500 m. About 8 % of a full-detail frame.",
        GraphicsRow.TerrainDetail => look1997
            ? "Terrain detail: the modern look's finest ground mesh. No cost in the 1997 look."
            : "Terrain detail: High adds the finest ground mesh near the car. About 6 % of a full-detail frame.",
        GraphicsRow.Vegetation => look1997
            ? "Vegetation: the modern look's shrubs and grass. No cost in the 1997 look."
            : "Vegetation: shrubs and grass on the ground. About 4 % of a full-detail frame at High.",
        GraphicsRow.VisibilityRange => look1997
            ? "Visibility range: Far is the original's 600 m, Max the whole map. Little cost in the 1997 look."
            : "Visibility range: Far is the original's 600 m, Max the whole map. Under 3 % of a full-detail frame, but some 800 more draw calls.",
        GraphicsRow.HdTextures => look1997
            ? "HD textures: the 4x texture pack, for the modern look. Not drawn in the 1997 look."
            : "HD textures: the 4x texture pack. No frame cost; about 0.7 GB more video memory and 3 s more loading on T01.",
        _ => null,
    };

    /// <summary>A help line broken at spaces into lines no wider than <paramref name="width"/> (a word longer than that keeps a line of its own).</summary>
    public static List<string> Wrap(string text, Func<string, float> measure, float width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && measure(t) > width) { lines.Add(line); line = word; }
            else line = t;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }
}
