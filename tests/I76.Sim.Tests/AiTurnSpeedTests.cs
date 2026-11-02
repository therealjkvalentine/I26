using I76.Formats.Vfs;
using I76.Sim.Damage;
using I76.Sim.Missions;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The AI's turn speed, measured in the original (retail 1.0 on an emulated Pentium, 2 boots, T05, every frame): every
/// wheeled AI car held 12.52198 m/s, the player's car 13.71714 and a van 15.83919, the same on every frame, while the
/// cars' own steer gains were 0.177 to 0.290. Those are sqrt(7.84 x the four corner grips {4, 4.8, 6.4} / 0.2): the
/// value is stored at spawn with the steer gain still at its default, and changes only when a wheel is damaged (the
/// player's fell 13.71714 to 13.67632 at the first hit, which is one wheel at 0.8875 with the car's own gain 0.188097).
/// I26 had worked the formula out on the live car at every call (13.1 to 14.4 m/s).
/// </summary>
public class AiTurnSpeedTests
{
    private static Vehicle Car(float grip, float steerGain)
    {
        var v = new Vehicle();
        foreach (var w in v.Wheels) w.Grip = grip;
        v.P.SteerGain = steerGain;
        v.TurnSpeed = v.TurnSpeedFor(Vehicle.SpawnSteerGain);
        return v;
    }

    [Theory]
    [InlineData(1.0f, 12.52198f)]
    [InlineData(1.2f, 13.71714f)]
    [InlineData(1.6f, 15.83919f)]
    [InlineData(0f, 0f)]          // no wheels' grip (the original's wheelless car): sqrt(0)
    public void TheSpawnTurnSpeedIsTheGripsOverTheDefaultGain(float grip, float expected)
    {
        foreach (float gain in new[] { 0.183056f, 0.188097f, 0.290249f })
            Assert.Equal(expected, Car(grip, gain).TurnSpeed, 1e-4f);
    }

    [Fact]
    public void TheMiddleWheelsAreNotCounted()
    {
        var v = new Vehicle { MiddleRight = new Wheel { Grip = 5f }, MiddleLeft = new Wheel { Grip = 5f } };
        foreach (var w in v.Wheels) w.Grip = 1f;
        Assert.Equal(12.52198f, v.TurnSpeedFor(Vehicle.SpawnSteerGain), 1e-4f);
    }

    [Fact]
    public void TheAiReadsTheStoredValueNotTheLiveCar()
    {
        var v = Car(1f, 0.183056f);
        var e = new Entity("ai", [], 1, 0) { Vehicle = v };
        var cruise = typeof(Ptable).GetMethod("Cruise", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, [typeof(Entity)])!;
        Assert.Equal(12.52198f, (float)cruise.Invoke(null, [e])!, 1e-4f);
        v.P.SteerGain = 0.1f;
        foreach (var w in v.Wheels) w.Grip = 1.5f;
        Assert.Equal(12.52198f, (float)cruise.Invoke(null, [e])!, 1e-4f);
    }

    [Fact]
    public void AWheelHitRecomputesItWithTheCarsGain()
    {
        var v = Car(1.2f, 0.188097f);
        var wp = new WheelPart(v.FrontLeft, 80, v);
        Assert.Equal(13.71714f, v.TurnSpeed, 1e-4f);
        var p = new DamagePacket { Mask = 1 };
        p.Ch[0] = 9;
        wp.Apply(p);                                     // 71 / 80 = 0.8875
        Assert.Equal(0.8875f, v.FrontLeft.Grip, 1e-6f);
        Assert.Equal(13.67632f, v.TurnSpeed, 1e-3f);     // measured; the gain is known to 6 digits
        var kill = new DamagePacket { Mask = 1 };
        kill.Ch[0] = 500;
        wp.Apply(kill);                                  // destroyed: grip 0.5
        Assert.Equal(MathF.Sqrt(7.84f * (3 * 1.2f + 0.5f) / 0.188097f), v.TurnSpeed, 1e-4f);
    }

    [Fact]
    public void AFactoryCarStoresTheSpawnValue()
    {
        if (Environment.GetEnvironmentVariable("I76_INSTALL") is not { Length: > 0 } dir || !Directory.Exists(dir)) return;
        var vfs = new AssetVfs(new GameInstall(dir));
        foreach (var name in new[] { "vppirna1.vcf", "vpbtg1.vcf" })
        {
            if (vfs.TryRead(name) is null) continue;
            var v = VehicleFactory.Create(vfs, name);
            float sum = v.Wheels.Sum(w => w.Grip);
            Assert.Equal(MathF.Sqrt(7.84f * sum / 0.2f), v.TurnSpeed, 1e-4f);
            Assert.NotEqual(0.2f, v.P.SteerGain, 1e-3f);   // the car's own gain, which the spawn value does not use
        }
    }
}
