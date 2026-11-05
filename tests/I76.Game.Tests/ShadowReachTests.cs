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
        Assert.Equal(1, I76.Game.Shell.ShellSettings.DefaultGraphics[0x6]);   // a first start is Low
    }

    [Fact]
    public void TheTerrainCastsTwiceTheReach()
    {
        Assert.Equal(1200f, ShadowReach.CasterReach(600f));
        Assert.Equal(3000f, ShadowReach.CasterReach(1500f));   // what it was before the reach was the row's
    }
}
