namespace I76.Game.Shell;

/// <summary>
/// Graphic Detail's Quality row (the port's; the original has no preset): one choice that sets the modern look's costly rows at once,
/// Original, Balanced*, High* or Ultra*, and shows Custom* when the rows match none. Nothing of its own is saved: the value is read
/// back from the rows, and choosing one sets each row as stepping it by hand would. Original is the original game's look in the modern
/// renderer (every port row at the original's value, the original's own rows at the default profile); Balanced is a first start's
/// defaults; High and Ultra are steps up for a strong graphics card (Ultra close to a 200 % supersampled playtest setup). The window,
/// the monitor, Field of View, VSync, Max FPS, Menu Width, 1997 Mode and the rows that are taste rather than cost (Mirrors, Mirror
/// Haze, Hood Gloss, Palette Grade, Crash Blur, Ext. Instrum. Disp., Terrain Detail, 3D Garage, Radar Sweep, Shimmer Strength) are
/// not in it. The texture rows ask
/// for a size and take the largest the installed packs offer up to it; with no pack they are left out. No engine here.
/// </summary>
public static class QualityPreset
{
    public const string Label = "Quality";
    public const int Original = 0, Balanced = 1, High = 2, Ultra = 3;
    public static readonly string[] Names = ["Original", "Balanced" + ShellSettings.PortMark, "High" + ShellSettings.PortMark, "Ultra" + ShellSettings.PortMark];
    public const string Custom = "Custom" + ShellSettings.PortMark;

    /// <summary>A column's value that is a first start's (<see cref="Balanced"/> is all of them).</summary>
    public const int Default = -1;

    /// <summary>
    /// The rows and, per column (Original, Balanced, High, Ultra), the row's value as its index in the row's words (the texture rows: a
    /// size, 0 the install's own). Balanced is every row's default: today Render Scale 100 %, Anti-Aliasing 2x, Shadow Detail Low, Terrain
    /// Shadows On, Road Detail Off, Ambient Occlusion and Reflections Off, Vegetation Low, Haze, Glow, Smoke Linger (Normal), Smoke
    /// Lighting and Heat Shimmer Off, Mirror Rate Alternate, Mirror AA Off, Visibility Range following the look (Max in the modern look)
    /// and the textures' first-start sizes.
    /// </summary>
    public static readonly (GraphicsRow Row, int[] Values)[] Table =
    [
        (GraphicsRow.RenderScale,      [4, Default, 5, 6]),      // 100 %, 150 %*, 200 %*
        (GraphicsRow.AntiAliasing,     [0, Default, 2, 4]),      // Off, 4x, SMAA* (over the 200 %: MSAA there is costly for little)
        (GraphicsRow.ShadowDetail,     [1, Default, 2, 3]),      // Low (the default profile), High, Ultra*
        (GraphicsRow.TerrainShadows,   [0, Default, 2, 2]),      // Off, Fine*
        (GraphicsRow.RoadDetail,       [0, Default, 1, 1]),      // Off, On*
        (GraphicsRow.VisibilityRange,  [0, Default, 1, 1]),      // Far (the original's 600 m), Max*
        (GraphicsRow.WorldTextures,    [0, Default, 4, 4]),      // 1997, 4x*
        (GraphicsRow.Vegetation,       [0, Default, 2, 2]),      // Off, High*
        (GraphicsRow.AmbientOcclusion, [0, Default, 1, 1]),
        (GraphicsRow.Reflections,      [0, Default, 0, 1]),      // only with Ambient Occlusion On
        (GraphicsRow.Haze,             [0, Default, 1, 2]),      // Off, Light*, Southwest*
        (GraphicsRow.MirrorRate,       [0, Default, 3, 2]),      // Alternate (the original's), Smooth 1/2* (Half's draws, the glass kept up with a turn), Full*
        (GraphicsRow.MirrorAa,         [0, Default, 4, 4]),      // Off, SMAA* (Ultra's view is SMAA too: As View would draw the same, but follow the view row)
        (GraphicsRow.CarTextures,      [0, Default, 8, 8]),      // 1997, 8x* (4x where only 4x is installed)
        (GraphicsRow.Glow,             [0, Default, 1, 1]),      // Off, Lamps*
        (GraphicsRow.SmokeLinger,      [0, Default, 1, 1]),      // Normal, Long*
        (GraphicsRow.SmokeLighting,    [0, Default, 1, 1]),
        (GraphicsRow.HeatShimmer,      [0, Default, 1, 2]),      // Off, Fires*, Fires + Road*
    ];

    public static IEnumerable<GraphicsRow> Rows => Table.Select(t => t.Row);

    private static bool Texture(GraphicsRow r) => r is GraphicsRow.WorldTextures or GraphicsRow.CarTextures;

    /// <summary>A row's value now (the texture rows: their size), Visibility Range as the modern look shows it.</summary>
    public static int Get(ShellSettings s, GraphicsRow r) => r switch
    {
        GraphicsRow.RenderScale => s.RenderScale,
        GraphicsRow.AntiAliasing => AntiAliasingRow.Index(s),
        GraphicsRow.ShadowDetail => ShadowDetailRow.Index(s),
        GraphicsRow.TerrainShadows => TerrainShadowsRow.Index(s),
        GraphicsRow.RoadDetail => RoadDetailRow.Index(s),
        GraphicsRow.MirrorAa => I76.Game.Sim.MirrorAaRow.Clamp(s.MirrorAa),
        GraphicsRow.SmokeLinger => SmokeLingerRow.Index(s),
        GraphicsRow.VisibilityRange => VisibilityRange.Index(s, look1997: false),
        GraphicsRow.WorldTextures or GraphicsRow.CarTextures => TextureRow.SizeOf(s, r),
        GraphicsRow.Vegetation => s.Vegetation,
        GraphicsRow.AmbientOcclusion => s.AmbientOcclusion ? 1 : 0,
        GraphicsRow.Reflections => s.Reflections ? 1 : 0,
        GraphicsRow.Haze => AirRows.ClampHaze(s.Haze),
        GraphicsRow.MirrorRate => (int)s.MirrorRate,
        GraphicsRow.Glow => AirRows.ClampGlow(s.Glow),
        GraphicsRow.SmokeLighting => SmokeLightingRow.Index(s),
        GraphicsRow.HeatShimmer => HeatShimmerRow.Index(s),
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    /// <summary>A row set as stepping it by hand would (Visibility Range as a choice; the textures to a size).</summary>
    private static void Set(ShellSettings s, GraphicsRow r, int v)
    {
        switch (r)
        {
            case GraphicsRow.RenderScale: s.RenderScale = v; break;
            case GraphicsRow.AntiAliasing: AntiAliasingRow.Put(s, v); break;
            case GraphicsRow.ShadowDetail: ShadowDetailRow.Put(s, v); break;
            case GraphicsRow.TerrainShadows: TerrainShadowsRow.Put(s, v); break;
            case GraphicsRow.RoadDetail: RoadDetailRow.Put(s, v); break;
            case GraphicsRow.MirrorAa: s.MirrorAa = v; break;
            case GraphicsRow.SmokeLinger: SmokeLingerRow.Put(s, v); break;
            case GraphicsRow.VisibilityRange: VisibilityRange.Put(s, false, v); break;
            case GraphicsRow.WorldTextures: s.WorldTextures = v; break;
            case GraphicsRow.CarTextures: s.CarTextures = v; break;
            case GraphicsRow.Vegetation: s.Vegetation = v; break;
            case GraphicsRow.AmbientOcclusion: s.AmbientOcclusion = v != 0; break;
            case GraphicsRow.Reflections: s.Reflections = v != 0; break;
            case GraphicsRow.Haze: s.Haze = v; break;
            case GraphicsRow.MirrorRate: s.MirrorRate = (I76.Game.Sim.MirrorRate)v; break;
            case GraphicsRow.Glow: s.Glow = v; break;
            case GraphicsRow.SmokeLighting: SmokeLightingRow.Put(s, v); break;
            case GraphicsRow.HeatShimmer: HeatShimmerRow.Put(s, v); break;
        }
    }

    /// <summary>A texture size asked for, as the packs offer it: the largest of the row's sizes up to it (0, the install's own, always).</summary>
    public static int BestSize(GraphicsRow r, int wanted, int[] available) =>
        TextureRow.Sizes(r, available).Where(z => z <= wanted).DefaultIfEmpty(0).Max();

    /// <summary>A column's value for a row, as it would be written (the default's where the column says so; a texture size as offered).</summary>
    private static int Want(GraphicsRow r, int column, int[] values, int[] available)
    {
        int v = values[column] == Default ? Get(new ShellSettings(), r) : values[column];
        return Texture(r) ? BestSize(r, v, available) : v;
    }

    /// <summary>The column the rows now match, or -1 (Custom). <paramref name="packs"/>: whether the texture rows are offered.</summary>
    public static int Match(ShellSettings s, int[] available, bool packs)
    {
        for (int c = 0; c < Names.Length; c++)
            if (Table.All(t => (Texture(t.Row) && !packs) || Get(s, t.Row) == Want(t.Row, c, t.Values, available))) return c;
        return -1;
    }

    /// <summary>The row's word now: a column's name, or Custom.</summary>
    public static string Word(ShellSettings s, int[] available, bool packs) => Match(s, available, packs) is var c and >= 0 ? Names[c] : Custom;

    /// <summary>A column chosen: every row of it set (the texture rows only when offered).</summary>
    public static void Apply(ShellSettings s, int column, int[] available, bool packs)
    {
        foreach (var (r, values) in Table)
            if (!Texture(r) || packs) Set(s, r, Want(r, column, values, available));
    }

    /// <summary>A click: the next column (from Custom, Original).</summary>
    public static void Step(ShellSettings s, int[] available, bool packs) => Apply(s, (Match(s, available, packs) + 1) % Names.Length, available, packs);

    /// <summary>Every row of the preset copied (the in-game pad's Done and Cancel for the row).</summary>
    public static void Copy(ShellSettings from, ShellSettings to)
    {
        foreach (var r in Rows) GraphicsPages.Copy(r, from, to);
    }
}
