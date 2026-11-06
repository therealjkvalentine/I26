using I76.Game.Boot;
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
        Assert.Equal(2, I76.Game.Shell.ShellSettings.DefaultGraphics[0x6]);   // a first start is High (round 5)
    }

    /// <summary>A settings file from before version 14 still at the earlier Low defaults moves to High; Off and a later file's choice are kept.</summary>
    [Theory]
    [InlineData(13, 0, 1, 1, 2)]   // both at the old defaults: High
    [InlineData(13, 1, 0, 1, 0)]   // Shadow Detail Off chosen: kept
    [InlineData(13, 1, 2, 1, 2)]
    [InlineData(14, 0, 1, 0, 1)]   // a version 14 file's Low is a choice
    public void OldLowDefaultsMoveToHigh(int version, byte terrain, byte shadow, byte terrainAfter, byte shadowAfter)
    {
        var g = (byte[])I76.Game.Shell.ShellSettings.DefaultGraphics.Clone();
        g[0x2] = terrain; g[0x6] = shadow;
        I76.Game.Shell.ShellSettings.RaiseOldDetailDefaults(g, version);
        Assert.Equal((terrainAfter, shadowAfter), (g[0x2], g[0x6]));
    }

    [Fact]
    public void TheTerrainCastsTwiceTheReach()
    {
        Assert.Equal(1200f, ShadowReach.CasterReach(600f));
        Assert.Equal(3000f, ShadowReach.CasterReach(1500f));   // what it was before the reach was the row's
    }
}
