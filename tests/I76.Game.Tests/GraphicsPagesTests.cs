using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's pages (GraphicsPages): the sheet holds only the pages' entries, every row (the original's too) on exactly one of
/// Display, World, Car &amp; Cockpit and Effects, each page fitting the sheet's lines without scrolling (GraphicsLayout, on the shell's
/// sheet and the in-game pad); Reset on a page puts back its own rows and no other's, the sheet's every page's (the original's whole
/// block), and the snapshot Cancel restores from carries every row. Visibility Range, World Textures and Car &amp; Dash Textures, the
/// rows over keys of their own. Without the engine.
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
        GraphicsRow.MirrorHaze => s.MirrorHaze,
        GraphicsRow.RadarSweep => s.RadarSweep,
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
        GraphicsRow.MenuWidth => s.MenuWide,
        GraphicsRow.SmokeLighting => s.SmokeLighting,
        GraphicsRow.HeatShimmer => s.HeatShimmer,
        GraphicsRow.TerrainShadows => s.TerrainShadows,
        GraphicsRow.Look1997 => (s.Look1997, s.SoftwareRenderer),
        GraphicsRow.PaletteGrade => s.PaletteGrade,
        GraphicsRow.CrashBlur => s.CrashBlur,
        GraphicsRow.Haze => s.Haze,
        GraphicsRow.Glow => s.Glow,
        GraphicsRow.Reflections => s.Reflections,
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
        s.Vegetation = 2; s.AmbientOcclusion = true; s.Haze = 2; s.Glow = 1; s.Reflections = true;
        s.FieldOfView = I76.Sim.Views.ViewFov.Monitor + 3; s.HoodGloss = I76.Sim.Views.PaintFinish.Satin + 15; s.CabinsBehindGlass = false;
        s.MouseCamera = I76.Sim.Views.MouseLook.Normal + 1; s.KeepMouseInWindow = true; s.CameraReturn = false; s.ZoomReset = false; s.Garage3D = false; s.MenuWide = false;
        s.Look1997 = true; s.SoftwareRenderer = true; s.PaletteGrade = true; s.CrashBlur = I76.Sim.Views.CrashBlurMode.Off;
        s.SideMirrors = I76.Game.Sim.SideMirrorMode.Left; s.MirrorRate = I76.Game.Sim.MirrorRate.Full; s.MirrorHaze = 2; s.RadarSweep = 1;
        s.SmokeLighting = true; s.HeatShimmer = HeatShimmerRow.Road; s.TerrainShadows = TerrainShadowsRow.Fine;
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

    /// <summary>
    /// The original's grxdet1.map in order, without the 1997 renderer's own rows (Clouds, Horizon and Terrain Textures, Terrain
    /// Resolution, Object Detail), Rear Mirror as Mirrors. The fork's sheet has no rows: each of the original's is on its page, leading
    /// it in the original's order, but for Field of View, Car &amp; Cockpit's most used, first there.
    /// </summary>
    [Fact]
    public void TheSheetHas1997ModeAndTheOriginalsRowsLeadTheirPages()
    {
        Assert.Equal([GraphicsRow.Look1997], GraphicsPages.Rows[GraphicsPages.Sheet]);
        Assert.Equal([GraphicsRow.Resolution, GraphicsRow.MonitorBrightness, GraphicsRow.VisibilityRange, GraphicsRow.TerrainDetail, GraphicsRow.ShadowDetail, GraphicsRow.ExtInstruments, GraphicsRow.Mirrors],
            GraphicsPages.Original);
        Assert.Equal([GraphicsPages.Display, GraphicsPages.Display, GraphicsPages.World, GraphicsPages.World, GraphicsPages.World, GraphicsPages.CarCockpit, GraphicsPages.CarCockpit],
            GraphicsPages.Original.Select(GraphicsPages.PageOf));
        for (int p = GraphicsPages.FirstPage; p < GraphicsPages.Names.Length; p++)
        {
            var mine = GraphicsPages.Rows[p].Where(GraphicsPages.Original.Contains).ToArray();
            Assert.Equal(GraphicsPages.Original.Where(mine.Contains), mine);
            int lead = p == GraphicsPages.CarCockpit ? 1 : 0;
            Assert.Equal(mine, GraphicsPages.Rows[p].Skip(lead).Take(mine.Length));
        }
    }

    /// <summary>Related rows sit together: Shadow Detail over Terrain Shadows, Mirrors over Mirror Rate and Mirror Haze, Ambient Occlusion over Reflections (which needs it).</summary>
    [Theory]
    [InlineData(GraphicsRow.ShadowDetail, GraphicsRow.TerrainShadows)]
    [InlineData(GraphicsRow.Mirrors, GraphicsRow.MirrorRate)]
    [InlineData(GraphicsRow.MirrorRate, GraphicsRow.MirrorHaze)]
    [InlineData(GraphicsRow.AmbientOcclusion, GraphicsRow.Reflections)]
    [InlineData(GraphicsRow.Resolution, GraphicsRow.MonitorBrightness)]
    public void RelatedRowsAreAdjacent(GraphicsRow a, GraphicsRow b)
    {
        int p = GraphicsPages.PageOf(a);
        Assert.Equal(p, GraphicsPages.PageOf(b));
        Assert.Equal(Array.IndexOf(GraphicsPages.Rows[p], a) + 1, Array.IndexOf(GraphicsPages.Rows[p], b));
    }

    /// <summary>
    /// Four pages under the sheet, by what a row changes (one Advanced page of twenty rows until it outgrew the sheet; four pages
    /// before round 5, then Look, Display and Camera): Display (1997 Mode first), World, Car &amp; Cockpit, Effects.
    /// </summary>
    [Fact]
    public void ThePagesAreDisplayWorldCarAndEffects()
    {
        Assert.Equal(["Graphic Detail", "Display", "World", "Car & Cockpit", "Effects"], GraphicsPages.Names);
        Assert.Equal([GraphicsRow.Resolution, GraphicsRow.MonitorBrightness, GraphicsRow.AntiAliasing, GraphicsRow.RenderScale, GraphicsRow.VSync, GraphicsRow.MaxFps, GraphicsRow.ShowFps, GraphicsRow.Renderer, GraphicsRow.MenuWidth],
            GraphicsPages.Rows[GraphicsPages.Display]);
        Assert.Equal([GraphicsRow.VisibilityRange, GraphicsRow.TerrainDetail, GraphicsRow.ShadowDetail, GraphicsRow.TerrainShadows, GraphicsRow.WorldTextures, GraphicsRow.Vegetation, GraphicsRow.AmbientOcclusion, GraphicsRow.Reflections, GraphicsRow.Haze],
            GraphicsPages.Rows[GraphicsPages.World]);
        Assert.Equal([GraphicsRow.FieldOfView, GraphicsRow.ExtInstruments, GraphicsRow.Mirrors, GraphicsRow.MirrorRate, GraphicsRow.MirrorHaze, GraphicsRow.RadarSweep, GraphicsRow.CarTextures, GraphicsRow.HoodGloss, GraphicsRow.Garage3D],
            GraphicsPages.Rows[GraphicsPages.CarCockpit]);
        Assert.Equal([GraphicsRow.PaletteGrade, GraphicsRow.CrashBlur, GraphicsRow.Glow, GraphicsRow.SmokeLighting, GraphicsRow.HeatShimmer],
            GraphicsPages.Rows[GraphicsPages.Effects]);
        Assert.Equal(GraphicsPages.Display, GraphicsPages.FirstPage);
        Assert.Equal(GraphicsPages.Display, GraphicsPages.PageNamed("advanced"));
        Assert.Equal(GraphicsPages.CarCockpit, GraphicsPages.PageNamed("Car"));
        Assert.Equal(GraphicsPages.Effects, GraphicsPages.PageNamed("effects"));
        Assert.Equal(-1, GraphicsPages.PageNamed("look"));
    }

    /// <summary>
    /// The sheet: 1997 Mode on its first line (the switch between the looks), a blank line, the four entries, the frame rate on the
    /// last; the pad's the same from row 1 (1997 Mode, a blank row, the entries on rows 3 to 6), the frame rate on row 14.
    /// </summary>
    [Fact]
    public void TheSheetIs1997ModeOverTheEntries()
    {
        Assert.Equal(2, GraphicsPages.FirstEntryLine);
        foreach (bool fps in new[] { true, false })
        {
            var shell = GraphicsLayout.Shell(GraphicsPages.Sheet, 1, fps);
            Assert.Equal((0, 1, false), (shell.LineOf(0), shell.Shown, shell.Scrolls));
            Assert.Equal([2, 3, 4, 5], shell.Entries);
            Assert.Equal(fps ? 13 : -1, shell.Fps);
            var pad = GraphicsLayout.Pad(GraphicsPages.Sheet, 1, fps);
            Assert.Equal((1, 1, false), (pad.LineOf(0), pad.Shown, pad.Scrolls));
            Assert.Equal([3, 4, 5, 6], pad.Entries);
            Assert.Equal(fps ? 14 : -1, pad.Fps);
            foreach (int r in pad.Entries) Assert.DoesNotContain(r, GraphicsLayout.PadButtons);
        }
    }

    /// <summary>1997 Mode is the sheet's: Display's Reset leaves it, the sheet's (every page's) puts it back, the snapshot carries it.</summary>
    [Fact]
    public void Look1997IsTheSheetsRow()
    {
        Assert.Equal(GraphicsPages.Sheet, GraphicsPages.PageOf(GraphicsRow.Look1997));
        var s = new ShellSettings { Look1997 = true, SoftwareRenderer = true };
        GraphicsPages.Reset(s, GraphicsPages.Display);
        Assert.True(s.Look1997 && s.SoftwareRenderer);
        var snap = GraphicsPages.CopyAll(s, new ShellSettings());
        Assert.True(snap.Look1997 && snap.SoftwareRenderer);
        GraphicsPages.Reset(s, GraphicsPages.Sheet);
        Assert.False(s.Look1997 || s.SoftwareRenderer);
        var pad = new ShellSettings { Look1997 = true };
        GraphicsPages.Reset(pad, GraphicsPages.Sheet, [GraphicsRow.Look1997]);
        Assert.False(pad.Look1997);
    }

    // ---- the layout: every page at every row count it can have, on both menus ----

    /// <summary>Every row count a page can show: all its rows, and down to all but the rows shown only with a pack (and the rows the pad leaves out).</summary>
    public static IEnumerable<object[]> PageCounts()
    {
        for (int p = 0; p < GraphicsPages.Names.Length; p++)
        {
            int all = GraphicsPages.Rows[p].Length, fewest = all - GraphicsPages.Rows[p].Count(r => GraphicsPages.PackRows.Contains(r)) - 3;
            for (int n = Math.Max(1, fewest); n <= all; n++)
                foreach (bool fps in new[] { false, true }) yield return [p, n, fps];
        }
    }

    /// <summary>
    /// The shell's sheet: every row on its own ruled line inside the row area (from the sheet's first line to the line over the
    /// buttons), each line once, none on the heading, an entry's or the frame rate's line; the entries and the frame-rate line inside
    /// the area too; every row on screen without scrolling (each page fits).
    /// </summary>
    [Theory]
    [MemberData(nameof(PageCounts))]
    public void TheShellsRowsSitOnTheirOwnLinesInsideTheSheet(int page, int rows, bool fps)
    {
        var place = GraphicsLayout.Shell(page, rows, fps);
        Assert.False(place.Scrolls, $"{GraphicsPages.Names[page]} with {rows} rows scrolls");
        var used = new List<int>();
        if (page != GraphicsPages.Sheet) used.Add(GraphicsPages.HeadingLine);
        for (int k = 0; k < rows; k++) used.Add(place.LineOf(k));
        used.AddRange(place.Entries);
        if (place.Fps >= 0) used.Add(place.Fps);
        Assert.Equal(used.Count, used.Distinct().Count());
        foreach (int line in used)
        {
            Assert.InRange(line, 0, GraphicsPages.Lines - 1);
            // a line's rectangle (its tooltip zone, 2 px over the text, the pitch high) ends above the buttons' foot line
            int top = GraphicsLayout.LineY(line) - 2, bottom = top + GraphicsLayout.LineH;
            Assert.True(top >= GraphicsLayout.ListY - 2 && bottom <= GraphicsLayout.ButtonY, $"line {line} ({top}..{bottom}) leaves the row area");
        }
        // the rows at the original's pitch, from the first line, in order
        for (int k = 1; k < rows; k++) Assert.Equal(GraphicsLayout.LineH, GraphicsLayout.LineY(place.LineOf(k)) - GraphicsLayout.LineY(place.LineOf(k - 1)));
        // the entries under the rows, the frame rate last, with the blank line over it on a page
        if (place.Entries.Length > 0) Assert.True(place.Entries[0] > place.LineOf(rows - 1));
        if (place.Fps >= 0) Assert.Equal(GraphicsPages.Lines - 1, place.Fps);
        if (place.Fps >= 0 && page != GraphicsPages.Sheet) Assert.True(place.LineOf(rows - 1) < place.Fps - 1, "no blank line over the frame rate");
        Assert.Equal(fps, place.Fps >= 0);
    }

    /// <summary>
    /// The in-game pad: every row on its own pad row 1 to 14, never on a button's pair (15 to 17: the bug where Advanced's rows past
    /// the fourteenth sat over Reset, Done and Cancel), the entries and the frame-rate line on rows of their own; every row on the pad.
    /// </summary>
    [Theory]
    [MemberData(nameof(PageCounts))]
    public void ThePadsRowsSitOnTheirOwnRows(int page, int rows, bool fps)
    {
        var place = GraphicsLayout.Pad(page, rows, fps);
        Assert.False(place.Scrolls, $"{GraphicsPages.Names[page]} with {rows} rows scrolls on the pad");
        var used = new List<int>();
        for (int k = 0; k < rows; k++) used.Add(place.LineOf(k));
        used.AddRange(place.Entries);
        if (place.Fps >= 0) used.Add(place.Fps);
        Assert.Equal(used.Count, used.Distinct().Count());
        foreach (int r in used)
        {
            Assert.InRange(r, 1, GraphicsLayout.PadRows);
            Assert.DoesNotContain(r, GraphicsLayout.PadButtons);
        }
        Assert.Equal(fps, place.Fps >= 0);
    }

    /// <summary>
    /// More rows than lines (none today) scroll as one list over the lines, every row reachable: the list shows its lines' worth and
    /// scrolls through the rest, never onto the entries, the frame rate or the buttons.
    /// </summary>
    [Theory]
    [InlineData(GraphicsPages.Display, 20, true)]
    [InlineData(GraphicsPages.World, 30, false)]
    [InlineData(GraphicsPages.CarCockpit, 15, true)]
    public void RowsPastTheLinesScrollAndAllAreReachable(int page, int rows, bool fps)
    {
        foreach (var place in new[] { GraphicsLayout.Shell(page, rows, fps), GraphicsLayout.Pad(page, rows, fps) })
        {
            Assert.True(place.Scrolls);
            var list = new ScrollRows<object> { Top = place.First, Shown = place.Shown, LineH = 1 };
            var items = Enumerable.Range(0, rows).Select(_ => new object()).ToArray();
            for (int k = 0; k < rows; k++) list.Add(items[k], k);
            var seen = new HashSet<object>();
            do foreach (var (it, y, shows) in list.Layout())
                {
                    if (!shows) continue;
                    seen.Add(it);
                    Assert.InRange(y, place.First, place.First + place.Shown - 1);
                    Assert.DoesNotContain(y, place.Entries);
                    Assert.NotEqual(place.Fps, y);
                }
            while (list.ScrollBy(1));
            Assert.Equal(rows, seen.Count);
        }
    }

    /// <summary>
    /// The shell's page room with the frame-rate line (eleven rows; the pad's thirteen) against each page's rows, all packs shown, and
    /// the rows coming (a Radar Sweep row on Car &amp; Cockpit, a smoke-linger row on Effects): every page fits with a line to spare.
    /// </summary>
    [Fact]
    public void EachPageHasRoomToSpare()
    {
        int room = GraphicsLayout.Shell(GraphicsPages.Display, 99, fps: true).Shown;
        Assert.Equal(11, room);
        Assert.Equal(13, GraphicsLayout.Pad(GraphicsPages.Display, 99, fps: true).Shown);
        int[] coming = [0, 0, 0, 1, 1];
        for (int p = GraphicsPages.FirstPage; p < GraphicsPages.Names.Length; p++)
            Assert.True(room - GraphicsPages.Rows[p].Length - coming[p] >= 1, $"{GraphicsPages.Names[p]} has {GraphicsPages.Rows[p].Length} rows");
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
        Assert.Equal(TerrainShadowsRow.On, s.TerrainShadows);
        Assert.True(s.VSync);
        Assert.Equal(I76.Sim.Views.FrameLimit.Unlimited, s.MaxFps);
        Assert.Equal(I76.Sim.Views.MouseLook.Normal, s.MouseCamera);
        Assert.Equal(I76.Sim.Views.PaintFinish.Default, s.HoodGloss);
        Assert.Equal(I76.Game.Boot.RenderScale.Default, s.RenderScale);
        Assert.Equal(I76.Sim.Views.ViewFov.Monitor, s.FieldOfView);
    }

    [Theory]
    [InlineData(GraphicsPages.Sheet)]
    [InlineData(GraphicsPages.Display)]
    [InlineData(GraphicsPages.World)]
    [InlineData(GraphicsPages.CarCockpit)]
    [InlineData(GraphicsPages.Effects)]
    public void ResetPutsBackThePagesRowsAlone(int page)
    {
        var s = Changed();
        var was = Changed();
        var defaults = new ShellSettings();
        GraphicsPages.Reset(s, page);
        foreach (var r in AllRows)
            Assert.Equal(Read(r, page == GraphicsPages.Sheet || GraphicsPages.PageOf(r) == page ? defaults : was), Read(r, s));
    }

    /// <summary>
    /// The sheet's Reset is every page's: the original's (the graphics block back to the default profile, the bytes no row shows too)
    /// with every port row, Screen Resolution back to Desktop and Visibility Range back to following the look. A page's Reset leaves
    /// the bytes no row shows alone.
    /// </summary>
    [Fact]
    public void TheSheetsResetIsTheDefaultProfileAndEveryPage()
    {
        var s = Changed();
        GraphicsPages.Reset(s, GraphicsPages.Display);
        Assert.Equal(ShellSettings.DefaultGraphics[0xc], s.Graphics[0xc]);
        Assert.Equal(ShellSettings.DefaultGraphics[0x8], s.Graphics[0x8]);
        Assert.Equal(Changed().Graphics[0x0], s.Graphics[0x0]);
        Assert.Equal(Changed().Graphics[0x2], s.Graphics[0x2]);   // World's
        GraphicsPages.Reset(s, GraphicsPages.Sheet);
        Assert.Equal(ShellSettings.DefaultGraphics, s.Graphics);
        Assert.Equal("", s.Resolution);
        Assert.Null(s.DrawDistanceChoice);
        var defaults = new ShellSettings();
        foreach (var r in AllRows) Assert.Equal(Read(r, defaults), Read(r, s));
    }

    /// <summary>Every byte of the graphics block is one row's or the sheet's: Copy and Reset touch each from one page.</summary>
    [Fact]
    public void EachBlockByteHasOneOwner()
    {
        for (int i = 0; i < 16; i++)
        {
            var from = new ShellSettings();
            from.Graphics[i] ^= 0x5a;
            int owners = 0;
            for (int p = 0; p < GraphicsPages.Names.Length; p++)
            {
                var to = new ShellSettings();
                GraphicsPages.Copy(p, from, to);
                if (to.Graphics[i] == from.Graphics[i]) owners++;
            }
            Assert.True(owners == 1, $"byte {i:x} copied by {owners} pages");
        }
    }

    /// <summary>The in-game pad's sheet Reset is every pad's rows and the bytes no row shows, not the window, the renderer, the 3D garage or the menus' width (the pad does not show them; its Cancel could not put them back).</summary>
    [Fact]
    public void ThePadsSheetResetLeavesTheShellsRowsAlone()
    {
        var s = Changed();
        GraphicsRow[] shellOnly = [GraphicsRow.Resolution, GraphicsRow.Renderer, GraphicsRow.Garage3D, GraphicsRow.MenuWidth];
        var offered = AllRows.Where(r => !shellOnly.Contains(r)).ToArray();
        GraphicsPages.Reset(s, GraphicsPages.Sheet, offered);
        Assert.Equal("1280x720", s.Resolution);
        Assert.Equal(I76.Game.Boot.RendererChoice.OpenGL, s.Renderer);
        Assert.Null(s.DrawDistanceChoice);
        Assert.Equal(ShellSettings.DefaultGraphics, s.Graphics);
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
        GraphicsPages.Copy(GraphicsPages.Effects, Changed(), to);
        var defaults = new ShellSettings();
        foreach (var r in AllRows)
            Assert.Equal(Read(r, GraphicsPages.PageOf(r) == GraphicsPages.Effects ? Changed() : defaults), Read(r, to));
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

    /// <summary>Its own row on World, Off for a first start; World's Reset puts it back and the snapshot (Cancel) carries it.</summary>
    [Fact]
    public void AmbientOcclusionIsALookRowOffByDefault()
    {
        Assert.Equal(GraphicsPages.World, GraphicsPages.PageOf(GraphicsRow.AmbientOcclusion));
        Assert.False(new ShellSettings().AmbientOcclusion);
        var s = new ShellSettings { AmbientOcclusion = true };
        var snap = GraphicsPages.CopyAll(s, new ShellSettings());
        Assert.True(snap.AmbientOcclusion);
        GraphicsPages.Reset(s, GraphicsPages.World);
        Assert.False(s.AmbientOcclusion);
        GraphicsPages.CopyAll(snap, s);
        Assert.True(s.AmbientOcclusion);
        // Display's Reset and Shadow Detail's leave it alone: it is no longer Shadow Detail High's
        GraphicsPages.Reset(s, GraphicsPages.Display);
        Assert.True(s.AmbientOcclusion);
        GraphicsPages.Reset(s, GraphicsRow.ShadowDetail);
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
