using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's pages (GraphicsPages): the sheet keeps the original's rows in the original's order and every port row sits on
/// exactly one of the two pages under them (Look, Display), each page fitting the sheet's lines; Reset puts back a page's
/// own rows and no other's (the sheet's the original's block but Anti-Aliasing, which is Display's), and the snapshot Cancel restores
/// from carries every row. Visibility Range, HD Textures, 8x Dash and 8x Cars, the rows over keys of their own. Without the engine.
/// </summary>
public class GraphicsPagesTests
{
    /// <summary>Each row's value in a settings block, as the pads show it.</summary>
    private static object? Read(GraphicsRow r, ShellSettings s) => r switch
    {
        GraphicsRow.Resolution => s.Resolution,
        GraphicsRow.MonitorBrightness => s.Graphics[0xc],
        GraphicsRow.VisibilityRange => s.DrawDistanceChoice,
        GraphicsRow.TerrainDetail => s.Graphics[0x2],
        GraphicsRow.ShadowDetail => s.Graphics[0x6],
        GraphicsRow.ExtInstruments => s.Graphics[0xe],
        GraphicsRow.RearMirror => s.Graphics[0xd],
        GraphicsRow.AntiAliasing => s.Graphics[0x8],
        GraphicsRow.VSync => s.VSync,
        GraphicsRow.MaxFps => s.MaxFps,
        GraphicsRow.RenderScale => s.RenderScale,
        GraphicsRow.Renderer => s.Renderer,
        GraphicsRow.LogFile => s.LogFile,
        GraphicsRow.ShowFps => s.DevFps,
        GraphicsRow.HdTextures => s.HdTextures,
        GraphicsRow.HdDash => s.HdDashSize,
        GraphicsRow.HdCars => s.HdCarSize,
        GraphicsRow.Vegetation => s.Vegetation,
        GraphicsRow.AmbientOcclusion => s.AmbientOcclusion,
        GraphicsRow.FieldOfView => s.FieldOfView,
        GraphicsRow.HoodGloss => s.HoodGloss,
        GraphicsRow.CarInteriors => s.CarInteriors,
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
        s.Resolution = "1280x720"; s.DrawDistanceChoice = I76.Formats.Imaging.DrawDistance.Original;
        s.VSync = false; s.MaxFps = I76.Sim.Views.FrameLimit.Unlimited + 1; s.RenderScale = I76.Game.Boot.RenderScale.Default + 1;
        s.Renderer = I76.Game.Boot.RendererChoice.OpenGL; s.LogFile = true; s.DevFps = true;
        s.HdTextures = false; s.HdDashSize = 2; s.HdCarSize = 3;
        s.Vegetation = 2; s.AmbientOcclusion = true;
        s.FieldOfView = I76.Sim.Views.ViewFov.Monitor + 3; s.HoodGloss = I76.Sim.Views.PaintFinish.Satin + 15; s.CarInteriors = false;
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

    /// <summary>The original's grxdet1.map in order, without the 1997 renderer's own rows (Clouds, Horizon and Terrain Textures, Terrain Resolution, Object Detail).</summary>
    [Fact]
    public void TheSheetKeepsTheOriginalsRowsInTheirOrder() => Assert.Equal(
        [GraphicsRow.Resolution, GraphicsRow.MonitorBrightness, GraphicsRow.VisibilityRange, GraphicsRow.TerrainDetail, GraphicsRow.ShadowDetail, GraphicsRow.ExtInstruments, GraphicsRow.RearMirror],
        GraphicsPages.Rows[GraphicsPages.Sheet]);

    /// <summary>Two pages: how it looks (1997 Mode first), how it is shown (the view's rows, Field of View among them) then start-up and diagnostics.</summary>
    [Fact]
    public void ThePagesAreLookAndDisplay()
    {
        Assert.Equal(["Graphic Detail", "Look", "Display"], GraphicsPages.Names);
        Assert.Equal([GraphicsRow.Look1997, GraphicsRow.HdTextures, GraphicsRow.HdDash, GraphicsRow.HdCars, GraphicsRow.Vegetation, GraphicsRow.AmbientOcclusion, GraphicsRow.HoodGloss, GraphicsRow.CarInteriors, GraphicsRow.PaletteGrade, GraphicsRow.CrashBlur, GraphicsRow.Garage3D],
            GraphicsPages.Rows[GraphicsPages.Look]);
        // the mouse rows are upstream's Mouse Options screen's (pass 19); Field of View, all the Camera page kept, is Display's
        Assert.Equal([GraphicsRow.AntiAliasing, GraphicsRow.RenderScale, GraphicsRow.FieldOfView, GraphicsRow.VSync, GraphicsRow.MaxFps, GraphicsRow.ShowFps, GraphicsRow.Renderer, GraphicsRow.LogFile],
            GraphicsPages.Rows[GraphicsPages.Display]);
    }

    [Fact]
    public void DisplaysStartUpRowsSitUnderABlankLine() => Assert.Equal([1, 2, 3, 4, 5, 6, 8, 9], GraphicsPages.LinesOf(GraphicsPages.Display));

    [Fact]
    public void EveryPageFitsTheSheetsLines()
    {
        // the sheet: its rows, a blank line, then the three entries; a page: its name, then its rows
        Assert.Equal(GraphicsPages.FirstEntryLine, GraphicsPages.Rows[GraphicsPages.Sheet].Length + 1);
        Assert.True(GraphicsPages.FirstEntryLine + (GraphicsPages.Rows.Length - 1) <= GraphicsPages.Lines);
        for (int p = 0; p < GraphicsPages.Rows.Length; p++)
            Assert.True(GraphicsPages.LinesOf(p)[^1] < GraphicsPages.Lines, $"{GraphicsPages.Names[p]} overflows the sheet");
    }

    [Fact]
    public void AFreshBlockHasTheDefaultsResetPutsBack()
    {
        var s = new ShellSettings();
        Assert.Equal(ShellSettings.DefaultGraphics, s.Graphics);
        Assert.Equal("", s.Resolution);
        Assert.Null(s.DrawDistanceChoice);
        Assert.True(s.HdTextures);
        Assert.Equal(I76.Formats.Shell.HdPackFolders.DefaultSize, s.HdDashSize);
        Assert.Equal(I76.Formats.Shell.HdPackFolders.DefaultSize, s.HdCarSize);
        Assert.Equal(I76.Sim.Views.CrashBlurMode.On, s.CrashBlur);
        Assert.Equal(1, s.Vegetation);
        Assert.False(s.AmbientOcclusion);
        Assert.True(s.VSync);
        Assert.Equal(I76.Sim.Views.FrameLimit.Unlimited, s.MaxFps);
        Assert.Equal(I76.Sim.Views.MouseLook.Normal, s.MouseCamera);
        Assert.Equal(I76.Sim.Views.PaintFinish.Default, s.HoodGloss);
        Assert.Equal(I76.Game.Boot.RenderScale.Default, s.RenderScale);
        Assert.Equal(I76.Sim.Views.ViewFov.Monitor, s.FieldOfView);
    }

    [Theory]
    [InlineData(GraphicsPages.Sheet)]
    [InlineData(GraphicsPages.Look)]
    [InlineData(GraphicsPages.Display)]
    public void ResetPutsBackThePagesRowsAlone(int page)
    {
        var s = Changed();
        var was = Changed();
        var defaults = new ShellSettings();
        GraphicsPages.Reset(s, page);
        foreach (var r in AllRows)
            Assert.Equal(Read(r, GraphicsPages.PageOf(r) == page ? defaults : was), Read(r, s));
    }

    /// <summary>
    /// The sheet's Reset is the original's (the graphics block back to the default profile, the bytes no row shows too) with the sheet's
    /// own rows: Screen Resolution back to Desktop and Visibility Range back to following the look. Anti-Aliasing, which moved to Display,
    /// keeps its byte; Display's Reset puts it back.
    /// </summary>
    [Fact]
    public void TheSheetsResetIsTheDefaultProfileButAntiAliasing()
    {
        var s = Changed();
        GraphicsPages.Reset(s, GraphicsPages.Sheet);
        for (int i = 0; i < 16; i++)
            Assert.Equal(i == 0x8 ? (byte)(ShellSettings.DefaultGraphics[i] + 1) : ShellSettings.DefaultGraphics[i], s.Graphics[i]);
        Assert.Equal("", s.Resolution);
        Assert.Null(s.DrawDistanceChoice);
        GraphicsPages.Reset(s, GraphicsPages.Display);
        Assert.Equal(ShellSettings.DefaultGraphics, s.Graphics);
    }

    /// <summary>The in-game pad's sheet has no Screen Resolution (the window is the shell's): its Reset leaves the window alone.</summary>
    [Fact]
    public void ThePadsSheetResetLeavesTheWindowAlone()
    {
        var s = Changed();
        var offered = GraphicsPages.Rows[GraphicsPages.Sheet].Where(r => r != GraphicsRow.Resolution).ToArray();
        GraphicsPages.Reset(s, GraphicsPages.Sheet, offered);
        Assert.Equal("1280x720", s.Resolution);
        Assert.Null(s.DrawDistanceChoice);
        Assert.Equal(ShellSettings.DefaultGraphics[0xc], s.Graphics[0xc]);
        Assert.Equal((byte)(ShellSettings.DefaultGraphics[0x8] + 1), s.Graphics[0x8]);
    }

    [Fact]
    public void TheSnapshotCarriesEveryRowAndCancelRestoresIt()
    {
        var opened = Changed();
        var snap = GraphicsPages.CopyAll(opened, new ShellSettings());
        foreach (var r in AllRows) Assert.Equal(Read(r, opened), Read(r, snap));
        Assert.Equal(opened.Graphics, snap.Graphics);
        var live = new ShellSettings();                 // every row changed since
        GraphicsPages.CopyAll(snap, live);
        foreach (var r in AllRows) Assert.Equal(Read(r, opened), Read(r, live));
        // a Visibility Range that follows the look is restored as following it, not as the value it showed
        var follows = new ShellSettings();
        GraphicsPages.CopyAll(follows, opened);
        Assert.Null(opened.DrawDistanceChoice);
    }

    /// <summary>The in-game pad's pages offer some of their page's rows: a row's Reset leaves the rest of its page alone (the window above all, which the pad's Cancel could not put back).</summary>
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
        GraphicsPages.Copy(GraphicsPages.Look, Changed(), to);
        var defaults = new ShellSettings();
        foreach (var r in AllRows)
            Assert.Equal(Read(r, GraphicsPages.PageOf(r) == GraphicsPages.Look ? Changed() : defaults), Read(r, to));
    }

    // ---- Visibility Range ----

    /// <summary>The original's words are Near, Med. and Far (Med. and Far both the mission's 600 m); the port has the 600 m as Far and its own 20 km as Max*.</summary>
    [Fact]
    public void VisibilityRangeIsFarTheKitsFadeOrTheMax()
    {
        Assert.Equal(["Far", "Max*", "2.3 km*"], VisibilityRange.Names);
        Assert.Equal("2.3 km*", VisibilityRange.Names[I76.Formats.Imaging.DrawDistance.Faded]);
        Assert.Equal("Far", VisibilityRange.Names[I76.Formats.Imaging.DrawDistance.Original]);
        Assert.Equal(I76.Formats.Imaging.DrawDistance.OriginalClip, I76.Formats.Imaging.DrawDistance.CameraFar(0));
    }

    [Fact]
    public void VisibilityRangeFollowsTheLookUntilChosen()
    {
        var s = new ShellSettings();
        Assert.Equal(0, VisibilityRange.Index(s, look1997: true));
        Assert.Equal(1, VisibilityRange.Index(s, look1997: false));
        // the pad's step to what shows writes nothing
        VisibilityRange.Put(s, false, 1);
        Assert.Null(s.DrawDistanceChoice);
        // a click is a choice, for both looks
        VisibilityRange.Step(s, false);
        Assert.Equal(I76.Formats.Imaging.DrawDistance.Original, s.DrawDistanceChoice);
        Assert.Equal(0, VisibilityRange.Index(s, look1997: false));
        // clicks go outward: Far, 2.3 km, Max, then Far again
        VisibilityRange.Step(s, true);
        Assert.Equal(I76.Formats.Imaging.DrawDistance.Faded, VisibilityRange.Index(s, look1997: true));
        VisibilityRange.Step(s, true);
        Assert.Equal(1, VisibilityRange.Index(s, look1997: true));
        GraphicsPages.Reset(s, GraphicsRow.VisibilityRange);
        Assert.Null(s.DrawDistanceChoice);
    }

    // ---- Ambient Occlusion ----

    /// <summary>Its own row on the Look page, Off for a first start; Look's Reset puts it back and the snapshot (Cancel) carries it.</summary>
    [Fact]
    public void AmbientOcclusionIsALookRowOffByDefault()
    {
        Assert.Equal(GraphicsPages.Look, GraphicsPages.PageOf(GraphicsRow.AmbientOcclusion));
        Assert.False(new ShellSettings().AmbientOcclusion);
        var s = new ShellSettings { AmbientOcclusion = true };
        var snap = GraphicsPages.CopyAll(s, new ShellSettings());
        Assert.True(snap.AmbientOcclusion);
        GraphicsPages.Reset(s, GraphicsPages.Look);
        Assert.False(s.AmbientOcclusion);
        GraphicsPages.CopyAll(snap, s);
        Assert.True(s.AmbientOcclusion);
        // the sheet's Reset (Shadow Detail's own) leaves it alone: it is no longer Shadow Detail High's
        GraphicsPages.Reset(s, GraphicsPages.Sheet);
        Assert.True(s.AmbientOcclusion);
        GraphicsPages.Reset(s, GraphicsRow.AmbientOcclusion);
        Assert.False(s.AmbientOcclusion);
    }

    /// <summary>The first start's Anti-Aliasing is 2x (the byte 1); 4x stays offered.</summary>
    [Fact]
    public void AntiAliasingStartsAt2x()
    {
        Assert.Equal(1, ShellSettings.DefaultGraphics[0x8]);
        Assert.Equal("2x", I76.Game.Boot.RenderScale.AntiAliasing[ShellSettings.DefaultGraphics[0x8]]);
        Assert.Contains("4x", I76.Game.Boot.RenderScale.AntiAliasing);
    }

    // ---- HD Textures, 8x Dash, 8x Cars ----

    /// <summary>Off or On: the switch over the pack, whatever its sizes (the 8x rows choose those); with no pack no row.</summary>
    [Fact]
    public void HdTexturesIsOffOrOn()
    {
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([8, 4], true)!);
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([4, 3, 2], true)!);
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([4], true)!);
        // no tex-world-4x: a pack of another size, a plain tex-world or a zip is on or off too
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([3], true)!);
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([3, 2], false)!);
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([], true)!);
        // no pack: no row
        Assert.Null(HdTextureRow.Names([], false));
        var s = new ShellSettings { HdDashSize = 8, HdCarSize = 3 };
        Assert.Equal(1, HdTextureRow.Index(s));
        HdTextureRow.Step(s);      // Off, the sizes kept
        Assert.Equal((false, 0, 8, 3), (s.HdTextures, HdTextureRow.Index(s), s.HdDashSize, s.HdCarSize));
        HdTextureRow.Step(s);
        Assert.Equal((true, 1, 8, 3), (s.HdTextures, HdTextureRow.Index(s), s.HdDashSize, s.HdCarSize));
        HdTextureRow.Put(s, 0);
        Assert.False(s.HdTextures);
    }

    /// <summary>The 8x rows are shown when tex-world-8x is beside the 4x, and are Off (4) or On (8) each over its own key.</summary>
    [Fact]
    public void The8xRowsAreOfferedWithThe8xFolderBesideThe4x()
    {
        Assert.True(Hd8xRow.Offered([8, 4]));
        Assert.True(Hd8xRow.Offered([8, 4, 2]));
        Assert.False(Hd8xRow.Offered([4, 3, 2]));   // no 8x folder
        Assert.False(Hd8xRow.Offered([8]));         // an 8x with no 4x under it: the HD Textures row alone, On or Off
        Assert.False(Hd8xRow.Offered([]));
        Assert.Equal(["Off", "On*"], Hd8xRow.Names);
        Assert.Equal("8x Dash", Hd8xRow.Label(GraphicsRow.HdDash));
        Assert.Equal("8x Cars", Hd8xRow.Label(GraphicsRow.HdCars));
    }

    /// <summary>Each 8x row reads and writes its own key and leaves the other's alone: a click is Off, On, Off; On writes 8 and Off 4.</summary>
    [Fact]
    public void The8xRowsAreEachOverTheirOwnKey()
    {
        var s = new ShellSettings { HdDashSize = 8, HdCarSize = 4 };
        Assert.Equal((1, 0), (Hd8xRow.Index(s, GraphicsRow.HdDash), Hd8xRow.Index(s, GraphicsRow.HdCars)));
        Hd8xRow.Step(s, GraphicsRow.HdCars);
        Assert.Equal((8, 8), (s.HdDashSize, s.HdCarSize));
        Hd8xRow.Step(s, GraphicsRow.HdDash);
        Assert.Equal((4, 8), (s.HdDashSize, s.HdCarSize));
        Hd8xRow.Step(s, GraphicsRow.HdDash);
        Assert.Equal((8, 8), (s.HdDashSize, s.HdCarSize));
        Hd8xRow.Put(s, GraphicsRow.HdCars, 0);
        Assert.Equal((8, 4), (s.HdDashSize, s.HdCarSize));
        // a 3x or 2x in the file shows as Off and stays until the row is clicked; a click writes 4 or 8
        var p = new ShellSettings { HdDashSize = 3, HdCarSize = 2 };
        Assert.Equal((0, 0), (Hd8xRow.Index(p, GraphicsRow.HdDash), Hd8xRow.Index(p, GraphicsRow.HdCars)));
        Hd8xRow.Put(p, GraphicsRow.HdDash, 0);
        Assert.Equal(3, p.HdDashSize);
        Hd8xRow.Step(p, GraphicsRow.HdDash);
        Assert.Equal((8, 2), (p.HdDashSize, p.HdCarSize));
        Hd8xRow.Step(p, GraphicsRow.HdDash);
        Assert.Equal((4, 2), (p.HdDashSize, p.HdCarSize));
        // the switch's row leaves both alone
        HdTextureRow.Put(p, 0);
        Assert.Equal((false, 4, 2), (p.HdTextures, p.HdDashSize, p.HdCarSize));
    }

    /// <summary>The loader reads each texture from its kind's size: the cockpit's art (z*) from the dash's, every other from the cars'.</summary>
    [Fact]
    public void TheChainIsPerKind()
    {
        Func<string, bool> has = f => f is "tex-world-8x" or "tex-world-4x";
        Assert.Equal(["tex-world-8x", "tex-world-4x"], I76.Formats.Shell.HdPackFolders.ChainFor("zdash101.map", 8, 4, has));
        Assert.Equal(["tex-world-4x"], I76.Formats.Shell.HdPackFolders.ChainFor("pp11bkb1.map", 8, 4, has));
        Assert.Equal(["tex-world-4x"], I76.Formats.Shell.HdPackFolders.ChainFor("zspeedo6.vqm", 4, 8, has));
        Assert.Equal(["tex-world-8x", "tex-world-4x"], I76.Formats.Shell.HdPackFolders.ChainFor("pp11bkb1.map", 4, 8, has));
    }

    /// <summary>The loader keeps every whole multiple: the 3x and 2x folders are still found and chosen.</summary>
    [Fact]
    public void TheLoaderStillServesEverySize()
    {
        Assert.Equal([8, 4, 3, 2], I76.Formats.Shell.HdPackFolders.Sizes);
        Assert.Equal("tex-world-2x", I76.Formats.Shell.HdPackFolders.Choose(2, f => f is "tex-world-4x" or "tex-world-2x"));
        Assert.Equal("tex-world-3x", I76.Formats.Shell.HdPackFolders.Choose(4, f => f is "tex-world-3x" or "tex-world-2x"));
    }

    /// <summary>An 8 in the file with the 8x folder gone loads the 4x (HdPackFolders.Choose) and the 8x rows are not shown.</summary>
    [Fact]
    public void An8WithoutItsFolderLoadsThe4x()
    {
        Assert.Equal("tex-world-4x", I76.Formats.Shell.HdPackFolders.Choose(8, f => f is "tex-world-4x"));
        Assert.Equal(["tex-world-4x"], I76.Formats.Shell.HdPackFolders.ChainFor("zdash101.map", 8, 8, f => f is "tex-world-4x"));
        Assert.False(Hd8xRow.Offered([4]));
    }
}
