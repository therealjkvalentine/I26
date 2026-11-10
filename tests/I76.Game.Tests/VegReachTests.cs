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
    // every headlamp_throw step a player can set, at Low and High: the lamps' visible reach at each
    public static TheoryData<int, float> Cases
    {
        get
        {
            var d = new TheoryData<int, float>();
            for (int p = HeadlampThrow.Min; p <= HeadlampThrow.Max; p += 5)
                foreach (int level in new[] { 1, 2 }) d.Add(level, HeadlampThrow.VisibleReach(p));
            return d;
        }
    }

    [Fact]
    public void TheVisibleReachLiesInsideTheRangeAndGrowsWithTheThrow()
    {
        float last = 0;
        for (int p = HeadlampThrow.Min; p <= HeadlampThrow.Max; p += 5)
        {
            float v = HeadlampThrow.VisibleReach(p);
            Assert.True(v > last && v < HeadlampThrow.Range(p), $"throw {p}: visible to {v} m of {HeadlampThrow.Range(p)}");
            // at the reach the light is the visible share of the light just ahead of the hood
            float near = HeadlampThrow.Falloff(HeadlampThrow.Near, HeadlampThrow.TodayRange, HeadlampThrow.TodayAttenuation);
            Assert.Equal(HeadlampThrow.VisibleShare, HeadlampThrow.EnergyScale(p) * HeadlampThrow.Falloff(v, HeadlampThrow.Range(p), HeadlampThrow.Attenuation(p)) / near, 3);
            last = v;
        }
        // the beams as they were: lit visibly to about 90 m of 110; the default: about 126 m of 147 (the road faintly lit there)
        Assert.InRange(HeadlampThrow.VisibleReach(HeadlampThrow.TodayPercent), 85f, 95f);
        Assert.InRange(HeadlampThrow.VisibleReach(HeadlampThrow.Default), 120f, 132f);
    }

    [Fact]
    public void TheAppearDistanceIsTheReachPlusTheMargin()
    {
        Assert.True(VegLevels.LampMargin >= 15f);
        foreach (float r in new[] { 80f, 126f, 201f })
            Assert.Equal(r + VegLevels.LampMargin, VegLevels.BushAppear(r));
        Assert.Equal(HeadlampThrow.VisibleReach(HeadlampThrow.Current), VegLevels.LampReachNow);
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
        Assert.True(l.ShrubFade >= own.ShrubFade && l.ForbFade >= own.ForbFade);
        Assert.Equal(own.ShrubEnd - own.ShrubFade, l.ShrubEnd - l.ShrubFade, 3);
        // the grass tufts keep their short reach (their cost); they grow in out of the sward instead (vegetation.gdshader grow_in)
        Assert.Equal((own.GrassFade, own.GrassEnd), (l.GrassFade, l.GrassEnd));
    }

    [Fact]
    public void TheOldLevelsFailedTheRule()
    {
        // before: every ground plant fully drawn from 35 m on Low (60 m on High), inside even the old beam's visible reach (90 m);
        // Low's shrubs from 120 m, inside the default beam's (126 m) plus the margin
        float old = HeadlampThrow.VisibleReach(HeadlampThrow.TodayPercent), now = HeadlampThrow.VisibleReach(HeadlampThrow.Default);
        Assert.True(VegLevels.OldLow.ForbFade < old && VegLevels.OldHigh.ForbFade < old);
        Assert.True(VegLevels.OldLow.ShrubFade < VegLevels.BushAppear(now));
        Assert.True(TumbleweedField.FadeIn(150f).Start < VegLevels.BushAppear(now));
    }
}
