namespace I76.Game.Shell;

/// <summary>A row of Graphic Detail: the original's rows the port honours (on the sheet, in the original's order) and the port's own (on the sheet under them, or on Advanced; the shell's sheet has them all, the in-game pad the ones a mission can take).</summary>
public enum GraphicsRow
{
    Resolution, MonitorBrightness, VisibilityRange, TerrainDetail, ShadowDetail, ExtInstruments, RearMirror,
    SideMirrors, AntiAliasing, FieldOfView, VSync, MaxFps,
    Look1997, PaletteGrade, CrashBlur, WorldTextures, CarTextures, Vegetation, AmbientOcclusion, MirrorRate, HoodGloss,
    RenderScale, ShowFps, Renderer, Garage3D,
}

/// <summary>
/// Graphic Detail's pages (the port's): the sheet keeps the original's rows in the original's order and words (grxdet1.map: Screen
/// Resolution, Monitor Brightness, Visibility Range, ..., Terrain Detail, ..., Shadow Detail, Ext. Instrum. Disp., Rear Mirror; the
/// 1997 renderer's own detail rows are gone), then the port's rows a player reaches for most (Side Mirrors by Rear Mirror,
/// Anti-Aliasing, Field of View, VSync, Max FPS) and an entry for Advanced: the look (1997 Mode first), the textures, the modern look's extras and
/// start-up. Each fits one sheet with a line to spare. A page is the same sheet with its name, its rows and a way back. The shell's
/// sheet and the in-game pad build from this table, so a row sits on the same page in both, and Reset, Cancel and the shell's
/// snapshot copy a page's rows through <see cref="Copy(int, ShellSettings, ShellSettings)"/>. No engine here: the tests check the
/// table and the copies without one.
/// </summary>
public static class GraphicsPages
{
    public const int Sheet = 0, Advanced = 1;
    public static readonly string[] Names = ["Graphic Detail", "Advanced"];
    /// <summary>Each page's rows in order; the sheet's start with the original's, as the original has them.</summary>
    public static readonly GraphicsRow[][] Rows =
    [
        [GraphicsRow.Resolution, GraphicsRow.MonitorBrightness, GraphicsRow.VisibilityRange, GraphicsRow.TerrainDetail, GraphicsRow.ShadowDetail, GraphicsRow.ExtInstruments, GraphicsRow.RearMirror,
            GraphicsRow.SideMirrors, GraphicsRow.AntiAliasing, GraphicsRow.FieldOfView, GraphicsRow.VSync, GraphicsRow.MaxFps],
        [GraphicsRow.Look1997, GraphicsRow.PaletteGrade, GraphicsRow.CrashBlur, GraphicsRow.WorldTextures, GraphicsRow.CarTextures, GraphicsRow.Vegetation,
            GraphicsRow.AmbientOcclusion, GraphicsRow.MirrorRate, GraphicsRow.HoodGloss, GraphicsRow.RenderScale, GraphicsRow.ShowFps, GraphicsRow.Renderer, GraphicsRow.Garage3D],
    ];

    /// <summary>The original's rows: the sheet's first seven.</summary>
    public const int OriginalRows = 7;

    /// <summary>Rows with a blank line over them on the shell's sheet (none: Advanced fits with a line to spare without one).</summary>
    public static readonly GraphicsRow[] BlankBefore = [];

    /// <summary>The graphics block's bytes that are a page's row, not the sheet's (none: Anti-Aliasing, +0x8, is the sheet's again).</summary>
    public static readonly int[] PageBytes = [];

    /// <summary>The sheet's lines: fourteen, 23 px apart from y 118 on the shell's sheet, the pad's rows 1 to 14.</summary>
    public const int Lines = 14;
    /// <summary>
    /// The sheet's entry for Advanced: its line (0-based), a blank line under the sheet's rows where the sheet has one to give (the
    /// shell's sheet, whose last line is the frame-rate estimate, has none since Side Mirrors: the entry goes straight under the rows).
    /// </summary>
    public static int FirstEntryLine => Math.Min(Rows[Sheet].Length + 1, Lines - Rows.Length);
    /// <summary>A page on the shell's sheet: its name on the first line, its rows from the next; the pad prints the name over the art's title and starts its rows on row 1.</summary>
    public const int HeadingLine = 0, FirstRowLine = 1;

    public static int PageOf(GraphicsRow row)
    {
        for (int p = 0; p < Rows.Length; p++) if (Array.IndexOf(Rows[p], row) >= 0) return p;
        return -1;
    }

    /// <summary>The shell's line of each of a page's rows (a page's start on <see cref="FirstRowLine"/>, the sheet's on 0), the blank lines skipped.</summary>
    public static int[] LinesOf(int page)
    {
        var rows = Rows[page];
        var lines = new int[rows.Length];
        int line = page == Sheet ? 0 : FirstRowLine;
        for (int k = 0; k < rows.Length; k++)
        {
            if (Array.IndexOf(BlankBefore, rows[k]) >= 0) line++;
            lines[k] = line++;
        }
        return lines;
    }

    /// <summary>
    /// A page's rows copied from one settings block to another. The sheet's take the graphics block with them (the original's Reset and
    /// Cancel treat it whole) but for the bytes a page owns (<see cref="PageBytes"/>, none now).
    /// </summary>
    public static void Copy(int page, ShellSettings from, ShellSettings to)
    {
        if (page == Sheet)
            for (int i = 0; i < 16; i++) if (Array.IndexOf(PageBytes, i) < 0) to.Graphics[i] = from.Graphics[i];
        foreach (var r in Rows[page]) Copy(r, from, to);
    }

    /// <summary>Every page's rows copied (the shell's snapshot on opening, and Cancel's restore from it).</summary>
    public static ShellSettings CopyAll(ShellSettings from, ShellSettings to)
    {
        for (int p = 0; p < Rows.Length; p++) Copy(p, from, to);
        return to;
    }

    /// <summary>Reset on a page: its rows to the defaults (the sheet's the default profile, as the original's Reset).</summary>
    public static void Reset(ShellSettings s, int page) => Copy(page, new ShellSettings(), s);

    /// <summary>Reset of one row: the in-game pad's pages, which offer only some of a page's rows, reset only those.</summary>
    public static void Reset(ShellSettings s, GraphicsRow row) => Copy(row, new ShellSettings(), s);

    /// <summary>Reset on the in-game pad's page: the rows it offers (on the sheet with the graphics block, as the shell's, but not the window, which the pad does not show).</summary>
    public static void Reset(ShellSettings s, int page, IEnumerable<GraphicsRow> offered)
    {
        var d = new ShellSettings();
        if (page == Sheet)
            for (int i = 0; i < 16; i++) if (Array.IndexOf(PageBytes, i) < 0) s.Graphics[i] = d.Graphics[i];
        foreach (var r in offered) Copy(r, d, s);
    }

    /// <summary>One row's keys copied (also the pad's Done and Cancel for a row over two keys, or a key that may be absent).</summary>
    public static void Copy(GraphicsRow r, ShellSettings f, ShellSettings t)
    {
        switch (r)
        {
            case GraphicsRow.Resolution: t.Resolution = f.Resolution; break;
            // absent (null) follows the look: the copy keeps that, and Reset goes back to it
            case GraphicsRow.VisibilityRange: t.DrawDistanceChoice = f.DrawDistanceChoice; break;
            case GraphicsRow.MonitorBrightness: t.Graphics[0xc] = f.Graphics[0xc]; break;
            case GraphicsRow.TerrainDetail: t.Graphics[0x2] = f.Graphics[0x2]; break;
            case GraphicsRow.ShadowDetail: t.Graphics[0x6] = f.Graphics[0x6]; break;
            case GraphicsRow.ExtInstruments: t.Graphics[0xe] = f.Graphics[0xe]; break;
            case GraphicsRow.RearMirror: t.Graphics[0xd] = f.Graphics[0xd]; break;
            case GraphicsRow.SideMirrors: t.SideMirrors = f.SideMirrors; break;
            case GraphicsRow.MirrorRate: t.MirrorRate = f.MirrorRate; break;
            case GraphicsRow.AntiAliasing: t.Graphics[0x8] = f.Graphics[0x8]; break;
            case GraphicsRow.VSync: t.VSync = f.VSync; break;
            case GraphicsRow.MaxFps: t.MaxFps = f.MaxFps; break;
            case GraphicsRow.RenderScale: t.RenderScale = f.RenderScale; break;
            case GraphicsRow.Renderer: t.Renderer = f.Renderer; break;
            case GraphicsRow.ShowFps: t.DevFps = f.DevFps; break;
            // the texture packs' sizes, 0 for the originals
            case GraphicsRow.WorldTextures: t.WorldTextures = f.WorldTextures; break;
            case GraphicsRow.CarTextures: t.CarTextures = f.CarTextures; break;
            case GraphicsRow.Vegetation: t.Vegetation = f.Vegetation; break;
            case GraphicsRow.AmbientOcclusion: t.AmbientOcclusion = f.AmbientOcclusion; break;
            case GraphicsRow.FieldOfView: t.FieldOfView = f.FieldOfView; break;
            case GraphicsRow.HoodGloss: t.HoodGloss = f.HoodGloss; break;
            case GraphicsRow.Garage3D: t.Garage3D = f.Garage3D; break;
            // one row: off, the original's 3D card or its software renderer
            case GraphicsRow.Look1997: t.Look1997 = f.Look1997; t.SoftwareRenderer = f.SoftwareRenderer; break;
            case GraphicsRow.PaletteGrade: t.PaletteGrade = f.PaletteGrade; break;
            case GraphicsRow.CrashBlur: t.CrashBlur = f.CrashBlur; break;
        }
    }
}

/// <summary>
/// Visibility Range: the original's row, over the port's [port] draw_distance. The original's values are Near, Med. and Far
/// (grxdet1.map's third item, its value names at 0x5a60e0; options byte +0xa): Near clipped at 150 m, Med. and Far both at the
/// mission's own far distance (600 in every mission). The port has that 600 m clip, shown as Far, and its own 20 km, Max*; no Near.
/// With no key the value follows the look, and the row shows the look's.
/// </summary>
public static class VisibilityRange
{
    /// <summary>
    /// Indexed by DrawDistance's setting: Original (600 m) is the original's Far, DrawDistance.Far (20 km) the port's Max, DrawDistance.Faded
    /// the patch kit's 2300 m faded into the horizon (2.3 km).
    /// </summary>
    public static readonly string[] Names = ["Far", "Max" + ShellSettings.PortMark, "2.3 km" + ShellSettings.PortMark];

    /// <summary>A click's order, nearest first: Far, 2.3 km, Max.</summary>
    private static readonly int[] Order = [I76.Formats.Imaging.DrawDistance.Original, I76.Formats.Imaging.DrawDistance.Faded, I76.Formats.Imaging.DrawDistance.Far];

    public static int Index(ShellSettings s, bool look1997) => I76.Formats.Imaging.DrawDistance.Resolve(s.DrawDistanceChoice, look1997);

    /// <summary>A click: the next value outward (Max goes back to Far), written as a choice (which then holds in either look).</summary>
    public static void Step(ShellSettings s, bool look1997) => s.DrawDistanceChoice = Order[(System.Array.IndexOf(Order, Index(s, look1997)) + 1) % Order.Length];

    /// <summary>A value picked on the pad: written only when it is not what shows, so an untouched row keeps following the look.</summary>
    public static void Put(ShellSettings s, bool look1997, int v) { if (Index(s, look1997) != v) s.DrawDistanceChoice = v; }
}

/// <summary>
/// World Textures and Car &amp; Dash Textures: the texture pack in two rows ([port] world_textures and car_textures), each the size its
/// textures are drawn at or 1997, the install's own (the original's, so unmarked). World Textures is 1997 or 4x* (the ground, the
/// buildings, the props and the sky); Car &amp; Dash Textures is 1997, 4x* or 8x* (the cars and the cockpit's own art, the dash, its
/// gauges and panels: one row, since the cars' 8x is tens of megabytes and the dash's half a gigabyte), the 8x when tex-world-8x is
/// beside the 4x. Shown when a pack of any kind was found: no pack, no rows. A size the file names that the row does not offer (a 2x
/// or 3x, an 8x whose folder has gone) shows as itself in the 4x's place until the row is stepped. The defaults (ShellSettings.HdDefault,
/// HdTextures.FirstStart) are 4x and 8x, the 8x only where the dash's half gigabyte fits; Reset puts them back. Both going to or from
/// 1997 swaps the pictures at once; a size applies from the next mission.
/// </summary>
public static class TextureRow
{
    /// <summary>The install's own textures: the original's, unmarked.</summary>
    public const string Original = "1997";
    /// <summary>The size the pack is drawn at by default, and the larger one Car &amp; Dash Textures offers when its folder is there.</summary>
    public const int Size = I76.Formats.Shell.HdPackFolders.DefaultSize, Large = 8;

    public static string Label(GraphicsRow r) => r == GraphicsRow.WorldTextures ? "World Textures" : "Car & Dash Textures";

    /// <summary>The rows are shown when a pack was found (<paramref name="available"/>: the sizes whose folders are there; <paramref name="anyPack"/>: a plain folder or a zip too).</summary>
    public static bool Offered(int[] available, bool anyPack) => anyPack || available.Length > 0;

    /// <summary>
    /// The sizes a row steps through (0 the originals): World 1997 and 4x; Car &amp; Dash the 8x too when its pack is there, 1997 and 8x
    /// alone when the 8x is there without the 4x (it holds every car and cockpit texture; its 4x would be the 8x read again).
    /// </summary>
    public static int[] Sizes(GraphicsRow r, int[] available) =>
        r != GraphicsRow.CarTextures || !available.Contains(Large) ? [0, Size]
        : available.Any(s => s != Large) ? [0, Size, Large] : [0, Large];

    public static int SizeOf(ShellSettings s, GraphicsRow r) => r == GraphicsRow.WorldTextures ? s.WorldTextures : s.CarTextures;

    public static string Word(int size) => size == 0 ? Original : $"{size}x" + ShellSettings.PortMark;

    /// <summary>The value shown: the size's place among <see cref="Sizes"/>, a size not among them in the 4x's.</summary>
    public static int Index(ShellSettings s, GraphicsRow r, int[] available)
    {
        int i = Array.IndexOf(Sizes(r, available), SizeOf(s, r));
        return i >= 0 ? i : 1;
    }

    /// <summary>The row's words: the sizes', the 4x's place showing a size set by hand (3x*) until the row is stepped.</summary>
    public static string[] Names(ShellSettings s, GraphicsRow r, int[] available)
    {
        var sizes = Sizes(r, available);
        var names = sizes.Select(Word).ToArray();
        if (Array.IndexOf(sizes, SizeOf(s, r)) < 0) names[1] = Word(SizeOf(s, r));
        return names;
    }

    /// <summary>A value picked: nothing when it is what shows (a size set by hand stays until the row is changed).</summary>
    public static void Put(ShellSettings s, GraphicsRow r, int[] available, int v)
    {
        if (Index(s, r, available) == v) return;
        int size = Sizes(r, available)[v];
        if (r == GraphicsRow.WorldTextures) s.WorldTextures = size; else s.CarTextures = size;
    }

    /// <summary>A click: the next size, past the last back to 1997.</summary>
    public static void Step(ShellSettings s, GraphicsRow r, int[] available) => Put(s, r, available, (Index(s, r, available) + 1) % Sizes(r, available).Length);
}
