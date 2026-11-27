using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The lamps' own light (lens glows, glares, beams, the rear lamps' light on the ground) is set on the screen, not in the scene:
/// the calibration's exposure divided out, so a lamp by day shows alike in every mission and under every Lighting choice, as the
/// 1997 look's lit lamp faces do, and nights (exposure 1 under every choice) are unchanged. Exposures as the game logged them.
/// </summary>
public class LampExposureTests
{
    // by day: Lighting 1997 (the default) at T10's dusk, M01, T13 and T07's dawn; 1997 Sunlit at T10 and T13; Off and Filmic at M01
    private static readonly float[] DayExposures = [0.160f, 0.601f, 0.653f, 0.992f, 0.102f, 0.300f, 1f, 0.795f];

    private static string Repo(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, .. path]));
    }

    [Fact]
    public void AtNightNothingChanges()
    {
        // every Lighting choice's night: exposure 1, no daylight
        Assert.Equal(1f, LampStrength.GainFor(1f, 0f), 5);
    }

    [Fact]
    public void ByDayALampShowsOnTheScreenAsAtTheReferenceExposure()
    {
        // on the screen the lamp's light is its scene strength times the exposure: the gain makes that the reference's in every case
        foreach (float e in DayExposures)
            Assert.Equal(LampStrength.ReferenceExposure, LampStrength.GainFor(e, 1f) * e, 4);
        // the default at the reference exposure (the median day mission) is as it was
        Assert.Equal(1f, LampStrength.GainFor(LampStrength.ReferenceExposure, 1f), 5);
    }

    [Fact]
    public void TheCalibrationOffNoLongerShowsTheLampsBrighterByDay()
    {
        // Lighting Off (exposure 1): the lamps were at 1/0.6 of the default's on the screen; now at the default's
        Assert.Equal(LampStrength.ReferenceExposure, LampStrength.GainFor(1f, 1f), 5);
        // and between Off, the default and Sunlit at one mission (T13: 1, 0.653, 0.300) the screen strength no longer differs
        float off = LampStrength.GainFor(1f, 1f) * 1f, card = LampStrength.GainFor(0.653f, 1f) * 0.653f, sunlit = LampStrength.GainFor(0.3f, 1f) * 0.3f;
        Assert.Equal(off, card, 4);
        Assert.Equal(off, sunlit, 4);
    }

    [Fact]
    public void BetweenDayAndNightItFollowsTheDaylight()
    {
        // a dawn preset (intensity 0.625) at exposure 1: between the night's 1 and the day's reference
        float g = LampStrength.GainFor(1f, 0.625f);
        Assert.InRange(g, LampStrength.ReferenceExposure, 1f);
        Assert.True(LampStrength.GainFor(1f, 0.3f) > LampStrength.GainFor(1f, 0.7f));
    }

    [Fact]
    public void ADegenerateExposureLeavesTheLampsAlone()
    {
        Assert.Equal(1f, LampStrength.GainFor(0f, 1f));
        Assert.Equal(1f, LampStrength.GainFor(float.NaN, 1f));
        Assert.Equal(1f, LampStrength.GainFor(-1f, 0f));
        Assert.InRange(LampStrength.GainFor(1e-4f, 1f), 0.1f, 10f);
    }

    [Fact]
    public void TheLampShadersReadTheGain()
    {
        Assert.Contains("\ni76_lamp_gain={", Repo("game", "project.godot"));
        var glare = Repo("game", "Shaders", "headlight_glare.gdshader");
        Assert.Contains("global uniform float i76_lamp_gain", glare);
        Assert.Contains("* i76_lamp_gain;", glare);
        var car = Repo("game", "Scripts", "Sim", "CarNode.cs");
        Assert.Contains("global uniform float i76_lamp_gain", car);
        Assert.Contains("* i76_lamp_gain;", car);
        // the beams and the rear lamps' light on the ground take it in the C# (light energies), the 1997 look's at 1
        Assert.Contains("float gain = Modern ? LampStrength.Gain : 1f;", car);
    }
}
