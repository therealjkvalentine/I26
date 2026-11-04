namespace I76.Game.Shell;

/// <summary>A row of Graphic Detail: the original's six the port honours, then the port's own (the shell's sheet has them all; the in-game pad the ones a mission can take).</summary>
public enum GraphicsRow
{
    MonitorBrightness, ExtInstruments, RearMirror, ShadowDetail, TerrainDetail, AntiAliasing,
    Resolution, VSync, MaxFps, RenderScale, Renderer, LogFile,
    Vegetation, MapEdge,
    FieldOfView, HoodFinish, CarInteriors, MouseCamera, MouseInWindow, CameraReturn, ZoomReset, Garage3D,
    Look1997, PaletteGrade, CrashBlur,
}

/// <summary>
/// Graphic Detail's pages (the port's): the sheet keeps the original's six rows and, under them, an entry for each page of the
/// port's own rows; a page is the same sheet with its name, its rows and a way back. The shell's sheet and the in-game pad build
/// from this table, so a row sits on the same page in both, and Reset, Cancel and the shell's snapshot copy a page's rows through
/// <see cref="Copy"/>. No engine here: the tests check the table and the copies without one.
/// </summary>
public static class GraphicsPages
{
    public const int Sheet = 0, Display = 1, World = 2, Car = 3, Effects = 4;
    public static readonly string[] Names = ["Graphic Detail", "Display", "World", "Car & Cockpit", "Effects"];
    /// <summary>Each page's rows in order; the sheet's are the original's six, as the original has them.</summary>
    public static readonly GraphicsRow[][] Rows =
    [
        [GraphicsRow.MonitorBrightness, GraphicsRow.ExtInstruments, GraphicsRow.RearMirror, GraphicsRow.ShadowDetail, GraphicsRow.TerrainDetail, GraphicsRow.AntiAliasing],
        [GraphicsRow.Resolution, GraphicsRow.VSync, GraphicsRow.MaxFps, GraphicsRow.RenderScale, GraphicsRow.Renderer, GraphicsRow.LogFile],
        [GraphicsRow.Vegetation, GraphicsRow.MapEdge],
        [GraphicsRow.FieldOfView, GraphicsRow.HoodFinish, GraphicsRow.CarInteriors, GraphicsRow.MouseCamera, GraphicsRow.MouseInWindow, GraphicsRow.CameraReturn, GraphicsRow.ZoomReset, GraphicsRow.Garage3D],
        [GraphicsRow.Look1997, GraphicsRow.PaletteGrade, GraphicsRow.CrashBlur],
    ];

    /// <summary>The sheet's lines: fourteen, 23 px apart from y 118 on the shell's sheet, the pad's rows 1 to 14.</summary>
    public const int Lines = 14;
    /// <summary>The sheet's entries: one line per page from here (0-based), a blank line under the original's six.</summary>
    public const int FirstEntryLine = 7;
    /// <summary>A page on the shell's sheet: its name on the first line, its rows from the next; the pad prints the name over the art's title and starts its rows on row 1.</summary>
    public const int HeadingLine = 0, FirstRowLine = 1;

    public static int PageOf(GraphicsRow row)
    {
        for (int p = 0; p < Rows.Length; p++) if (Array.IndexOf(Rows[p], row) >= 0) return p;
        return -1;
    }

    /// <summary>A page's rows copied from one settings block to another: the sheet's the whole graphics block, as the original's Reset and Cancel treat it.</summary>
    public static void Copy(int page, ShellSettings from, ShellSettings to)
    {
        if (page == Sheet) { Array.Copy(from.Graphics, to.Graphics, 16); return; }
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

    private static void Copy(GraphicsRow r, ShellSettings f, ShellSettings t)
    {
        switch (r)
        {
            case GraphicsRow.Resolution: t.Resolution = f.Resolution; break;
            case GraphicsRow.VSync: t.VSync = f.VSync; break;
            case GraphicsRow.MaxFps: t.MaxFps = f.MaxFps; break;
            case GraphicsRow.RenderScale: t.RenderScale = f.RenderScale; break;
            case GraphicsRow.Renderer: t.Renderer = f.Renderer; break;
            case GraphicsRow.LogFile: t.LogFile = f.LogFile; break;
            case GraphicsRow.Vegetation: t.Vegetation = f.Vegetation; break;
            case GraphicsRow.MapEdge: t.MapEdge = f.MapEdge; break;
            case GraphicsRow.FieldOfView: t.FieldOfView = f.FieldOfView; break;
            case GraphicsRow.HoodFinish: t.HoodFinish = f.HoodFinish; break;
            case GraphicsRow.CarInteriors: t.CarInteriors = f.CarInteriors; break;
            case GraphicsRow.MouseCamera: t.MouseCamera = f.MouseCamera; break;
            case GraphicsRow.MouseInWindow: t.KeepMouseInWindow = f.KeepMouseInWindow; break;
            case GraphicsRow.CameraReturn: t.CameraReturn = f.CameraReturn; break;
            case GraphicsRow.ZoomReset: t.ZoomReset = f.ZoomReset; break;
            case GraphicsRow.Garage3D: t.Garage3D = f.Garage3D; break;
            // one row: off, the original's 3D card or its software renderer
            case GraphicsRow.Look1997: t.Look1997 = f.Look1997; t.SoftwareRenderer = f.SoftwareRenderer; break;
            case GraphicsRow.PaletteGrade: t.PaletteGrade = f.PaletteGrade; break;
            case GraphicsRow.CrashBlur: t.CrashBlur = f.CrashBlur; break;
            // the original's six live in the graphics block, copied whole with the sheet
        }
    }
}
