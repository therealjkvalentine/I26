namespace I76.Game.Shell;

/// <summary>A row of Graphic Detail: the original's rows the port honours (on the sheet, in the original's order), then the port's own on the pages (the shell's sheet has them all; the in-game pad the ones a mission can take).</summary>
public enum GraphicsRow
{
    Resolution, MonitorBrightness, VisibilityRange, TerrainDetail, ShadowDetail, ExtInstruments, RearMirror,
    Look1997, HdTextures, Vegetation, HoodGloss, CarInteriors, PaletteGrade, CrashBlur, Garage3D,
    AntiAliasing, RenderScale, VSync, MaxFps, ShowFps, Renderer, LogFile,
    FieldOfView,
    AmbientOcclusion,
}

/// <summary>
/// Graphic Detail's pages (the port's): the sheet keeps the original's rows in the original's order and words (grxdet1.map: Screen
/// Resolution, Monitor Brightness, Visibility Range, ..., Terrain Detail, ..., Shadow Detail, Ext. Instrum. Disp., Rear Mirror; the
/// 1997 renderer's own detail rows are gone) and, under them, an entry for each page of the port's own rows: how the game looks, how
/// fast it runs (then start-up and diagnostics), how the player looks around. A page is the same sheet with its name, its rows and a
/// way back. The shell's sheet and the in-game pad build from this table, so a row sits on the same page in both, and Reset, Cancel
/// and the shell's snapshot copy a page's rows through <see cref="Copy(int, ShellSettings, ShellSettings)"/>. No engine here: the
/// tests check the table and the copies without one.
/// </summary>
public static class GraphicsPages
{
    public const int Sheet = 0, Look = 1, Display = 2;
    public static readonly string[] Names = ["Graphic Detail", "Look", "Display"];
    /// <summary>Each page's rows in order; the sheet's are the original's, as the original has them.</summary>
    public static readonly GraphicsRow[][] Rows =
    [
        [GraphicsRow.Resolution, GraphicsRow.MonitorBrightness, GraphicsRow.VisibilityRange, GraphicsRow.TerrainDetail, GraphicsRow.ShadowDetail, GraphicsRow.ExtInstruments, GraphicsRow.RearMirror],
        [GraphicsRow.Look1997, GraphicsRow.HdTextures, GraphicsRow.Vegetation, GraphicsRow.AmbientOcclusion, GraphicsRow.HoodGloss, GraphicsRow.CarInteriors, GraphicsRow.PaletteGrade, GraphicsRow.CrashBlur, GraphicsRow.Garage3D],
        // Field of View, the Camera page's last row once the mouse rows (Mouse Camera, Camera Return, View Zoom Reset, Mouse In Window) became
        // upstream's Mouse Options screen's (pass 19), with the live view rows; the Camera page is gone
        [GraphicsRow.AntiAliasing, GraphicsRow.RenderScale, GraphicsRow.FieldOfView, GraphicsRow.VSync, GraphicsRow.MaxFps, GraphicsRow.ShowFps, GraphicsRow.Renderer, GraphicsRow.LogFile],
    ];

    /// <summary>Rows with a blank line over them on the shell's sheet: Display's start-up rows apart from the live ones.</summary>
    public static readonly GraphicsRow[] BlankBefore = [GraphicsRow.Renderer];

    /// <summary>The graphics block's bytes that are a page's row, not the sheet's: Anti-Aliasing (+0x8, the port's) on Display.</summary>
    public static readonly int[] PageBytes = [0x8];

    /// <summary>The sheet's lines: fourteen, 23 px apart from y 118 on the shell's sheet, the pad's rows 1 to 14.</summary>
    public const int Lines = 14;
    /// <summary>The sheet's entries: one line per page from here (0-based), a blank line under the original's seven.</summary>
    public const int FirstEntryLine = 8;
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
    /// Cancel treat it whole) but for the bytes a page owns (<see cref="PageBytes"/>): the sheet's Reset leaves Anti-Aliasing alone,
    /// Display's puts it back.
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

    /// <summary>Reset on a page: its rows to the defaults (the sheet's the default profile, as the original's Reset, Anti-Aliasing apart).</summary>
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
            case GraphicsRow.AntiAliasing: t.Graphics[0x8] = f.Graphics[0x8]; break;
            case GraphicsRow.VSync: t.VSync = f.VSync; break;
            case GraphicsRow.MaxFps: t.MaxFps = f.MaxFps; break;
            case GraphicsRow.RenderScale: t.RenderScale = f.RenderScale; break;
            case GraphicsRow.Renderer: t.Renderer = f.Renderer; break;
            case GraphicsRow.LogFile: t.LogFile = f.LogFile; break;
            case GraphicsRow.ShowFps: t.DevFps = f.DevFps; break;
            // one row over two keys: off, or on at a size (the size kept while off)
            case GraphicsRow.HdTextures: t.HdTextures = f.HdTextures; t.HdTextureSize = f.HdTextureSize; break;
            case GraphicsRow.Vegetation: t.Vegetation = f.Vegetation; break;
            case GraphicsRow.AmbientOcclusion: t.AmbientOcclusion = f.AmbientOcclusion; break;
            case GraphicsRow.FieldOfView: t.FieldOfView = f.FieldOfView; break;
            case GraphicsRow.HoodGloss: t.HoodGloss = f.HoodGloss; break;
            case GraphicsRow.CarInteriors: t.CarInteriors = f.CarInteriors; break;
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
    /// <summary>Indexed by DrawDistance's setting: Original (600 m) is the original's Far, DrawDistance.Far (20 km) the port's Max.</summary>
    public static readonly string[] Names = ["Far", "Max" + ShellSettings.PortMark];

    public static int Index(ShellSettings s, bool look1997) => I76.Formats.Imaging.DrawDistance.Resolve(s.DrawDistanceChoice, look1997);

    /// <summary>A click: the other value, written as a choice (which then holds in either look).</summary>
    public static void Step(ShellSettings s, bool look1997) => s.DrawDistanceChoice = 1 - Index(s, look1997);

    /// <summary>A value picked on the pad: written only when it is not what shows, so an untouched row keeps following the look.</summary>
    public static void Put(ShellSettings s, bool look1997, int v) { if (Index(s, look1997) != v) s.DrawDistanceChoice = v; }
}

/// <summary>
/// HD Textures: one row over [port] hd_textures and hd_texture_size, Off or 4x* (the port's, so marked): the 4x pack is the one the
/// modern look is made for, and a 2x or 3x pack saves video memory and a second of loading but no frame time (docs/perf/costs.md).
/// The loader still serves any whole multiple (HdPackFolders.Choose: the chosen size's tex-world-Nx, else the plain tex-world, else the
/// nearest size present), and a size set in the file or on the Developer panel shows as itself until the row is clicked. A pack with no
/// tex-world-4x (a plain tex-world, a zip, or only a 2x or 3x folder) gives Off and On*; no pack at all, no row. Off keeps the size; a
/// size change applies from the next mission.
/// </summary>
public static class HdTextureRow
{
    public const string On = "On" + ShellSettings.PortMark;
    /// <summary>The one size the row offers.</summary>
    public const int Size = I76.Formats.Shell.HdPackFolders.DefaultSize;

    /// <summary>The row's values for the sizes present (largest first), whether a pack of any kind was found and the size the file asks for; null for no row.</summary>
    public static string[]? Names(int[] available, bool anyPack, int size = Size)
    {
        if (available.Contains(Size)) return ["Off", $"{(size != Size && available.Contains(size) ? size : Size)}x{ShellSettings.PortMark}"];
        return anyPack || available.Length > 0 ? ["Off", On] : null;
    }

    public static int Index(ShellSettings s, int[] available) => s.HdTextures ? 1 : 0;

    /// <summary>A value picked: Off writes hd_textures=false and keeps the size, On writes true and 4 (when its folder is there); nothing when it is what shows.</summary>
    public static void Put(ShellSettings s, int[] available, int v)
    {
        if (Index(s, available) == v) return;
        s.HdTextures = v != 0;
        if (v > 0 && available.Contains(Size)) s.HdTextureSize = Size;
    }

    /// <summary>A click: the other value.</summary>
    public static void Step(ShellSettings s, int[] available, bool anyPack)
    {
        if (Names(available, anyPack, s.HdTextureSize) is null) return;
        Put(s, available, 1 - Index(s, available));
    }
}
