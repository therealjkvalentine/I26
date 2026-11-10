using I76.Formats.Vegetation;
using I76.Game.Sim;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's plants fade in beyond the headlamps' reach (the owner, at night: bushes seen materialising in the
/// headlights): at every Vegetation level and whatever the lamps' range, each kind of bush (the shrubs, the ground layer's
/// larger plants, the rolling tumbleweeds) is wholly undrawn until past the lamps' range plus the chase view's offset.
/// </summary>
public class VegReachTests
{
    // the default throw, a longer one (a third more), and far beyond any level's own reach
    public static TheoryData<int, float> Cases => new() { { 1, LampStrength.HeadRange }, { 2, LampStrength.HeadRange }, { 1, 155f }, { 2, 155f }, { 1, 260f }, { 2, 260f } };

    [Fact]
    public void TheAppearDistanceFollowsTheLampRange()
    {
        Assert.True(VegLevels.LampMargin >= 15f);
        foreach (float r in new[] { 80f, LampStrength.HeadRange, 155f })
            Assert.Equal(r + VegLevels.LampMargin, VegLevels.BushAppear(r));
        Assert.Equal(LampStrength.HeadRange, LampStrength.Range);   // no I76_HEAD_RANGE in the test run
    }

    [Theory, MemberData(nameof(Cases))]
    public void ShrubsFadeInBeyondTheLamps(int level, float lamp)
    {
        var l = VegLevels.For(level, lamp);
        Assert.True(l.ShrubFade >= VegLevels.BushAppear(lamp), $"level {level}, lamps {lamp} m: shrubs fully drawn from {l.ShrubFade} m");
        Assert.True(l.ShrubEnd > l.ShrubFade);
    }

    [Theory, MemberData(nameof(Cases))]
    public void TheGroundLayersLargerPlantsFadeInBeyondTheLamps(int level, float lamp)
    {
        var l = VegLevels.For(level, lamp);
        Assert.True(l.ForbFade >= VegLevels.BushAppear(lamp), $"level {level}, lamps {lamp} m: forbs fully drawn from {l.ForbFade} m");
        Assert.True(l.ForbEnd > l.ForbFade);
        Assert.Equal(l.ForbEnd, l.GroundEnd);
    }

    [Theory, MemberData(nameof(Cases))]
    public void RollingTumbleweedsFadeInBeyondTheLamps(int level, float lamp)
    {
        float range = VegLevels.TumbleRange(level, lamp);
        var (start, end) = TumbleweedField.FadeIn(range);
        Assert.True(start >= VegLevels.BushAppear(lamp), $"level {level}, lamps {lamp} m: tumbleweeds fully drawn from {start} m");
        Assert.True(end > start && end < range);
    }

    [Theory, MemberData(nameof(Cases))]
    public void TheLevelsOwnReachHoldsWhereTheLampsAllow(int level, float lamp)
    {
        var own = level >= 2 ? VegLevels.High : VegLevels.Low;
        var l = VegLevels.For(level, lamp);
        Assert.True(l.ShrubFade >= own.ShrubFade && l.ShrubEnd - l.ShrubFade == own.ShrubEnd - own.ShrubFade);
        // the grass tufts keep their short reach (their cost); they grow in out of the sward instead (vegetation.gdshader grow_in)
        Assert.Equal((own.GrassFade, own.GrassEnd), (l.GrassFade, l.GrassEnd));
        Assert.True(l.GrassEnd < VegLevels.BushAppear(lamp));
    }

    [Fact]
    public void TheOldLevelsFailedTheRule()
    {
        // before: Low's shrubs fully drawn from 120 m and every ground plant from 35 m (High 60 m), with 110 m lamps: in the beam
        float appear = VegLevels.BushAppear(LampStrength.HeadRange);
        Assert.True(VegLevels.OldLow.ShrubFade < appear);
        Assert.True(VegLevels.OldLow.ForbFade < LampStrength.HeadRange && VegLevels.OldHigh.ForbFade < LampStrength.HeadRange);
        Assert.True(TumbleweedField.FadeIn(150f).Start < appear);
    }
}
