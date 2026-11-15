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
