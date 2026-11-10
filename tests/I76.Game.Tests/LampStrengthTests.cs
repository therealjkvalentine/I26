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
            Assert.Equal(p.IsDay ? p.Intensity : 0f, LampStrength.Daylight(p.IsDay, p.Intensity));
        Assert.Equal(0.625f, LampStrength.Daylight(Lighting.Presets[2].IsDay, Lighting.Presets[2].Intensity));
    }

    [Fact]
    public void LampsAreFullAtNightAndDownToTheirFloorInFullSun()
    {
        Assert.Equal(1f, LampStrength.Scale(0f, LampStrength.HeadDayFloor));
        Assert.Equal(LampStrength.HeadDayFloor, LampStrength.Scale(1f, LampStrength.HeadDayFloor), 5);
        float dusk = LampStrength.Scale(0.625f, LampStrength.HeadDayFloor);
        Assert.InRange(dusk, LampStrength.HeadDayFloor, 1f);
    }

    [Fact]
    public void TheLampsAreWeakerThanBefore()
    {
        // 14 (the beam) and 1.3 (the brake glow) were the strengths the testers found too strong
        Assert.True(LampStrength.HeadEnergy < 14f);
        Assert.True(LampStrength.BrakeGlow < 1.3f);
        Assert.True(LampStrength.HeadEnergy * LampStrength.HeadDayFloor < 14f * 0.25f);
    }

    [Fact]
    public void TheRearLampsLightTheGroundFaintlyWithTheLightsOnAndMoreBraking()
    {
        // a night: the tail filament's glow, the brake's several times brighter, nothing with both off
        Assert.Equal(0f, LampStrength.RearGround(0f, 0f, 0f));
        Assert.Equal(LampStrength.TailGroundEnergy, LampStrength.RearGround(1f, 0f, 0f));
        Assert.Equal(LampStrength.BrakeGroundEnergy, LampStrength.RearGround(1f, 1f, 0f));
        Assert.Equal(LampStrength.BrakeGroundEnergy, LampStrength.RearGround(0f, 1f, 0f));
        Assert.True(LampStrength.TailGroundEnergy > 0f);
        Assert.True(LampStrength.BrakeGroundEnergy >= 3f * LampStrength.TailGroundEnergy);
        // a fraction of the headlamps' beam
        Assert.True(LampStrength.BrakeGroundEnergy < LampStrength.HeadEnergy);
        // the ramp: half-way up the brake filament is half-way between the two
        Assert.Equal((LampStrength.TailGroundEnergy + LampStrength.BrakeGroundEnergy) / 2f, LampStrength.RearGround(1f, 0.5f, 0f), 5);
    }

    [Fact]
    public void TheRearLampsLightNoGroundInFullSun()
    {
        Assert.Equal(0f, LampStrength.RearGround(1f, 1f, 1f));
        Assert.Equal(0f, LampStrength.RearGround(1f, 0f, 1f));
        float dusk = LampStrength.RearGround(1f, 1f, 0.625f);
        Assert.InRange(dusk, 0.3f * LampStrength.BrakeGroundEnergy, 0.4f * LampStrength.BrakeGroundEnergy);
    }
}
