namespace I76.Game.Shell;

/// <summary>
/// Graphic Detail's tooltips (the port's: the original had no help on its pads; <see cref="MenuTooltip"/> shows them on the shell's
/// sheets and the in-game pad): for every row, what the setting does in plain words and, where it matters, what it costs. The strings
/// live here, apart from the pages' table (GraphicsPages), so the rows can move without touching them.
/// <para>
/// The costs are shares of a full-detail frame from the frame-cost survey (docs/perf/costs.md, "Features, sorted by frame cost"): the
/// T01 start in the cockpit, 3440x1440, a GTX 1080 Ti at 180 W, Godot 4.7.2 Forward+, every feature on, 16.9 ms of GPU time a frame;
/// each share is the feature's GPU ms there over 16.9, rounded: ambient occlusion 4.96 ms (29 %), the rear mirror at Full 2.88 (17 %),
/// MSAA 4x 2.07 over Off (12 %) and 1.52 for 2x (9 %; 4x over 2x 0.55, 3 %), the sun's shadows 1.34 (8 %; High over Low, the reach
/// from 600 to 1500 m, 0.18 ms at 1920x1080), Terrain Detail High 1.03 (6 %), Vegetation High 0.74 (4 %), Visibility Range Max 0.04
/// at 3440x1440 and 0.47 at 1920x1080 (under 3 %) with ~800 draw calls, the 4x texture pack none (+723 MB of video memory, T01's load
/// 11.6 s against 8.7 s), and the 8x of the cars and the dash none (+510 MB of video memory over the 4x in T01, the dash's 482 of
/// it). The 1997 look draws none of the modern look's features, so its rows say so.
/// </para>
/// </summary>
public static class GraphicsHelp
{
    /// <summary>The tooltip for a row in a look (every row has one).</summary>
    public static string Text(GraphicsRow row, bool look1997) => row switch
    {
        GraphicsRow.Resolution => "Screen resolution: Desktop is borderless fullscreen at the desktop's size; a size is a window of that size. Applies at once.",
        GraphicsRow.MonitorBrightness => "Monitor brightness: raise it for a dark screen or TV, lower it if blacks look grey. Applies at once.",
        GraphicsRow.VisibilityRange => look1997
            ? "Visibility range: Far is the original's 600 m, 2.3 km fades into the horizon from 1.7 km, Max the whole map. Little cost in the 1997 look."
            : "Visibility range: Far is the original's 600 m, 2.3 km fades into the sky from 1.7 km, Max the whole map. Under 3 % of a full-detail frame at Max, but some 800 more draw calls.",
        GraphicsRow.TerrainDetail => look1997
            ? "Terrain detail: the modern look's finest ground mesh. No cost in the 1997 look."
            : "Terrain detail: High (the default) adds the finest ground mesh near the car. About 1 ms, 6 % of a full-detail frame, over Low.",
        GraphicsRow.ShadowDetail => look1997
            ? "Shadow detail: the modern look's sun shadows (the 1997 look keeps the original's). No cost in the 1997 look."
            : "Shadow detail: the sun's shadows. Low (the default) reaches 600 m, High 1500 m with finer shadows near you, Off none. About 8 % of a full-detail frame; High about 6 % more.",
        GraphicsRow.ExtInstruments => "Ext. instruments: the cockpit's panels (radar, damage, weapons) over the outside views; the [ key toggles them in play. Little cost.",
        GraphicsRow.RearMirror => look1997
            ? "Rear mirror: the original's small mirror, to 100 m. Little cost in the 1997 look."
            : "Rear mirror: a second view, behind. Up to 17 % of a full-detail frame; the original's 100 m is on the Developer panel.",
        GraphicsRow.SideMirrors => look1997
            ? "Side mirrors: the modern look's view behind in the door mirrors' glass. The 1997 look paints them, as the original. No cost in the 1997 look."
            : "Side mirrors: the view behind in the door mirrors (the Piranha's two, the CC cars' left one). Left is the driver's alone; Off paints them, as the original. A mirror out of view is not drawn. Both and Left cost the same: one side picture every second frame, about 4 % of a full-detail frame; Off saves it.",
        GraphicsRow.MirrorRate => look1997
            ? "Mirror rate: how often the modern look's mirrors are drawn. The 1997 look draws its mirror every second frame, as the original."
            : "Mirror rate: Alternate (the default) draws the rear mirror every second frame and the side mirrors in turn; Half every mirror every second frame; Full every frame, smoothest. Full is about 9 % of a full-detail frame over Half.",
        GraphicsRow.AntiAliasing => look1997
            ? "Anti-aliasing: smooths jagged edges in the modern look (and the 1997 look's polygon edges). Little cost in the 1997 look."
            : "Anti-aliasing: smooths jagged edges. 4x is about 12 % of a full-detail frame, 2x (the default) about 9 %.",
        GraphicsRow.FieldOfView => "Field of view: how wide the 3D view is. Monitor keeps the original's height and widens with the window; 90 is the original's width. Applies at once.",
        GraphicsRow.VSync => "VSync: waits for the screen's refresh, so the picture never tears. Off can feel quicker on a fast machine. Applies at once.",
        GraphicsRow.MaxFps => "Max FPS: a cap on the frames drawn a second; the game's own rate is unchanged. A cap saves power and heat. Applies at once.",
        GraphicsRow.Look1997 => "1997 mode: draw the game as the original did, with a 3D card of the day or in its software renderer. Off is the modern look. From the next mission (on resume in one).",
        GraphicsRow.PaletteGrade => look1997
            ? "1997 palette grade: the modern look's colours moved toward the mission's 256-colour palette. The 1997 look has them already."
            : "1997 palette grade: the modern look in the original's colours, each moved toward the mission's 256-colour palette. Little cost.",
        GraphicsRow.CrashBlur => "Crash blur: the double image a big hit leaves in the cockpit, as the original's software renderer drew it. Off for none.",
        GraphicsRow.WorldTextures => look1997
            ? "World textures: the ground, buildings and props from the texture pack, for the modern look. The 1997 look always draws the install's own."
            : "World textures: the ground, buildings and props. 1997 is the install's own; 4x the pack's upscales, no frame cost (the whole 4x pack takes about 0.7 GB of video memory)." + PacksFound(),
        GraphicsRow.CarTextures => look1997
            ? "Car & dash textures: the cars and your dash and gauges from the texture pack, for the modern look. The 1997 look always draws the install's own."
            : "Car & dash textures: the cars and your dash and gauges. 1997 is the install's own, 4x the pack; 8x is sharper up close for about 0.5 GB more video memory (most of it the dash), no frame cost. From the next mission." + PacksFound(),
        GraphicsRow.Vegetation => look1997
            ? "Vegetation: the modern look's shrubs and grass. No cost in the 1997 look."
            : "Vegetation: shrubs and grass on the ground. About 4 % of a full-detail frame at High.",
        GraphicsRow.AmbientOcclusion => look1997
            ? "Ambient occlusion: the modern look's contact shadows. No cost in the 1997 look."
            : "Ambient occlusion: contact shadows under cars and shrubs. About 29 % of a full-detail frame.",
        GraphicsRow.HoodGloss => look1997
            ? "Hood gloss: the modern look's shine on the cars' paint and your hood. Not drawn in the 1997 look."
            : "Hood gloss: how much the cars' paint and your hood shine, from Matte (0, the original's flat paint) to Gloss (100, a full clear coat); 5 is the default sheen. No frame cost.",
        GraphicsRow.RenderScale => look1997
            ? "Render scale: the modern look's 3D view drawn smaller or larger than the window. Not used in the 1997 look."
            : "Render scale: the 3D view drawn at 50 to 200 % of the window (the menus and gauges stay sharp). Under 100 % is faster and softer, over it sharper and much slower."
                + (I76.Game.Boot.RenderScale.HeldNote is { Length: > 0 } held ? $" Now {held}: over it the view would not fit the card's video memory." : ""),
        GraphicsRow.ShowFps => "Show FPS: the frame rate and frame time in the window's top right corner, over the menus and the game.",
        GraphicsRow.Renderer => "Renderer: Vulkan, Direct3D 12 (Windows) or OpenGL; Auto picks. Try another if the game will not start or draws wrongly. From the next start.",
        GraphicsRow.Garage3D => "3D garage: Configure Chassis drawn as a 3D garage with your car in it; Off is the original's flat 2D form.",
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    /// <summary>
    /// The texture rows' last sentence: which texture packs were found, where when not where the README puts them, and what looked like
    /// a pack and was not used (HdPacks.RowNote). Empty before the start's scan (the tests' calls).
    /// </summary>
    private static string PacksFound()
    {
        return I76.Game.Assets.HdPacks.RowNote() is { Length: > 0 } n ? " " + n : "";
    }

    /// <summary>The sheet's entry for the Advanced page.</summary>
    public const string AdvancedEntry = "Advanced: 1997 Mode and its palette, the texture packs, the modern look's extras, render scale and the start-up rows.";

    /// <summary>The Esc menu's frame-rate line (FrameRateEstimate's LiveFps): paused in single player, the game running on in a network game.</summary>
    public static string MeasuredFps(bool net, double? play = null) => (net
        ? "Measured: the frames a second drawn now, over the last second, with these settings; in brackets the change since the menu opened. The game runs on under the menu."
        : "Measured: the frames a second drawn now behind the paused game, with these settings; in brackets the change since the menu opened. Paused, the game's own work stops, so play runs slower.")
        + (play is { } p && p > 0 ? $" In play just before the menu: {p:0} fps." : "");

    /// <summary>The main menu's frame-rate line: estimated from the last mission measured (<paramref name="from"/>: its mission, size and figure), or how to get one.</summary>
    public static string EstimatedFps(I76.Game.Ui.FpsRecord? from)
    {
        if (from is not { } r) return "Estimated frame rate: play a mission in the modern look and this line estimates your frame rate for the settings you pick here.";
        return $"Estimated: your last mission ({r.Mission.ToUpperInvariant()}, {r.At.Width}x{r.At.Height}, {r.Fps:0} fps) worked out for these settings from what each costs on a test machine. A guide; the Esc menu's Graphic Detail measures it in a mission.";
    }

    /// <summary>Reset on a page.</summary>
    public const string Reset = "Reset: this page's rows back to their defaults.";

/// <summary>A help line broken at spaces into lines no wider than <paramref name="width"/> (a word longer than that keeps a line of its own).</summary>
    public static List<string> Wrap(string text, Func<string, float> measure, float width)
    {
        var lines = new List<string>();
        var line = "";
        // a figure and its unit stay on one line ("29 %", "100 m", "0.7 GB", "50 MB")
        var words = new List<string>();
        foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (words.Count > 0 && (w.StartsWith('%') || w is "m" or "m," or "m)" or "GB" or "MB" or "s" or "ms") && char.IsDigit(words[^1][^1])) words[^1] += " " + w;
            else words.Add(w);
        foreach (var word in words)
        {
            var t = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && measure(t) > width) { lines.Add(line); line = word; }
            else line = t;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }
}
