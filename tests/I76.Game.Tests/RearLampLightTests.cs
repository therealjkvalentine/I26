using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's rear lamps' red light on the ground behind the car (RearLampLight): faint with the lights on, brighter braking, a night's only.</summary>
public class RearLampLightTests
{
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
        // under the old brake light's energy (1, an omni light under the car), a fraction of the headlamps' beam (14)
        Assert.True(RearLampLight.BrakeEnergy < 1f);
        // the ramp: half-way up the brake filament is half-way between the two
        Assert.Equal((RearLampLight.TailEnergy + RearLampLight.BrakeEnergy) / 2f, RearLampLight.Energy(1f, 0.5f, 0f), 5);
    }

    [Fact]
    public void TheRearLampsLightNoGroundInFullSun()
    {
        Assert.Equal(0f, RearLampLight.Energy(1f, 1f, 1f));
        Assert.Equal(0f, RearLampLight.Energy(1f, 0f, 1f));
        // dusk (a preset's sun intensity 0.625): between
        float dusk = RearLampLight.Energy(1f, 1f, Daylight.Of(true, 0.625f));
        Assert.InRange(dusk, 0.3f * RearLampLight.BrakeEnergy, 0.4f * RearLampLight.BrakeEnergy);
        Assert.Equal(0f, Daylight.Of(false, 0.9f));
    }

    [Fact]
    public void ThePoolAloneLiesWhereNoMirrorSeesTheGround()
    {
        // the rear mirror: level, 45 degrees across 1024x256 (the original's 4:1); its nearest road behind an eye a metre up is
        // about 10 m back, and the eye sits 1.5 m or more ahead of the lamps. The pool (lamp 0.6 to 1.2 m up) ends within 6 m
        float mirror = RearLampLight.MirrorGround(1.0f, MirrorCameraSettings.FovDegrees, 1024, 256);
        Assert.InRange(mirror, 9f, 10.5f);
        foreach (float lamp in new[] { 0.6f, 0.9f, 1.2f })
        {
            var pool = RearLampLight.GroundReach(lamp, RearLampLight.PoolPitch, RearLampLight.PoolAngle, RearLampLight.PoolRange);
            Assert.NotNull(pool);
            Assert.True(pool!.Value.Far < 6f);
            Assert.True(pool.Value.Far + 1.5f < mirror);
        }
    }

    [Fact]
    public void TheWashLightsTheRoadTheMirrorsSee()
    {
        var w = RearLampLight.ParseWash(null);
        Assert.True(w.Share > 0f);
        // the ground alone: no car (the own one's tail, one behind) and no scenery takes it
        Assert.Equal(I76.Game.World.TerrainNode.TerrainLayer, RearLampLight.WashLayers);
        // its axis on the road a long way back, its lower edge clear of the ground under the car: the road from a few metres back
        Assert.InRange(-w.Pitch, 0.5f, 6f);
        foreach (float lamp in new[] { 0.6f, 0.9f, 1.2f })
        {
            var reach = RearLampLight.GroundReach(lamp, w.Pitch, w.Angle, w.Range);
            Assert.NotNull(reach);
            Assert.InRange(reach!.Value.Near, 1.5f, 6f);
            // and out past what the rear mirror sees from an eye 1 to 1.4 m up and 1.5 to 3 m ahead of the lamps, and from a truck's cab
            // 2.4 m up and 6 m ahead of them over its bed (its nearest road about 29 m behind the lamps)
            foreach (float eye in new[] { 1.0f, 1.4f })
                Assert.True(reach.Value.Far > RearLampLight.MirrorGround(eye, MirrorCameraSettings.FovDegrees, 1024, 256) + 5f);
            Assert.True(reach.Value.Far > RearLampLight.MirrorGround(2.4f, MirrorCameraSettings.FovDegrees, 1024, 256) - 6f + 8f);
        }
        // lit evenly along the road: a fall-off of -1 or under cancels the grazing angle (h / d^(1 + attenuation))
        Assert.True(w.Attenuation <= -1f);
    }

    [Fact]
    public void TheWashHookTakesSixNumbersOrNone()
    {
        Assert.Equal(RearLampLight.WashShare, RearLampLight.ParseWash("1,2").Share);
        var w = RearLampLight.ParseWash("0,-10,5,18,-1,0");
        Assert.Equal((0f, -10f, 5f, 18f, -1f, 0f), w);
    }
}
