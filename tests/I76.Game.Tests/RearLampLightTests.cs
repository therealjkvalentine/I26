using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's rear lamps' red light on the ground behind the car (LampStrength.RearGround): faint with the lights on, brighter braking, a night's only.</summary>
public class RearLampLightTests
{
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
        // under the old brake light's energy (1, an omni light under the car), a fraction of the headlamps' beam (14)
        Assert.True(LampStrength.BrakeGroundEnergy < 1f);
        // the ramp: half-way up the brake filament is half-way between the two
        Assert.Equal((LampStrength.TailGroundEnergy + LampStrength.BrakeGroundEnergy) / 2f, LampStrength.RearGround(1f, 0.5f, 0f), 5);
    }

    [Fact]
    public void TheRearLampsLightNoGroundInFullSun()
    {
        Assert.Equal(0f, LampStrength.RearGround(1f, 1f, 1f));
        Assert.Equal(0f, LampStrength.RearGround(1f, 0f, 1f));
        // dusk (a preset's sun intensity 0.625): between
        float dusk = LampStrength.RearGround(1f, 1f, LampStrength.Daylight(true, 0.625f));
        Assert.InRange(dusk, 0.3f * LampStrength.BrakeGroundEnergy, 0.4f * LampStrength.BrakeGroundEnergy);
        Assert.Equal(0f, LampStrength.Daylight(false, 0.9f));
    }
}
