using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Vfs;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Rng;
using I76.Sim.Session;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// The accelerator with the engine off (the ignition key, or cranking) changes nothing: the car coasts, brakes, steers and
/// rolls down a slope as with the pedal up (docs/deviations.md, "Accelerator with the engine off"). The original gates
/// only the drive force on the engine; its step reads the pedal elsewhere, and accelerating while rolling against the gear
/// faster than 1 m/s put a dead car in the wheel-spin slide model (measured before the fix, a synthetic 1600 kg car, 20 Hz:
/// backing at 6 m/s in drive with half left lock, the yaw rate went from the commanded -0.556 rad/s to +0.08, +1.02 a second
/// later and the car spun round; rolling back down a 15% grade the slide began at 1 m/s, 14 ticks in).
/// </summary>
public class EngineOffThrottleTests(ITestOutputHelper output)
{
    /// <summary>A plane rising by <paramref name="grade"/> per unit along +z (0: flat).</summary>
    private sealed class Slope(float grade) : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => ((float)(grade * z), Normal);
        public Vector3 Normal => Vector3.Normalize(new Vector3(0, 1, -grade));
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static readonly float Tick = SimRate.R20.TickSeconds;

    /// <summary>A car as the factory builds one, heading +z, rolling at <paramref name="speed"/> along its heading in <paramref name="gear"/>.</summary>
    private static Vehicle Car(Slope ground, float speed, float gear, bool engineOn)
    {
        var v = new Vehicle();
        foreach (var (w, x, z) in new[] { (v.FrontRight, 0.8f, 1.4f), (v.FrontLeft, -0.8f, 1.4f), (v.RearRight, 0.8f, -1.3f), (v.RearLeft, -0.8f, -1.3f) })
        { w.Grip = 1f; w.Rolling = 1f; w.Offset = new Vector3(x, 0.33f, z); w.InitSuspension(); }
        float wb = 2.7f;
        v.P.Mass = 1600; v.P.DriveScale = 1f / 1600; v.P.BrakeStrength = 2300f / 1600; v.P.Wheelbase = wb; v.P.Track = 1.6f;
        v.P.SteerGain = 1f / (2f * wb);
        v.P.YawRateLimit = 2.65618f / wb;
        v.P.PivotForward = v.RearLeft.Offset.Z;
        v.Engine.PeakForce = 190000; v.Engine.Curve = 190000 * 8.1632656e-8f;
        v.Flags |= VehicleFlags.CommandedYaw;
        if (!engineOn) v.Flags &= ~VehicleFlags.EngineOn;
        v.GroundNormal = ground.Normal;
        v.Gear = gear;
        v.Vel = v.Rot.Forward * speed;
        v.Speed = MathF.Abs(speed);
        return v;
    }

    private static string State(Vehicle v) => FormattableString.Invariant(
        $"pos {v.Pos} vel {v.Vel} speed {v.Speed} ang {v.AngVel} fwd {v.Rot.Forward} up {v.Rot.Up} flags 0x{(uint)v.Flags:x} gear {v.Gear} egear {v.Engine.Gear} rpm {v.Engine.Rpm} force {v.Engine.Force} spin {v.RearLeft.Spin} {v.FrontLeft.Spin}");

    /// <summary>
    /// Every tick's state with the pedal at <paramref name="throttle"/> (the brake from <paramref name="brakeFrom"/> on).
    /// The states print the floats' bits through their round-trip strings, so equal strings are the same car bit for bit.
    /// </summary>
    private static List<string> Run(float grade, float speed, float gear, float steer, float throttle, bool far, bool engineOn, int brakeFrom = int.MaxValue)
    {
        var ground = new Slope(grade);
        var step = new VehicleStep(ground, new CrtRand(1));
        var v = Car(ground, speed, gear, engineOn);
        var states = new List<string>();
        for (int i = 0; i < 80; i++)
        {
            v.Steer = steer;
            v.Throttle = i >= brakeFrom ? -0.9999f : throttle;
            if (far) step.CheapFrame(v, Tick, true); else step.Frame(v, Tick, true);
            states.Add(State(v));
        }
        return states;
    }

    private static int FirstDifference(List<string> a, List<string> b)
    {
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return i;
        return -1;
    }

    [Theory]
    [InlineData(0f, 15f, 1f, 0f)]      // coasting forward in drive
    [InlineData(0f, 15f, 1f, 0.5f)]    // coasting forward in drive, steering
    [InlineData(0f, -6f, 1f, 0f)]      // rolling backwards in drive (against the gear)
    [InlineData(0f, -6f, 1f, 0.5f)]
    [InlineData(0f, 6f, -1f, 0.5f)]    // rolling forwards in reverse (against the gear)
    [InlineData(0f, -6f, -1f, 0.5f)]   // rolling backwards in reverse
    [InlineData(0f, 0.5f, 1f, 0f)]     // a crawl under 1 m/s (the far model stops it with the pedal up)
    [InlineData(0.15f, 0f, 1f, 0f)]    // at rest facing up a 15% grade in drive: it rolls back
    [InlineData(0.15f, 0f, 1f, 0.5f)]
    [InlineData(-0.15f, 0f, -1f, 0.5f)] // at rest facing down it in reverse: it rolls forward
    [InlineData(-0.15f, 5f, 1f, 0.5f)] // rolling down it forwards in drive
    public void WithTheEngineOffTheAcceleratorChangesNothing(float grade, float speed, float gear, float steer)
    {
        foreach (bool far in new[] { false, true })
        {
            var up = Run(grade, speed, gear, steer, 0f, far, engineOn: false);
            foreach (float pedal in new[] { 0.0002f, 0.5f, 0.9999f })
            {
                var down = Run(grade, speed, gear, steer, pedal, far, engineOn: false);
                int d = FirstDifference(up, down);
                Assert.True(d < 0, $"{(far ? "far" : "near")} pedal {pedal}: tick {d}\n  pedal up   {(d < 0 ? "" : up[d])}\n  pedal down {(d < 0 ? "" : down[d])}");
            }
            // and the brake, pressed after a second of the accelerator, brakes as it does after a second of the pedal up
            Assert.Equal(Run(grade, speed, gear, steer, 0f, far, false, brakeFrom: 20), Run(grade, speed, gear, steer, 0.9999f, far, false, brakeFrom: 20));
        }
    }

    [Fact]
    public void BackingInDriveWithTheEngineOffTheAcceleratorNoLongerSpinsTheCar()
    {
        var ground = new Slope(0);
        var step = new VehicleStep(ground, new CrtRand(1));
        var v = Car(ground, -6f, 1f, engineOn: false);
        for (int i = 0; i < 20; i++)
        {
            v.Steer = 0.5f; v.Throttle = 0.9999f;
            step.Frame(v, Tick, true);
            Assert.True((v.Flags & VehicleFlags.Sliding) == 0, $"tick {i}: sliding");
            // gripping, the commanded yaw rate: steer · signed speed · gain, negative rolling backwards
            Assert.True(v.AngVel.Y < -0.5f, $"tick {i}: yaw rate {v.AngVel.Y}");
        }
        output.WriteLine($"after 1 s: yaw rate {v.AngVel.Y:0.0000}, speed {Vector3.Dot(v.Vel, v.Rot.Forward):0.000}");
    }

    [Fact]
    public void WithTheEngineRunningTheAcceleratorAgainstTheGearStillSlides()
    {
        // the original's wheel spin against the travel is kept for a car that has an engine to spin the wheels
        var ground = new Slope(0);
        var step = new VehicleStep(ground, new CrtRand(1));
        var v = Car(ground, -6f, 1f, engineOn: true);
        v.Steer = 0.5f; v.Throttle = 0.9999f;
        step.Frame(v, Tick, true);
        Assert.True((v.Flags & VehicleFlags.Sliding) != 0);
    }

    [Fact]
    public void AComputersCarWithItsEngineOffIsTheSame()
    {
        // the rule is the car's, not the driver's: a computer's car (isPlayer false) alike
        var ground = new Slope(0.15f);
        List<string> R(float t)
        {
            var step = new VehicleStep(ground, new CrtRand(1));
            var v = Car(ground, 0, 1f, engineOn: false);
            var s = new List<string>();
            for (int i = 0; i < 60; i++) { v.Steer = 0.5f; v.Throttle = t; step.Frame(v, Tick, false); s.Add(State(v)); }
            return s;
        }
        Assert.Equal(R(0), R(0.9999f));
    }

    [Fact]
    public void AutoReverseLeavesTheKeysAloneWithTheEngineOff()
    {
        var down = new CarInput { Buttons = CarButtons.ThrottleDown };
        var up = new CarInput { Buttons = CarButtons.ThrottleUp };
        // in reverse, rolling backwards: the brake key would be swapped to an accelerator, which with no engine does not brake
        Assert.Equal(down, new AutoReverse().Apply(down, reversing: true, forwardSpeed: -3f, throttle: 0, engineOn: false));
        Assert.NotEqual(down, new AutoReverse().Apply(down, reversing: true, forwardSpeed: -3f, throttle: 0, engineOn: true));
        // in drive, stopped: the brake key would shift into reverse and drive
        Assert.Equal(down, new AutoReverse().Apply(down, reversing: false, forwardSpeed: 0f, throttle: 0, engineOn: false));
        // in reverse, backing: accelerate would brake
        Assert.Equal(up, new AutoReverse().Apply(up, reversing: true, forwardSpeed: -3f, throttle: 0, engineOn: false));
    }

    private static string? InstallDir =>
        Environment.GetEnvironmentVariable("I76_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    /// <summary>
    /// A real car in a real session (the Piranha on Auto Melee m04, the keys through the session's ramp): driven backwards in
    /// drive at 6 m/s with the engine switched off by the ignition key, then the accelerate key held or not, with left lock.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(60)]
    public void ARealCarBackingWithItsEngineOffIgnoresTheAccelerateKey(int hz)
    {
        if (InstallDir is not { } dir) return;
        var rate = SimRate.FromLabel(hz);
        List<string> Drive(bool accelerate)
        {
            var install = new GameInstall(dir);
            var vfs = new AssetVfs(install);
            var asm = new ModelAssembler(vfs);
            var w = new MissionWorld(vfs, asm, install.FindMission("m04")!);
            var setup = MissionSetup.Build(vfs, w, asm, "vppirna1.vcf", script: false, rand: new CrtRand(1), seed: 1);
            setup.Melee?.Start();
            var s = new SimSession(setup, SessionOptions.SinglePlayer, rate);
            var v = setup.World.Player!;
            s.AddDriver(v);
            setup.World.Vehicles.RemoveAll(x => x != v);
            v.Pos = new Double3(4172.5, 119.9, 49552.5);
            v.Rot = new Rows(new Vector3(0, 0, -1), Vector3.UnitY, Vector3.UnitX);
            v.Vel = Vector3.Zero;
            float dt = rate.TickSeconds;
            void T(CarInput i) { s.Tick([i]); setup.Damage?.Events.Clear(); }
            for (int t = 0; t < (int)(4 / dt); t++) T(new CarInput { Commands = t * dt is >= 2.5f and < 2.9f ? CarCommands.StartEngine : CarCommands.None });
            Assert.NotEqual(0, (int)(v.Flags & VehicleFlags.EngineOn));
            T(new CarInput { Commands = CarCommands.StartEngine });
            Assert.Equal(0, (int)(v.Flags & VehicleFlags.EngineOn));
            v.Vel = v.Rot.Forward * -6f; v.Speed = 6f;
            var states = new List<string>();
            for (int t = 0; t < (int)(3 / dt); t++)
            {
                T(new CarInput { Buttons = CarButtons.SteerLeft | (accelerate ? CarButtons.ThrottleUp : CarButtons.None) });
                states.Add(FormattableString.Invariant($"pos {v.Pos} vel {v.Vel} ang {v.AngVel} fwd {v.Rot.Forward} flags 0x{(uint)v.Flags:x} gear {v.Gear} rpm {v.Engine.Rpm} steer {v.Steer}"));
            }
            output.WriteLine($"{hz} Hz accelerate {accelerate}: {states[^1]}");
            return states;
        }
        var up = Drive(false);
        var down = Drive(true);
        int d = FirstDifference(up, down);
        Assert.True(d < 0, $"tick {d}\n  pedal up   {(d < 0 ? "" : up[d])}\n  pedal down {(d < 0 ? "" : down[d])}");
    }
}
