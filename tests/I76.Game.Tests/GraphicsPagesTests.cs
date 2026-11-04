using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's pages (GraphicsPages): the sheet keeps the original's six rows and every port row sits on exactly one of the
/// four pages under them, each page fitting the sheet's lines; Reset puts back a page's own rows and no other's (the sheet's the
/// original's whole block), and the snapshot Cancel restores from carries every row. Without the engine.
/// </summary>
public class GraphicsPagesTests
{
    private static readonly GraphicsRow[] OriginalsSix =
        [GraphicsRow.MonitorBrightness, GraphicsRow.ExtInstruments, GraphicsRow.RearMirror, GraphicsRow.ShadowDetail, GraphicsRow.TerrainDetail, GraphicsRow.AntiAliasing];

    /// <summary>Each row's value in a settings block, as the pads show it.</summary>
    private static object Read(GraphicsRow r, ShellSettings s) => r switch
    {
        GraphicsRow.MonitorBrightness => s.Graphics[0xc],
        GraphicsRow.ExtInstruments => s.Graphics[0xe],
        GraphicsRow.RearMirror => s.Graphics[0xd],
        GraphicsRow.ShadowDetail => s.Graphics[0x6],
        GraphicsRow.TerrainDetail => s.Graphics[0x2],
        GraphicsRow.AntiAliasing => s.Graphics[0x8],
        GraphicsRow.Resolution => s.Resolution,
        GraphicsRow.VSync => s.VSync,
        GraphicsRow.MaxFps => s.MaxFps,
        GraphicsRow.RenderScale => s.RenderScale,
        GraphicsRow.Renderer => s.Renderer,
        GraphicsRow.LogFile => s.LogFile,
        GraphicsRow.ShowFps => s.DevFps,
        GraphicsRow.Vegetation => s.Vegetation,
        GraphicsRow.FieldOfView => s.FieldOfView,
        GraphicsRow.HoodFinish => s.HoodFinish,
        GraphicsRow.CarInteriors => s.CarInteriors,
        GraphicsRow.MouseCamera => s.MouseCamera,
        GraphicsRow.MouseInWindow => s.KeepMouseInWindow,
        GraphicsRow.CameraReturn => s.CameraReturn,
        GraphicsRow.ZoomReset => s.ZoomReset,
        GraphicsRow.Garage3D => s.Garage3D,
        GraphicsRow.Look1997 => (s.Look1997, s.SoftwareRenderer),
        GraphicsRow.PaletteGrade => s.PaletteGrade,
        GraphicsRow.CrashBlur => s.CrashBlur,
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    /// <summary>A block with every row off its default.</summary>
    private static ShellSettings Changed()
    {
        var s = new ShellSettings();
        for (int i = 0; i < 16; i++) s.Graphics[i] = (byte)(ShellSettings.DefaultGraphics[i] + 1);
        s.Resolution = "1280x720"; s.VSync = false; s.MaxFps = I76.Sim.Views.FrameLimit.Unlimited + 1; s.RenderScale = I76.Game.Boot.RenderScale.Default + 1;
        s.Renderer = I76.Game.Boot.RendererChoice.OpenGL; s.LogFile = true; s.DevFps = true;
        s.Vegetation = 2;
        s.FieldOfView = I76.Sim.Views.ViewFov.Monitor + 3; s.HoodFinish = (I76.Sim.Views.PaintFinish.Default + 1) % I76.Sim.Views.PaintFinish.Names.Length; s.CarInteriors = false;
        s.MouseCamera = I76.Sim.Views.MouseLook.Normal + 1; s.KeepMouseInWindow = true; s.CameraReturn = false; s.ZoomReset = false; s.Garage3D = false;
        s.Look1997 = true; s.SoftwareRenderer = true; s.PaletteGrade = true; s.CrashBlur = I76.Sim.Views.CrashBlurMode.Off;
        return s;
    }

    private static IEnumerable<GraphicsRow> AllRows => Enum.GetValues<GraphicsRow>();

    [Fact]
    public void EveryRowIsOnOnePage()
    {
        Assert.Equal(GraphicsPages.Names.Length, GraphicsPages.Rows.Length);
        foreach (var r in AllRows)
        {
            int pages = GraphicsPages.Rows.Count(p => p.Contains(r));
            Assert.True(pages == 1, $"{r} is on {pages} pages");
            Assert.True(GraphicsPages.Rows[GraphicsPages.PageOf(r)].Contains(r));
        }
    }

    [Fact]
    public void TheSheetKeepsTheOriginalsSixInTheirOrder() => Assert.Equal(OriginalsSix, GraphicsPages.Rows[GraphicsPages.Sheet]);

    [Fact]
    public void EveryPageFitsTheSheetsLines()
    {
        // the sheet: the six, a blank line, then the four entries; a page: its name, then its rows
        Assert.True(GraphicsPages.Rows[GraphicsPages.Sheet].Length < GraphicsPages.FirstEntryLine);
        Assert.True(GraphicsPages.FirstEntryLine + (GraphicsPages.Rows.Length - 1) <= GraphicsPages.Lines);
        for (int p = GraphicsPages.Display; p <= GraphicsPages.Effects; p++)
            Assert.True(GraphicsPages.FirstRowLine + GraphicsPages.Rows[p].Length <= GraphicsPages.Lines, $"{GraphicsPages.Names[p]} overflows the sheet");
    }

    [Fact]
    public void AFreshBlockHasTheDefaultsResetPutsBack()
    {
        var s = new ShellSettings();
        Assert.Equal(ShellSettings.DefaultGraphics, s.Graphics);
        Assert.Equal(I76.Sim.Views.CrashBlurMode.On, s.CrashBlur);
        Assert.Equal(1, s.Vegetation);
        Assert.True(s.VSync);
        Assert.Equal(I76.Sim.Views.FrameLimit.Unlimited, s.MaxFps);
        Assert.Equal(I76.Sim.Views.MouseLook.Normal, s.MouseCamera);
        Assert.Equal(I76.Sim.Views.PaintFinish.Default, s.HoodFinish);
        Assert.Equal(I76.Game.Boot.RenderScale.Default, s.RenderScale);
        Assert.Equal(I76.Sim.Views.ViewFov.Monitor, s.FieldOfView);
    }

    [Theory]
    [InlineData(GraphicsPages.Sheet)]
    [InlineData(GraphicsPages.Display)]
    [InlineData(GraphicsPages.World)]
    [InlineData(GraphicsPages.Car)]
    [InlineData(GraphicsPages.Effects)]
    public void ResetPutsBackThePagesRowsAlone(int page)
    {
        var s = Changed();
        var was = Changed();
        var defaults = new ShellSettings();
        GraphicsPages.Reset(s, page);
        foreach (var r in AllRows)
            Assert.Equal(Read(r, GraphicsPages.PageOf(r) == page ? defaults : was), Read(r, s));
    }

    /// <summary>The sheet's Reset is the original's: the whole graphics block back to the default profile, the bytes no row shows too.</summary>
    [Fact]
    public void TheSheetsResetIsTheDefaultProfile()
    {
        var s = Changed();
        GraphicsPages.Reset(s, GraphicsPages.Sheet);
        Assert.Equal(ShellSettings.DefaultGraphics, s.Graphics);
    }

    [Fact]
    public void TheSnapshotCarriesEveryRowAndCancelRestoresIt()
    {
        var opened = Changed();
        var snap = GraphicsPages.CopyAll(opened, new ShellSettings());
        foreach (var r in AllRows) Assert.Equal(Read(r, opened), Read(r, snap));
        var live = new ShellSettings();                 // every row changed since
        GraphicsPages.CopyAll(snap, live);
        foreach (var r in AllRows) Assert.Equal(Read(r, opened), Read(r, live));
    }

    /// <summary>The in-game pad's Display and Car & Cockpit offer some of their page's rows: a row's Reset leaves the rest of its page alone (the window above all, which the pad's Cancel could not put back).</summary>
    [Fact]
    public void ARowsResetLeavesItsPageAlone()
    {
        var s = Changed();
        GraphicsPages.Reset(s, GraphicsRow.VSync);
        var defaults = new ShellSettings();
        foreach (var r in AllRows)
            Assert.Equal(Read(r, r == GraphicsRow.VSync ? defaults : Changed()), Read(r, s));
    }

    [Fact]
    public void APagesCopyLeavesTheOthersAlone()
    {
        var to = new ShellSettings();
        GraphicsPages.Copy(GraphicsPages.World, Changed(), to);
        var defaults = new ShellSettings();
        foreach (var r in AllRows)
            Assert.Equal(Read(r, GraphicsPages.PageOf(r) == GraphicsPages.World ? Changed() : defaults), Read(r, to));
    }
}
