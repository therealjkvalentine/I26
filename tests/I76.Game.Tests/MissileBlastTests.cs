using I76.Formats.Bwd2;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's blast for each launcher's impact effects (CombatView.Blast, CombatView.StyleOf).</summary>
public class MissileBlastTests
{
    private static EffectDef Effect(string file, float splash = 2, float radius = 5) => new(file, 20, 0, splash, radius, []);

    // every rocket, missile and the howitzer: ground, car and building hits (the stock .gdf impact pairs)
    public static TheoryData<string> Launchers => new() { "xmslg1.xdf", "xmslc1.xdf", "xmslb1.xdf", "XMSLC1.XDF" };

    [Theory]
    [MemberData(nameof(Launchers))]
    public void LauncherImpactsAreLayeredHdBlasts(string file)
    {
        var b = CombatView.Blast(Effect(file));
        Assert.NotNull(b);
        Assert.Equal(1f, b!.Value.Fire);
        var st = CombatView.StyleOf(Effect(file));
        Assert.NotNull(st);
        // the HD sequence in layers, whatever the size (one layer of the low-resolution sequence was the small puffball)
        Assert.True(st!.Value.Layered);
    }

    [Theory]
    [MemberData(nameof(Launchers))]
    public void LauncherBlastsSitOnTheHitWithoutTheWhiteDisc(string file)
    {
        var st = CombatView.StyleOf(Effect(file))!.Value;
        // the HD sequence's first four frames are a white disc (half of the ball or more near-white): started past them
        Assert.True(st.FirstFrame >= 4);
        // its fireball sits about 0.25 sizes under the frame's centre: the centre no higher than that puts the ball on the hit,
        // and the frame hardly turned keeps it there (turned at random it stood up to a size above the hit)
        Assert.InRange(st.Lift, 0f, 0.3f);
        Assert.InRange(st.Spin, 0f, 0.5f);
        Assert.True(st.Flash < 1f && st.Bright < 1f);
    }

    [Theory]
    [InlineData("xcarx1.xdf", 25)]
    [InlineData("xgren1.xdf", 95)]
    [InlineData("xbldx1.xdf", 15)]
    public void OtherBlastsKeepTheirLayout(string file, float splash) => Assert.Null(CombatView.StyleOf(Effect(file, splash)));

    [Theory]
    [InlineData("xbulc1.xdf", 0)]
    [InlineData("xbulg1.xdf", 0)]
    [InlineData("xfire1.xdf", 1)]
    public void PuffsAndFlamesKeepTheOriginalSprites(string file, float splash) => Assert.Null(CombatView.Blast(Effect(file, splash)));
}
