using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's pages (GraphicsPages): the sheet keeps the original's rows in the original's order, the port's most used under
/// them, and every other port row on Advanced, each page fitting the sheet's lines with one to spare; Reset puts back a page's own
/// rows and no other's (the sheet's the original's whole block), and the snapshot Cancel restores from carries every row. Visibility
/// Range, World Textures and Car &amp; Dash Textures, the rows over keys of their own. Without the engine.
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
        GraphicsRow.Mirrors => (s.Graphics[0xd], s.SideMirrors),
        GraphicsRow.MirrorRate => s.MirrorRate,
        GraphicsRow.AntiAliasing => s.Graphics[0x8],
        GraphicsRow.VSync => s.VSync,
        GraphicsRow.MaxFps => s.MaxFps,
        GraphicsRow.RenderScale => s.RenderScale,
        GraphicsRow.Renderer => s.Renderer,
        GraphicsRow.ShowFps => s.DevFps,
        GraphicsRow.WorldTextures => s.WorldTextures,
        GraphicsRow.CarTextures => s.CarTextures,
        GraphicsRow.Vegetation => s.Vegetation,
        GraphicsRow.AmbientOcclusion => s.AmbientOcclusion,
        GraphicsRow.FieldOfView => s.FieldOfView,
        GraphicsRow.HoodGloss => s.HoodGloss,
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
        s.WorldTextures = 2; s.CarTextures = 3;
        s.Vegetation = 2; s.AmbientOcclusion = true;
        s.FieldOfView = I76.Sim.Views.ViewFov.Monitor + 3; s.HoodGloss = I76.Sim.Views.PaintFinish.Satin + 15; s.CabinsBehindGlass = false;
        s.MouseCamera = I76.Sim.Views.MouseLook.Normal + 1; s.KeepMouseInWindow = true; s.CameraReturn = false; s.ZoomReset = false; s.Garage3D = false;
        s.Look1997 = true; s.SoftwareRenderer = true; s.PaletteGrade = true; s.CrashBlur = I76.Sim.Views.CrashBlurMode.Off;
        s.SideMirrors = I76.Game.Sim.SideMirrorMode.Left; s.MirrorRate = I76.Game.Sim.MirrorRate.Full;
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

    /// <summary>The original's grxdet1.map in order, without the 1997 renderer's own rows (Clouds, Horizon and Terrain Textures, Terrain Resolution, Object Detail), Rear Mirror as Mirrors, then the port's most used.</summary>
    [Fact]
    public void TheSheetKeepsTheOriginalsRowsInTheirOrder() => Assert.Equal(
        [GraphicsRow.Resolution, GraphicsRow.MonitorBrightness, GraphicsRow.VisibilityRange, GraphicsRow.TerrainDetail, GraphicsRow.ShadowDetail, GraphicsRow.ExtInstruments, GraphicsRow.Mirrors,
         GraphicsRow.AntiAliasing, GraphicsRow.FieldOfView, GraphicsRow.VSync, GraphicsRow.MaxFps],
        GraphicsPages.Rows[GraphicsPages.Sheet]);

    /// <summary>One page under the sheet, Advanced (Look and Display until round 5): 1997 Mode first, its palette grade after it.</summary>
    [Fact]
    public void ThePageIsAdvanced()
    {
        Assert.Equal(["Graphic Detail", "Advanced"], GraphicsPages.Names);
        Assert.Equal([GraphicsRow.Look1997, GraphicsRow.PaletteGrade, GraphicsRow.CrashBlur, GraphicsRow.WorldTextures, GraphicsRow.CarTextures, GraphicsRow.Vegetation,
            GraphicsRow.AmbientOcclusion, GraphicsRow.MirrorRate, GraphicsRow.HoodGloss, GraphicsRow.RenderScale, GraphicsRow.ShowFps, GraphicsRow.Renderer, GraphicsRow.Garage3D],
            GraphicsPages.Rows[GraphicsPages.Advanced]);
    }

    /// <summary>Each page fits the sheet's fourteen lines with at least one to spare (the frame-rate estimate's): the sheet with Advanced's entry under its eleven rows and a blank line, Advanced with its name.</summary>
    [Fact]
    public void EveryPageFitsTheSheetsLinesWithOneToSpare()
    {
        Assert.Equal(11, GraphicsPages.Rows[GraphicsPages.Sheet].Length);
        Assert.Equal(GraphicsPages.Rows[GraphicsPages.Sheet].Length + 1, GraphicsPages.FirstEntryLine);
        int sheetLast = GraphicsPages.FirstEntryLine + (GraphicsPages.Rows.Length - 1) - 1;
        Assert.True(sheetLast < GraphicsPages.Lines - 1, "the sheet has no line to spare");
        // Advanced fills the sheet's lines with the texture rows shown (Mirror Rate's line): the shell's page leaves out the frame-rate
        // line when its rows reach it, and shows it without a pack
        for (int p = 1; p < GraphicsPages.Rows.Length; p++)
            Assert.True(GraphicsPages.LinesOf(p)[^1] < GraphicsPages.Lines, $"{GraphicsPages.Names[p]} does not fit");
        Assert.Equal(Enumerable.Range(1, 13), GraphicsPages.LinesOf(GraphicsPages.Advanced));
    }

    [Fact]
    public void AFreshBlockHasTheDefaultsResetPutsBack()
    {
        var s = new ShellSettings();
        Assert.Equal(ShellSettings.DefaultGraphics, s.Graphics);
        Assert.Equal("", s.Resolution);
        Assert.Null(s.DrawDistanceChoice);
        Assert.True(s.HdTextures);
        Assert.Equal(ShellSettings.HdDefault, (s.WorldTextures, s.CarTextures));
        // Terrain Detail starts High (round 5), Shadow Detail Low (High in round 5)
        Assert.Equal(1, s.Graphics[0x2]);
        Assert.Equal(1, s.Graphics[0x6]);
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
    [InlineData(GraphicsPages.Advanced)]
    public void ResetPutsBackThePagesRowsAlone(int page)
    {
        var s = Changed();
        var was = Changed();
        var defaults = new ShellSettings();
        GraphicsPages.Reset(s, page);
        foreach (var r in AllRows)
            Assert.Equal(Read(r, GraphicsPages.PageOf(r) == page ? defaults : was), Read(r, s));
    }

    /// <summary>The sheet's Reset is the original's (the graphics block back to the default profile, the bytes no row shows too, Anti-Aliasing's with them) with its port rows: Screen Resolution back to Desktop and Visibility Range back to following the look.</summary>
    [Fact]
    public void TheSheetsResetIsTheDefaultProfile()
    {
        var s = Changed();
        GraphicsPages.Reset(s, GraphicsPages.Sheet);
        Assert.Equal(ShellSettings.DefaultGraphics, s.Graphics);
        Assert.Equal("", s.Resolution);
        Assert.Null(s.DrawDistanceChoice);
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
        Assert.Equal(ShellSettings.DefaultGraphics[0x8], s.Graphics[0x8]);
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
        GraphicsPages.Copy(GraphicsPages.Advanced, Changed(), to);
        var defaults = new ShellSettings();
        foreach (var r in AllRows)
            Assert.Equal(Read(r, GraphicsPages.PageOf(r) == GraphicsPages.Advanced ? Changed() : defaults), Read(r, to));
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

    /// <summary>Its own row on Advanced, Off for a first start; Advanced's Reset puts it back and the snapshot (Cancel) carries it.</summary>
    [Fact]
    public void AmbientOcclusionIsALookRowOffByDefault()
    {
        Assert.Equal(GraphicsPages.Advanced, GraphicsPages.PageOf(GraphicsRow.AmbientOcclusion));
        Assert.False(new ShellSettings().AmbientOcclusion);
        var s = new ShellSettings { AmbientOcclusion = true };
        var snap = GraphicsPages.CopyAll(s, new ShellSettings());
        Assert.True(snap.AmbientOcclusion);
        GraphicsPages.Reset(s, GraphicsPages.Advanced);
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

    // ---- World Textures, Car & Dash Textures ----

    /// <summary>World Textures is 1997 or 4x*; Car &amp; Dash Textures the 8x* too when its folder is beside the 4x; 1997, the install's own, is the original's and unmarked. No pack, no rows.</summary>
    [Fact]
    public void TheTextureRowsOfferTheirSizes()
    {
        var s = new ShellSettings { WorldTextures = 4, CarTextures = 8 };
        Assert.Equal(["1997", "4x*"], TextureRow.Names(s, GraphicsRow.WorldTextures, [8, 4]));
        Assert.Equal(["1997", "4x*", "8x*"], TextureRow.Names(s, GraphicsRow.CarTextures, [8, 4]));
        Assert.Equal("World Textures", TextureRow.Label(GraphicsRow.WorldTextures));
        Assert.Equal("Car & Dash Textures", TextureRow.Label(GraphicsRow.CarTextures));
        Assert.True(TextureRow.Offered([4], false));
        Assert.True(TextureRow.Offered([], true));
        Assert.False(TextureRow.Offered([], false));
        // no 8x folder: the 8 in the file shows as itself in the 4x's place until stepped (the loader serves the 4x)
        Assert.Equal(["1997", "8x*"], TextureRow.Names(s, GraphicsRow.CarTextures, [4]));
        Assert.Equal(1, TextureRow.Index(s, GraphicsRow.CarTextures, [4]));
    }

    /// <summary>Each row steps its own key: 1997, 4x, (8x,) 1997 again; a size set by hand stays until the row is stepped.</summary>
    [Fact]
    public void TheTextureRowsStepTheirOwnKey()
    {
        int[] both = [8, 4];
        var s = new ShellSettings { WorldTextures = 4, CarTextures = 4 };
        TextureRow.Step(s, GraphicsRow.CarTextures, both);
        Assert.Equal((4, 8), (s.WorldTextures, s.CarTextures));
        TextureRow.Step(s, GraphicsRow.CarTextures, both);
        Assert.Equal((4, 0), (s.WorldTextures, s.CarTextures));
        Assert.True(s.HdTextures);
        TextureRow.Step(s, GraphicsRow.WorldTextures, both);
        Assert.Equal((0, 0), (s.WorldTextures, s.CarTextures));
        Assert.False(s.HdTextures);
        TextureRow.Step(s, GraphicsRow.WorldTextures, both);
        Assert.Equal((4, 0), (s.WorldTextures, s.CarTextures));
        var p = new ShellSettings { WorldTextures = 3, CarTextures = 2 };
        Assert.Equal(["1997", "3x*"], TextureRow.Names(p, GraphicsRow.WorldTextures, both));
        TextureRow.Put(p, GraphicsRow.WorldTextures, both, 1);
        Assert.Equal(3, p.WorldTextures);
        TextureRow.Step(p, GraphicsRow.CarTextures, both);
        Assert.Equal(8, p.CarTextures);
    }

    /// <summary>The loader reads each kind from its row's size: the world's from World Textures, a car's and the cockpit's from Car &amp; Dash Textures.</summary>
    [Fact]
    public void TheChainIsPerKind()
    {
        Func<string, bool> has = f => f is "tex-world-8x" or "tex-world-4x";
        var k = I76.Formats.Shell.TextureKind.World;
        Assert.Equal(["tex-world-4x"], I76.Formats.Shell.HdPackFolders.ChainFor(k, 4, 8, has));
        Assert.Equal(["tex-world-8x", "tex-world-4x"], I76.Formats.Shell.HdPackFolders.ChainFor(I76.Formats.Shell.TextureKind.Car, 4, 8, has));
        Assert.Equal(["tex-world-8x", "tex-world-4x"], I76.Formats.Shell.HdPackFolders.ChainFor(I76.Formats.Shell.TextureKind.Dash, 4, 8, has));
        Assert.Empty(I76.Formats.Shell.HdPackFolders.ChainFor(k, 0, 8, has));
        // an 8 without its folder loads the 4x
        Assert.Equal(["tex-world-4x"], I76.Formats.Shell.HdPackFolders.ChainFor(I76.Formats.Shell.TextureKind.Dash, 4, 8, f => f is "tex-world-4x"));
    }
}
