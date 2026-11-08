using I76.Game.Boot;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Shadow Detail's sun shadows (ShadowReach, docs/perf/plan.md item 4): Low two splits to 600 m, High four to 1500 m; the terrain casts twice the reach.</summary>
public class ShadowReachTests
{
    [Fact]
    public void LowIsTwoSplitsTo600mAndHighFourTo1500m()
    {
        Assert.Equal((2, 600f), ShadowReach.For(1));
        Assert.Equal((4, 1500f), ShadowReach.For(2));
        Assert.Equal((2, 600f), ShadowReach.For(0));   // Off draws none; its numbers are Low's
    }

    /// <summary>A first start is Low (High in round 5), on the block, a fresh settings object and the sheet's and the row's Reset.</summary>
    [Fact]
    public void AFreshInstallAndResetAreLow()
    {
        Assert.Equal(1, ShellSettings.DefaultGraphics[0x6]);
        Assert.Equal(1, new ShellSettings().Graphics[0x6]);
        var s = new ShellSettings();
        s.Graphics[0x6] = 2;
        GraphicsPages.Reset(s, GraphicsPages.Sheet);
        Assert.Equal(1, s.Graphics[0x6]);
        s.Graphics[0x6] = 0;
        GraphicsPages.Reset(s, GraphicsRow.ShadowDetail);
        Assert.Equal(1, s.Graphics[0x6]);
    }

    /// <summary>
    /// A settings file's Shadow Detail is kept at every version: Off, Low and High as saved (version 14 once raised an older file's Low
    /// to High, and a version 14 file's High, the round-5 default, cannot be told from a choice, so it stays). Terrain Detail still at
    /// a pre-14 file's Low default moves to High; a later file's Low is a choice.
    /// </summary>
    [Theory]
    [InlineData(1, 0, 1, 1, 1)]
    [InlineData(13, 0, 1, 1, 1)]   // both at the old Low defaults: Terrain High, Shadow stays Low
    [InlineData(13, 0, 2, 1, 2)]   // Shadow High chosen in an old file: kept
    [InlineData(13, 1, 0, 1, 0)]   // Shadow Off: kept
    [InlineData(14, 0, 1, 0, 1)]   // a version 14 file's Lows are choices
    [InlineData(14, 1, 2, 1, 2)]   // a version 14 file's High (round 5's default or a choice): kept
    public void AFilesShadowDetailIsKept(int version, byte terrain, byte shadow, byte terrainAfter, byte shadowAfter)
    {
        var file = (byte[])ShellSettings.DefaultGraphics.Clone();
        file[0x2] = terrain; file[0x6] = shadow;
        var g = new ShellSettings().Graphics;
        ShellSettings.ReadGraphics(g, file, version);
        Assert.Equal((terrainAfter, shadowAfter), (g[0x2], g[0x6]));
    }

    /// <summary>A block saved at the current version reads back as it was, each Shadow Detail value with every other byte.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheCurrentVersionRoundTrips(byte shadow)
    {
        var saved = new ShellSettings();
        for (int i = 0; i < 16; i++) saved.Graphics[i] = (byte)(ShellSettings.DefaultGraphics[i] ^ 1);
        saved.Graphics[0x6] = shadow;
        var g = new ShellSettings().Graphics;
        ShellSettings.ReadGraphics(g, (byte[])saved.Graphics.Clone(), ShellSettings.Version);
        Assert.Equal(saved.Graphics, g);
    }

    [Fact]
    public void TheTerrainCastsTwiceTheReach()
    {
        Assert.Equal(1200f, ShadowReach.CasterReach(600f));
        Assert.Equal(3000f, ShadowReach.CasterReach(1500f));   // what it was before the reach was the row's
    }
}
