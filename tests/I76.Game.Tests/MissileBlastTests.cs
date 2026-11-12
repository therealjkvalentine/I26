using I76.Formats.Bwd2;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's blast for each launcher's impact effects (CombatView.Blast).</summary>
public class MissileBlastTests
{
    private static EffectDef Effect(string file, float splash = 2, float radius = 5) => new(file, 20, 0, splash, radius, []);

    // every rocket, missile and the howitzer: ground, car and building hits (the stock .gdf impact pairs)
    [Theory]
    [InlineData("xmslg1.xdf")]
    [InlineData("xmslc1.xdf")]
    [InlineData("xmslb1.xdf")]
    [InlineData("XMSLC1.XDF")]
    public void LauncherImpactsAreLayeredHdBlasts(string file)
    {
        var b = CombatView.Blast(Effect(file));
        Assert.NotNull(b);
        Assert.True(b!.Value.Size >= ModernFx.LayeredSize, $"{file}: size {b.Value.Size} draws one layer of the low-resolution sequence");
        Assert.Equal(1f, b.Value.Fire);
    }

    [Fact]
    public void LauncherBlastsMatchTheMines()
    {
        // the smallest of the other weapons' blasts (the mines', size 4) is the floor for a launcher's
        Assert.True(CombatView.Blast(Effect("xmslc1.xdf"))!.Value.Size >= CombatView.Blast(Effect("xmine1.xdf", 25))!.Value.Size);
    }

    [Theory]
    [InlineData("xbulc1.xdf", 0)]
    [InlineData("xbulg1.xdf", 0)]
    [InlineData("xfire1.xdf", 1)]
    public void PuffsAndFlamesKeepTheOriginalSprites(string file, float splash) => Assert.Null(CombatView.Blast(Effect(file, splash)));
}
