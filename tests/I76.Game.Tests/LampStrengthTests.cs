using I76.Formats.Mission;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's lamps (beams, glares, lens glows) are full at night and turned down by day: the original lights
/// nothing from a car under a day preset, and the testers found the lamps too strong, most of all in sunlight.
/// </summary>
public class LampStrengthTests
{
    [Fact]
    public void NightPresetsGiveNoDaylightAndDayPresetsTheirIntensity()
    {
        foreach (var p in Lighting.Presets)
            Assert.Equal(p.IsDay ? p.Intensity : 0f, Daylight.Of(p.IsDay, p.Intensity));
        Assert.Equal(0.625f, Daylight.Of(Lighting.Presets[2].IsDay, Lighting.Presets[2].Intensity));
    }

    [Fact]
    public void LampsAreFullAtNightAndDownToTheirFloorInFullSun()
    {
        Assert.Equal(1f, Daylight.Scale(0f, LampStrength.HeadDayFloor));
        Assert.Equal(LampStrength.HeadDayFloor, Daylight.Scale(1f, LampStrength.HeadDayFloor), 5);
        float dusk = Daylight.Scale(0.625f, LampStrength.HeadDayFloor);
        Assert.InRange(dusk, LampStrength.HeadDayFloor, 1f);
    }

    [Fact]
    public void TheLampsAreWeakerThanBefore()
    {
        // 14 (the beam) and 1.3 (the brake glow) were the strengths the testers found too strong
        Assert.True(LampStrength.HeadEnergy < 14f);
        Assert.True(LampStrength.BrakeGlow < 1.3f);
        Assert.True(LampStrength.HeadEnergy * LampStrength.HeadDayFloor < 14f * 0.25f);
        // the rear lamps at 0.9 / 0.4 (brake and tail glow), 0.45 (the glare), 0.8 / 0.06 (their light on the ground) and
        // 0.4 / 0.2 (the glow's and the glare's share left in full sun) were found too strong: all halved, the day shares cut further
        Assert.True(LampStrength.BrakeGlow <= 0.45f && LampStrength.TailGlow <= 0.2f && LampStrength.RearGlare <= 0.225f);
        Assert.True(RearLampLight.BrakeEnergy <= 0.4f && RearLampLight.TailEnergy <= 0.03f);
        Assert.True(LampStrength.BrakeGlow * LampStrength.GlowDayFloor <= 0.9f * 0.4f / 3f);
        Assert.True(LampStrength.RearGlare * LampStrength.RearGlareDayFloor <= 0.45f * 0.2f / 3f);
        // braking still stands out from the tail lamps alone, as much as before
        Assert.True(LampStrength.BrakeGlow >= 2f * LampStrength.TailGlow);
        Assert.True(LampStrength.TailGlareShare <= 0.3f);
    }

    [Fact]
    public void TheRearLampsLightTheGroundFaintlyWithTheLightsOnAndMoreBraking()
    {
        // a night: the tail filament's glow, the brake's several times brighter, nothing with both off
        Assert.Equal(0f, RearLampLight.Energy(0f, 0f, 0f));
        Assert.Equal(RearLampLight.TailEnergy, RearLampLight.Energy(1f, 0f, 0f));
        Assert.Equal(RearLampLight.BrakeEnergy, RearLampLight.Energy(1f, 1f, 0f));
        Assert.Equal(RearLampLight.BrakeEnergy, RearLampLight.Energy(0f, 1f, 0f));
        Assert.True(RearLampLight.TailEnergy > 0f);
        Assert.True(RearLampLight.BrakeEnergy >= 3f * RearLampLight.TailEnergy);
        // a fraction of the headlamps' beam
        Assert.True(RearLampLight.BrakeEnergy < LampStrength.HeadEnergy);
        // the ramp: half-way up the brake filament is half-way between the two
        Assert.Equal((RearLampLight.TailEnergy + RearLampLight.BrakeEnergy) / 2f, RearLampLight.Energy(1f, 0.5f, 0f), 5);
    }

    [Fact]
    public void TheRearLampsLightNoGroundInFullSun()
    {
        Assert.Equal(0f, RearLampLight.Energy(1f, 1f, 1f));
        Assert.Equal(0f, RearLampLight.Energy(1f, 0f, 1f));
        float dusk = RearLampLight.Energy(1f, 1f, 0.625f);
        Assert.InRange(dusk, 0.3f * RearLampLight.BrakeEnergy, 0.4f * RearLampLight.BrakeEnergy);
    }
}
