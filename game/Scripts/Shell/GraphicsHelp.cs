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
        GraphicsRow.Resolution => "Screen resolution (display mode, fullscreen or window): Desktop is borderless fullscreen at the desktop's size; a size is a window of that size. Applies at once.",
        GraphicsRow.MonitorBrightness => "Monitor brightness (gamma): raise it for a dark screen or TV, lower it if blacks look grey. Applies at once.",
        GraphicsRow.VisibilityRange => look1997
            ? "Visibility range (draw distance): Far is the original's 600 m, 2.3 km fades into the horizon from 1.7 km, Max the whole map. Little cost in the 1997 look."
            : "Visibility range (draw distance): Far is the original's 600 m, 2.3 km fades into the sky from 1.7 km, Max the whole map. Under 3 % of a full-detail frame at Max, but some 800 more draw calls. Applies at once.",
        GraphicsRow.TerrainDetail => look1997
            ? "Terrain detail (ground mesh detail): the modern look's finest ground mesh. No cost in the 1997 look."
            : "Terrain detail (ground mesh detail): High (the default) adds the finest ground mesh near the car. About 1 ms, 6 % of a full-detail frame, over Low. Applies at once.",
        GraphicsRow.ShadowDetail => look1997
            ? "Shadow detail (shadow quality): the modern look's sun shadows (the 1997 look keeps the original's). No cost in the 1997 look."
            : "Shadow detail (shadow quality): the sun's shadows. Low (the default) reaches 600 m, High 1500 m with finer shadows near you, Off none. About 8 % of a full-detail frame; High about 6 % more. Ultra is High with a shadow map twice as fine and cleaner edges at a low sun, a few % more than High. Applies at once.",
        GraphicsRow.ExtInstruments => "Ext. instruments (HUD panels): the cockpit's panels (radar, damage, weapons) over the outside views; the [ key toggles them in play. Little cost. Applies at once.",
        GraphicsRow.Mirrors => look1997
            ? "Mirrors: the original's small rear mirror, to 100 m (Rear + Left and All draw it alone: the 1997 look paints the door mirrors, as the original). Little cost in the 1997 look."
            : "Mirrors: Rear is the rear-view mirror; Rear + Left adds the driver's door mirror, All both door mirrors where the car has them (the Piranha's two, the CC cars' left one). The rear mirror is up to 17 % of a full-detail frame, a door mirror in view about 4 %; a mirror out of view is not drawn. At 3440x1440 in T05 the graphics card alone could draw about 116 fps with Off, 105 with Rear, 98 with All. The original's 100 m reach is on the Developer panel. Applies at once.",
        GraphicsRow.MirrorRate => look1997
            ? "Mirror rate: how often the modern look's mirrors are drawn. The 1997 look draws its mirror every second frame, as the original."
            : "Mirror rate: Alternate (the default) draws the rear mirror every second frame and the side mirrors in turn; Half every mirror every second frame; Full every frame, smoothest. Full is about 9 % of a full-detail frame over Half (in T05 at 3440x1440, about 84 fps from the graphics card against 98). Smooth 1/2, 1/3 and 1/4 draw each mirror every second, third or fourth frame in turn and move its last picture with the car's turn on the frames between, so the glass keeps up with a turn at less cost; cars moving in it update at the lower rate. Applies at once.",
        GraphicsRow.MirrorHaze => look1997
            ? "Mirror haze (mirror dirt, glass tint): the modern look's mirror glass a little milky. The 1997 look's mirror is the original's."
            : "Mirror haze (mirror dirt, glass tint): the mirrors' glass a touch milky, as no mirror is perfect or perfectly clean, so it does not look quite like the view through the windscreen. Low (the default) barely seen, Med a little more, High about three times Low, Off the clear glass. A flat veil, the same all over the glass. Little cost; Off none. Applies at once.",
        GraphicsRow.RadarSweep => look1997
            ? "Radar sweep (radar animation): the modern look's smooth radar beam. The 1997 look's radar is the original's 30 painted frames."
            : "Radar sweep (radar animation): Stepped (the default) is the original's: the beam jumps 12 degrees five times a second through 30 painted frames, and the lock cone points at the nearest 12 degrees. Smooth turns the beam every frame at the same speed and points the cone right at the target, drawn on the graphics card from the texture pack's radar layers (a pack made with them; otherwise it stays stepped). No cost to speak of; Smooth also saves the uploads of the stepped frames. Applies at once.",
        GraphicsRow.MirrorAa => look1997
            ? "Mirror AA (mirror anti-aliasing): the modern look's mirrors' anti-aliasing. The 1997 look's mirror is the original's, without any."
            : "Mirror AA (mirror anti-aliasing): smooths jagged edges in the mirrors' pictures, which the main view's Anti-Aliasing does not reach. Off (the default) is the mirrors as they were; SMAA smooths poles, wires and the horizon for little cost; 2x, 4x and 8x are MSAA in the mirrors' renders: 2x about 2 % of a light frame, 8x about 9 %, as much again as the mirrors themselves; As View follows the Anti-Aliasing row. Applies at once.",
        GraphicsRow.AntiAliasing => look1997
            ? "Anti-aliasing (AA): smooths jagged edges in the modern look (and the 1997 look's polygon edges; SMAA is the modern look's only). Little cost in the 1997 look."
            : "Anti-aliasing (AA): smooths jagged edges. 4x is about 12 % of a full-detail frame, 2x (the default) about 9 %. SMAA smooths edges after drawing (thin poles and cut-out edges too) for a third of what 2x costs, a touch softer; 2x+SMAA adds it to 2x. Not on the OpenGL renderer. Applies at once.",
        GraphicsRow.FieldOfView => "Field of view (FOV): how wide the 3D view is. Monitor keeps the original's height and widens with the window; 90 is the original's width. Applies at once.",
        GraphicsRow.VSync => "VSync (vertical sync): waits for the screen's refresh, so the picture never tears. Off can feel quicker on a fast machine. Applies at once.",
        GraphicsRow.MaxFps => "Max FPS (frame rate limit, frame cap): a cap on the frames drawn a second; the game's own rate is unchanged. A cap saves power and heat. Applies at once.",
        GraphicsRow.Look1997 => "1997 mode: draw the game as the original did, with a 3D card of the day or in its software renderer. Off is the modern look. From the next mission (on resume in one).",
        GraphicsRow.PaletteGrade => look1997
            ? "1997 palette grade: the modern look's colours moved toward the mission's 256-colour palette. The 1997 look has them already."
            : "1997 palette grade: the modern look in the original's colours, each moved toward the mission's 256-colour palette. Little cost. Applies at once.",
        GraphicsRow.CrashBlur => "Crash blur: the double image a big hit leaves in the cockpit, as the original's software renderer drew it. Off for none. Applies at once.",
        GraphicsRow.WorldTextures => look1997
            ? "World textures: the ground, buildings and props from the texture pack, for the modern look. The 1997 look always draws the install's own."
            : "World textures: the ground, buildings and props. 1997 is the install's own; 4x the pack's upscales, no frame cost (the whole 4x pack takes about 0.7 GB of video memory). Applies at once." + PacksFound(),
        GraphicsRow.CarTextures => look1997
            ? "Car & dash textures: the cars and your dash and gauges from the texture pack, for the modern look. The 1997 look always draws the install's own."
            : "Car & dash textures: the cars and your dash and gauges. 1997 is the install's own, 4x the pack; 8x is sharper up close for about 0.5 GB more video memory (most of it the dash), no frame cost. 1997 and the pack switch at once; between 4x and 8x from the next mission." + PacksFound(),
        GraphicsRow.Vegetation => look1997
            ? "Vegetation (foliage, grass): the modern look's shrubs and grass. No cost in the 1997 look."
            : "Vegetation (foliage, grass): shrubs and grass on the ground. About 4 % of a full-detail frame at High. Applies at once.",
        GraphicsRow.AmbientOcclusion => look1997
            ? "Ambient occlusion (SSAO): the modern look's contact shadows. No cost in the 1997 look."
            : "Ambient occlusion (SSAO): contact shadows under cars and shrubs. About 29 % of a full-detail frame. Applies at once.",
        GraphicsRow.Haze => look1997
            ? "Haze (distance fog): the modern look's air over the distance. No cost in the 1997 look."
            : "Haze (distance fog): air over the distance, as in photographs of the desert: from 600 m the far ground and mesas pale toward the sky's horizon colour. Off (the default) is the original's clear air; Light a little, Southwest more. About 0.1 to 0.25 ms at 3440x1440 with Render Scale 200 % on a fast card; Off costs nothing. Applies at once.",
        GraphicsRow.Glow => look1997
            ? "Glow (bloom): the modern look's bloom round lamps and fire. No cost in the 1997 look."
            : "Glow (bloom): Lamps puts a soft bloom round the brightest things only (lamps, muzzle flashes, fire and explosions), mostly seen at night; the sky, the sunlit ground and the dash do not glow. Off (the default) as the original. About 0.3 ms (the glow pass) to 0.6 ms at 3440x1440 with Render Scale 200 % on a fast card; Off costs nothing. Applies at once.",
        GraphicsRow.Reflections => look1997
            ? "Reflections (SSR): the modern look's screen-space reflections. No cost in the 1997 look."
            : "Reflections (SSR): glass, and glossy paint with Hood Gloss up, mirror what is on screen (other cars, the road, buildings) instead of the sky alone. Only with Ambient Occlusion On, whose pass it shares. About 0.4 ms over Ambient Occlusion at 3440x1440 with Render Scale 200 % on a fast card; little to see in most scenes. Applies at once.",
        GraphicsRow.HoodGloss => look1997
            ? "Hood gloss: the modern look's shine on the cars' paint and your hood. Not drawn in the 1997 look."
            : "Hood gloss: how much the cars' paint and your hood shine, from Matte (0, the original's flat paint) to Gloss (100, a full clear coat); 5 is the default sheen. No frame cost. Applies at once.",
        GraphicsRow.RenderScale => look1997
            ? "Render scale (resolution scale; supersampling over 100 %): the modern look's 3D view drawn smaller or larger than the window. Not used in the 1997 look."
            : "Render scale (resolution scale; supersampling over 100 %): the 3D view drawn at 50 to 200 % of the window (the menus and gauges stay sharp). Under 100 % is faster and softer, over it sharper and much slower. Applies at once."
                + (I76.Game.Boot.RenderScale.HeldNote is { Length: > 0 } held ? $" Now {held}: over it the view would not fit the card's video memory." : ""),
        GraphicsRow.ShowFps => "Show FPS (FPS counter): the frame rate and frame time in the window's top right corner, over the menus and the game. Applies at once.",
        GraphicsRow.Renderer => "Renderer: Vulkan, Direct3D 12 (Windows) or OpenGL; Auto picks. Try another if the game will not start or draws wrongly. From the next start: the row says (restart) until then.",
        GraphicsRow.Garage3D => "3D garage: Configure Chassis drawn as a 3D garage with your car in it; Off is the original's flat 2D form. Applies at once.",
        GraphicsRow.MenuWidth => "Menu width: Wide fills a widescreen window with the widescreen menu pack's art beside each menu page; 4:3 shows the pages alone between black bars. The page itself is the same either way. Applies at once.",
        GraphicsRow.SmokeLighting => look1997
            ? "Smoke lighting: the modern look's smoke and dust lit by the sun and sky. Not drawn in the 1997 look."
            : "Smoke lighting: smoke and dust take the sun's colour and direction and the sky's light, so smoke at dusk is warm on the sun's side and dark on the other, and smoke at night is dim. Off is one even light. No measurable frame cost. Applies at once.",
        GraphicsRow.SmokeLinger => look1997
            ? "Smoke linger (smoke duration): how long the modern look's smoke hangs in the air. The 1997 look's puffs are the original's."
            : "Smoke linger (smoke duration): Long lets the smoke of damaged cars, burning wrecks, rocket and mortar trails and explosions hang on after it has risen, starting denser and darker, then slowly widening and thinning to a faint haze that drifts with the wind and fades away over two to three minutes. Normal (the default) ends each puff as before. About 0.05 ms a frame with seven smoking cars in view at 1920x1080 on low settings, 0.2 to 0.4 ms at 3440x1440 with Render Scale 200 % (an RTX 5090); none when Normal. Applies at once.",
        GraphicsRow.HeatShimmer => look1997
            ? "Heat shimmer: the modern look's wobbling air over fires and hot roads. Not drawn in the 1997 look."
            : "Heat shimmer: the air wobbles over fires, burning wrecks and flame streams. Fires + Road adds, on hot days only, a shimmer far down the road (from 200 m) on ground that runs nearly flat to your view, strongest on asphalt, and the asphalt's mirage, faded toward the sky's colour; Shimmer Strength sets how strong. The 1997 game has none. About 0.1 ms a frame while something shimmers (3440x1440, 8x, 200 %, an RTX 5090), none when Off. Applies at once.",
        GraphicsRow.HeatStrength => look1997
            ? "Shimmer strength (heat haze strength): how strongly the modern look's heat shimmer bends the air. Not drawn in the 1997 look."
            : "Shimmer strength (heat haze strength): how strongly Heat Shimmer bends the air, over fires and, on a hot day, over the far road and flat ground and the road's mirage, all together. Medium by default; Low is half of it, High and Extreme stronger. Nothing while Heat Shimmer is Off, and no cost of its own. Applies at once.",
        GraphicsRow.TerrainShadows => look1997
            ? "Terrain shadows: the modern look's baked hill shadows. Not drawn in the 1997 look."
            : "Terrain shadows: the hills' and mesas' own shadows on the land, baked from the map and its sun as the mission loads: crisp and steady at any Shadow Detail, out to the horizon. On by default. Off leaves them to Shadow Detail (600 m at Low, 1500 m at High). Fine bakes twice as finely (more load time and video memory). By day only. About 1-2 % of a frame at 3440x1440 and 200 % scale, none measurable at 1920x1080. Applies at once (a new bake eases in).",
        GraphicsRow.RoadDetail => look1997
            ? "Road detail (road surface quality): the modern look's surface on the paved roads. Not drawn in the 1997 look."
            : "Road detail (road surface quality): the paved roads get a surface of their own near the camera (aggregate grain and stones, fine cracks, sealed patches, crisper and slightly worn lane lines, broad lighter and darker stretches down a long straight, the dirt verge blended into the ground), made in the shader; the road's layout, colours and markings stay the original's. Off (the default) draws the roads' own textures alone. A few extra noise reads on road pixels near the camera; Off costs nothing. Applies at once.",
        GraphicsRow.Quality => look1997
            ? "Quality (graphics preset): sets the modern look's costly rows at once. The 1997 look draws none of them."
            : "Quality (graphics preset): Original is the original game's look in the modern renderer, Balanced the defaults, High and Ultra more for a strong graphics card. It sets Render Scale, Anti-Aliasing, Shadow Detail, Terrain Shadows, Road Detail, Visibility Range, the textures, Vegetation, Ambient Occlusion, Reflections, Haze, Mirror Rate, Mirror AA, Glow, Smoke Linger, Smoke Lighting and Heat Shimmer; Custom when they match none. The line at the foot estimates the frame rate. Applies at once.",
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

    /// <summary>The sheet's entries for the pages, by page (GraphicsPages; the sheet's own is unused): what each page holds, in its order.</summary>
    public static readonly string[] Entries =
    [
        "",
        "Display: the screen resolution and monitor brightness, anti-aliasing, render scale, VSync and the frame rate limit, the frame counter, the renderer and the menus' width.",
        "World: how far you see, the terrain's detail, the shadows (the cars' and the terrain's), the world's textures, plants, ambient occlusion, reflections and haze.",
        "Car & Cockpit: the field of view, the external instruments, the mirrors (which, how often drawn, their haze), the car and dash textures, the paint's gloss and the 3D garage.",
        "Effects: the modern look's palette grade, crash blur, glow, smoke linger, smoke lighting and heat shimmer.",
    ];
    public static string Entry(int page) => Entries[page];

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

    /// <summary>Reset on the sheet: every page's.</summary>
    public const string ResetAll = "Reset: every page's rows back to their defaults (the original's rows to its default profile).";

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
