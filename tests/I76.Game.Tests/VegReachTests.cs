using I76.Formats.Vegetation;
using I76.Game.Sim;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's plants fade in beyond the headlamps' reach (the owner, at night: bushes seen materialising in the
/// headlights): at every Vegetation level, each kind of bush (the shrubs, the ground layer's larger plants, the rolling
/// tumbleweeds) is wholly undrawn until past the lamps' range plus the chase view's offset.
/// </summary>
public class VegReachTests
{
    public static TheoryData<int> Levels => new() { 1, 2 };

    private static VegLevels.Level Level(int level) => level >= 2 ? VegLevels.High : VegLevels.Low;

    [Fact]
    public void TheAppearDistanceIsTheLampsReachPlusTheMargin()
    {
        Assert.Equal(110f, LampStrength.HeadRange);
        Assert.True(VegLevels.LampMargin >= 15f);
        Assert.Equal(LampStrength.HeadRange + VegLevels.LampMargin, VegLevels.BushAppear);
    }

    [Theory, MemberData(nameof(Levels))]
    public void ShrubsFadeInBeyondTheLamps(int level)
    {
        var l = Level(level);
        Assert.True(l.ShrubFade >= VegLevels.BushAppear, $"level {level}: shrubs fully drawn from {l.ShrubFade} m");
        Assert.True(l.ShrubEnd > l.ShrubFade);
    }

    [Theory, MemberData(nameof(Levels))]
    public void TheGroundLayersLargerPlantsFadeInBeyondTheLamps(int level)
    {
        var l = Level(level);
        Assert.True(l.ForbFade >= VegLevels.BushAppear, $"level {level}: forbs fully drawn from {l.ForbFade} m");
        Assert.True(l.ForbEnd > l.ForbFade);
        // the ground layer's chunks are wanted out to its farther kind
        Assert.Equal(l.ForbEnd, l.GroundEnd);
    }

    [Theory, MemberData(nameof(Levels))]
    public void RollingTumbleweedsFadeInBeyondTheLamps(int level)
    {
        var (start, end) = TumbleweedField.FadeIn(TumbleweedField.RangeFor(level));
        Assert.True(start >= VegLevels.BushAppear, $"level {level}: tumbleweeds fully drawn from {start} m");
        Assert.True(end > start && end < TumbleweedField.RangeFor(level));
    }

    [Theory, MemberData(nameof(Levels))]
    public void TheGrassTuftsKeepTheirShortReach(int level)
    {
        // the tufts stay near (their cost); they grow in out of the sward instead (vegetation.gdshader grow_in)
        var l = Level(level);
        Assert.True(l.GrassEnd < VegLevels.BushAppear && l.GrassEnd > l.GrassFade);
        Assert.Equal(level >= 2 ? (60f, 85f) : (35f, 50f), (l.GrassFade, l.GrassEnd));
    }

    [Fact]
    public void TheOldLevelsFailedTheRule()
    {
        // before: Low's shrubs fully drawn from 120 m and every ground plant from 35 m (High 60 m): in the beam
        Assert.True(VegLevels.OldLow.ShrubFade < VegLevels.BushAppear);
        Assert.True(VegLevels.OldLow.ForbFade < LampStrength.HeadRange && VegLevels.OldHigh.ForbFade < LampStrength.HeadRange);
        Assert.True(TumbleweedField.FadeIn(150f).Start < VegLevels.BushAppear);
    }
}
