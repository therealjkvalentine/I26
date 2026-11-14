using System.Numerics;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The handbrake pressed while the car rolls backwards reversed the steering (a co-op session: forward, a handbrake turn to
/// roll backwards, the handbrake again, and the car turned the other way for the same steer). The original's slide kicks
/// the yaw rate on its first step by the signed speed, which rolling backwards negates it, and then steers by the gear, which
/// rolling backwards in drive holds it reversed. A human's car keeps the sense it had at the press; the computer's cars, and
/// every slide begun rolling the gear's way, are the original's (docs/deviations.md, "Handbrake rolling backwards").
/// </summary>
public class HandbrakeSteeringTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static readonly float Tick = I76.Sim.Session.SimRate.R20.TickSeconds;

    /// <summary>A car as the factory builds one (its grip model, steer gain, yaw-rate limit and pivot from the wheelbase), heading +z.</summary>
    private static Vehicle Car()
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
        v.Flags |= VehicleFlags.EngineOn | VehicleFlags.CommandedYaw;
        return v;
    }

    /// <summary>The car rolling at <paramref name="signedSpeed"/> along its heading (negative: backwards) in <paramref name="gear"/>.</summary>
    private static Vehicle Rolling(float signedSpeed, float gear)
    {
        var v = Car();
        v.Gear = gear;
        v.Vel = v.Rot.Forward * signedSpeed;
        v.Speed = MathF.Abs(signedSpeed);
        return v;
    }

    private static bool Sliding(Vehicle v) => (v.Flags & VehicleFlags.Sliding) != 0;

    /// <summary>
    /// Grips for a few ticks with the steer held, then holds the handbrake; the yaw rate before the press and on every tick
    /// after it while the car is still moving.
    /// </summary>
    private static (float Before, List<float> After, bool Slid) PressWhileRolling(float signedSpeed, float gear, float steer, bool human)
    {
        var step = new VehicleStep(new Flat(), new CrtRand(1));
        var v = Rolling(signedSpeed, gear);
        v.Steer = steer; v.Throttle = 0;
        for (int i = 0; i < 6; i++) step.Frame(v, Tick, human);
        Assert.False(Sliding(v), "the car grips before the press");
        float before = v.AngVel.Y;
        var after = new List<float>();
        bool slid = false;
        v.Handbrake = 1;
        for (int i = 0; i < 40 && v.Speed > 2f; i++)
        {
            step.Frame(v, Tick, human);
            slid |= Sliding(v);
            after.Add(v.AngVel.Y);
        }
        return (before, after, slid);
    }

    [Theory]
    [InlineData(1f)]    // in drive: after a handbrake turn, or rolling back down a slope
    [InlineData(-1f)]   // in reverse: the reverse 180
    public void TheHandbrakeRollingBackwardsKeepsTheSteeringsSense(float gear)
    {
        var (before, after, slid) = PressWhileRolling(-10f, gear, 0.5f, human: true);
        Assert.True(slid, "the handbrake slides the car");
        Assert.NotEmpty(after);
        // gripping, the yaw rate is steer · signed speed · gain: rolling backwards a positive steer gives a negative rate; the press keeps it so
        Assert.True(before < -0.1f, $"yaw rate before the press {before}");
        for (int i = 0; i < after.Count; i++)
            Assert.True(after[i] < 0, $"tick {i} after the press: yaw rate {after[i]} (before it {before})");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void TheComputersCarsKeepTheOriginalsSlide(float gear)
    {
        // the original's first sliding step multiplies the yaw rate by the signed speed / 10: rolling backwards it changes sign
        var (before, after, _) = PressWhileRolling(-10f, gear, 0.5f, human: false);
        Assert.True(before < -0.1f, $"yaw rate before the press {before}");
        Assert.True(after[0] > 0, $"the original's first sliding tick: yaw rate {after[0]}");
    }

    /// <summary>Two copies of a car under the same inputs, one stepped as a human's and one as the computer's: the same car, bit for bit.</summary>
    private static void SameAsTheOriginal(Vehicle a, Vehicle b, Func<int, (float Steer, float Throttle, int Handbrake)> inputs, int ticks)
    {
        var sa = new VehicleStep(new Flat(), new CrtRand(1));
        var sb = new VehicleStep(new Flat(), new CrtRand(1));
        for (int i = 0; i < ticks; i++)
        {
            var (s, t, h) = inputs(i);
            (a.Steer, a.Throttle, a.Handbrake) = (s, t, h);
            (b.Steer, b.Throttle, b.Handbrake) = (s, t, h);
            sa.Frame(a, Tick, true);
            sb.Frame(b, Tick, false);
            Assert.True(a.Pos == b.Pos && a.Vel == b.Vel && a.AngVel == b.AngVel && a.Rot.Forward == b.Rot.Forward && a.Flags == b.Flags,
                $"tick {i}: human {a.Pos} {a.AngVel} {a.Flags}, original {b.Pos} {b.AngVel} {b.Flags}");
        }
    }

    [Fact]
    public void AForwardHandbrakeTurnIsTheOriginals()
    {
        // 18 u/s forwards in drive, a right steer, the handbrake from the tenth tick to the fiftieth: the J-turn
        SameAsTheOriginal(Rolling(18f, 1f), Rolling(18f, 1f), i => (0.5f, 0f, i >= 10 && i < 50 ? 1 : 0), 120);
    }

    [Fact]
    public void InReverseTheSlideSteersByTheGear()
    {
        // reversing in reverse the travel and the gear agree: the slide steers by the gear as the original's (only its first
        // tick's kick is unsigned), and the latch is never set
        var step = new VehicleStep(new Flat(), new CrtRand(1));
        var v = Rolling(-12f, -1f);
        v.Steer = 0.5f;
        for (int i = 0; i < 6; i++) step.Frame(v, Tick, true);
        v.Handbrake = 1;
        for (int i = 0; i < 30 && v.Speed > 2f; i++)
        {
            step.Frame(v, Tick, true);
            Assert.True((v.Flags & VehicleFlags.SlideAgainstGear) == 0, "rolling the gear's way the slide keeps the gear's sense");
            Assert.True(v.AngVel.Y < 0, $"tick {i}: yaw rate {v.AngVel.Y}");
        }
    }

    [Theory]
    [InlineData(-10f, -1f)]   // normal reversing
    [InlineData(-10f, 1f)]    // rolling backwards in drive, no handbrake
    [InlineData(15f, 1f)]     // driving forwards
    public void WithoutTheHandbrakeTheSteeringIsTheOriginals(float signedSpeed, float gear)
    {
        SameAsTheOriginal(Rolling(signedSpeed, gear), Rolling(signedSpeed, gear), i => (i < 20 ? 0.5f : -0.5f, 0f, 0), 40);
    }

    [Fact]
    public void ForwardThenAHandbrakeTurnThenTheHandbrakeAgainKeepsTheSense()
    {
        // the session's sequence: forwards with a right steer held, a handbrake turn until the car rolls backwards, the handbrake
        // released, and once the car grips rolling backwards, the handbrake again. At the second press the yaw rate keeps its sign.
        var step = new VehicleStep(new Flat(), new CrtRand(1));
        var v = Rolling(24f, 1f);
        v.Steer = 0.5f;
        for (int i = 0; i < 6; i++) step.Frame(v, Tick, true);
        Assert.True(v.AngVel.Y > 0.1f, $"forwards a positive steer gives a positive yaw rate: {v.AngVel.Y}");
        float SignedSpeed() => Vector3.Dot(v.Vel, v.Rot.Forward);
        // the handbrake turn: held until the car's travel is behind its nose
        v.Handbrake = 1;
        int t = 0;
        for (; t < 120 && SignedSpeed() >= 0 && v.Speed > 2f; t++)
        {
            step.Frame(v, Tick, true);
            Assert.True(v.AngVel.Y > 0, $"tick {t} of the turn: yaw rate {v.AngVel.Y}");   // the original's J-turn, unchanged
        }
        v.Handbrake = 0;
        // now steer the other way, as a driver straightening a reverse would, until the slide ends with the car still rolling back
        v.Steer = -0.5f;
        for (int i = 0; i < 120 && Sliding(v) && v.Speed > 4f; i++) step.Frame(v, Tick, true);
        if (Sliding(v) || SignedSpeed() > -4f)
        {
            // the turn bled the speed off before the car gripped rolling backwards: the constructed cases above cover the press
            output.WriteLine($"not reached: sliding {Sliding(v)}, signed speed {SignedSpeed()} after {t} ticks of the turn");
            return;
        }
        for (int i = 0; i < 3; i++) step.Frame(v, Tick, true);
        Assert.False(Sliding(v));
        float before = v.AngVel.Y;
        Assert.True(before > 0.05f, $"rolling backwards a negative steer gives a positive yaw rate: {before}");
        output.WriteLine($"second press at signed speed {SignedSpeed()}, yaw rate {before}");
        v.Handbrake = 1;
        for (int i = 0; i < 20 && v.Speed > 2f; i++)
        {
            step.Frame(v, Tick, true);
            Assert.True(v.AngVel.Y > 0, $"tick {i} after the second press: yaw rate {v.AngVel.Y} (before it {before})");
        }
    }
}
