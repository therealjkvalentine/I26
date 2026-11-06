using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's pages (GraphicsPages): the sheet keeps the original's rows in the original's order and every port row sits on
/// exactly one of the two pages under them (Look, Display), each page fitting the sheet's lines; Reset puts back a page's
/// own rows and no other's (the sheet's the original's block but Anti-Aliasing, which is Display's), and the snapshot Cancel restores
/// from carries every row. Visibility Range and HD Textures, the rows over keys of their own. Without the engine.
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
        GraphicsRow.HdTextures => (s.HdTextures, s.HdTextureSize),
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
        s.HdTextures = false; s.HdTextureSize = 2;
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
        Assert.Equal([GraphicsRow.Look1997, GraphicsRow.HdTextures, GraphicsRow.Vegetation, GraphicsRow.AmbientOcclusion, GraphicsRow.HoodGloss, GraphicsRow.CarInteriors, GraphicsRow.PaletteGrade, GraphicsRow.CrashBlur, GraphicsRow.Garage3D],
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
        Assert.Equal(I76.Formats.Shell.HdPackFolders.DefaultSize, s.HdTextureSize);
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

    // ---- HD Textures ----

    /// <summary>Off or 4x: the 2x and 3x folders are no longer offered by the row (the loader still reads them).</summary>
    [Fact]
    public void HdTexturesIsOffOr4x()
    {
        Assert.Equal(["Off", "4x*"], HdTextureRow.Names([4, 3, 2], true)!);
        Assert.Equal(["Off", "4x*"], HdTextureRow.Names([4], true)!);
        // no tex-world-4x: a pack of another size, a plain tex-world or a zip is on or off
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([3], true)!);
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([3, 2], false)!);
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([], true)!);
        // no pack: no row
        Assert.Null(HdTextureRow.Names([], false));
    }

    /// <summary>A size the file or the Developer panel names shows as itself while its folder is there, until the row is clicked.</summary>
    [Fact]
    public void HdTexturesShowsAnotherSizeAsItself()
    {
        Assert.Equal(["Off", "3x*"], HdTextureRow.Names([4, 3, 2], true, 3)!);
        Assert.Equal(["Off", "4x*"], HdTextureRow.Names([4, 2], true, 3)!);   // no 3x folder: the 4x loads (HdPackFolders.Choose)
        Assert.Equal("tex-world-4x", I76.Formats.Shell.HdPackFolders.Choose(3, f => f is "tex-world-4x" or "tex-world-2x"));
        var s = new ShellSettings { HdTextureSize = 3 };
        Assert.Equal(1, HdTextureRow.Index(s, [4, 3, 2]));
        HdTextureRow.Step(s, [4, 3, 2], true);      // Off, the size kept
        Assert.False(s.HdTextures);
        Assert.Equal(3, s.HdTextureSize);
        HdTextureRow.Step(s, [4, 3, 2], true);      // on again: 4x
        Assert.True(s.HdTextures);
        Assert.Equal(4, s.HdTextureSize);
    }

    /// <summary>The loader keeps every whole multiple: the 3x and 2x folders are still found and chosen.</summary>
    [Fact]
    public void TheLoaderStillServesEverySize()
    {
        Assert.Equal([8, 4, 3, 2], I76.Formats.Shell.HdPackFolders.Sizes);
        Assert.Equal("tex-world-2x", I76.Formats.Shell.HdPackFolders.Choose(2, f => f is "tex-world-4x" or "tex-world-2x"));
        Assert.Equal("tex-world-3x", I76.Formats.Shell.HdPackFolders.Choose(4, f => f is "tex-world-3x" or "tex-world-2x"));
    }

    /// <summary>With a tex-world-8x the row is Off, 4x and 8x, the 8x saying what a partial pack covers; a click steps Off, 4x, 8x, Off.</summary>
    [Fact]
    public void HdTexturesOffers8xWhenItsFolderIsThere()
    {
        int[] have = [8, 4];
        Assert.Equal(["Off", "4x*", "8x (cockpit & cars)*"], HdTextureRow.Names(have, true, 4, "cockpit & cars")!);
        Assert.Equal(["Off", "4x*", "8x*"], HdTextureRow.Names(have, true)!);   // a full 8x pack
        Assert.Equal(["Off", "4x*"], HdTextureRow.Names([4, 2], true, 8)!);       // 8 in the file, no folder: the 4x loads
        Assert.Equal(["Off", "On*"], HdTextureRow.Names([8], true)!);             // an 8x with no 4x under it: on or off
        var s = new ShellSettings { HdTextures = true, HdTextureSize = 4 };
        Assert.Equal(1, HdTextureRow.Index(s, have));
        HdTextureRow.Step(s, have, true);
        Assert.Equal((true, 8, 2), (s.HdTextures, s.HdTextureSize, HdTextureRow.Index(s, have)));
        HdTextureRow.Step(s, have, true);
        Assert.Equal((false, 8, 0), (s.HdTextures, s.HdTextureSize, HdTextureRow.Index(s, have)));   // Off keeps the size
        HdTextureRow.Step(s, have, true);
        Assert.Equal((true, 4, 1), (s.HdTextures, s.HdTextureSize, HdTextureRow.Index(s, have)));
        HdTextureRow.Put(s, have, 2);
        Assert.Equal(8, s.HdTextureSize);
        // the 8x folder gone: an 8 in the file shows as the 4x it loads, and the row is two values again
        Assert.Equal(1, HdTextureRow.Index(s, [4]));
        Assert.Equal("tex-world-4x", I76.Formats.Shell.HdPackFolders.Choose(8, f => f is "tex-world-4x"));
    }

    /// <summary>The two keys kept as they were: Off writes hd_textures=false and keeps the size; On writes true and 4 (with its folder).</summary>
    [Fact]
    public void HdTexturesWritesItsTwoKeys()
    {
        int[] have = [4, 3, 2];
        var s = new ShellSettings { HdTextureSize = 4 };
        HdTextureRow.Put(s, have, 0);
        Assert.False(s.HdTextures);
        Assert.Equal(4, s.HdTextureSize);
        HdTextureRow.Put(s, have, 1);
        Assert.True(s.HdTextures);
        Assert.Equal(4, s.HdTextureSize);
        // with no 4x folder the size is left alone
        var p = new ShellSettings { HdTextureSize = 3, HdTextures = false };
        HdTextureRow.Step(p, [3], true);
        Assert.True(p.HdTextures);
        Assert.Equal(3, p.HdTextureSize);
        // picking what shows writes nothing
        var q = new ShellSettings { HdTextureSize = 3 };
        HdTextureRow.Put(q, have, 1);
        Assert.Equal(3, q.HdTextureSize);
    }
}
