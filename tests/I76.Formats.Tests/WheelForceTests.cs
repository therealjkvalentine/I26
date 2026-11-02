using System.Text;
using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>The force files and the DLL's own effects as DirectInput effects for a wheel, and the rate limit in front of the device.</summary>
public class WheelForceTests
{
    /// <summary>One effect record as the shipped files lay it out: the 68-byte name block, then the parameter words.</summary>
    private static byte[] Record(string name, params int[] words)
    {
        var rec = new List<byte>();
        rec.AddRange(BitConverter.GetBytes(104));
        var n = new byte[64];
        Encoding.ASCII.GetBytes(name).CopyTo(n, 0);
        rec.AddRange(n);
        foreach (var w in words) rec.AddRange(BitConverter.GetBytes(w));
        return rec.ToArray();
    }

    /// <summary>A primitive: type, direction, duration, envelope sustain / fade %, frequency, max / min %.</summary>
    private static byte[] Prim(int type, int dir, int ms, int hz, int max, int min, int sustain = 100, int fade = 0) =>
        Record("p", 2, type, 3, dir, 0, ms, 100, 100, 0, 32, 0, 0, sustain, fade, 0, 0, 100, 20, hz, 100, max, min);

    private static byte[] Composite(int type, int children) =>
        Record("c", 3, type, 3, 0, 0, 1000, 100, 100, 0, 32, 0, 0, 100, 0, 0, 0, 100, 12, children, 7917328, 0, 1);

    private static byte[] UserDefined(int dir, int ms, params int[] samples) =>
        Record("u", [3, 201, 3, dir, 0, ms, 100, 100, 0, 32, 0, 0, 100, 0, 0, 0, 100, 12, samples.Length, 7824832, .. samples]);

    private static byte[] Riff(params byte[][] records)
    {
        byte[] Chunk(string id, byte[] body) => [.. Encoding.ASCII.GetBytes(id), .. BitConverter.GetBytes(body.Length), .. body, .. (body.Length % 2 == 1 ? new byte[1] : [])];
        byte[] List(string type, byte[] body) => Chunk("LIST", [.. Encoding.ASCII.GetBytes(type), .. body]);
        var trak = List("trak", records.SelectMany(r => List("efct", [.. Chunk("id  ", new byte[4]), .. Chunk("data", r)])).ToArray());
        var all = List("FORC", [.. Chunk("trgt", new byte[16]), .. trak]);
        return [.. Encoding.ASCII.GetBytes("RIFF"), .. BitConverter.GetBytes(all.Length - 8 + 4), .. Encoding.ASCII.GetBytes("FORC"), .. all[12..]];
    }

    private static List<FrcEffect> File(params byte[][] records) => FrcFile.Parse(Riff(records));

    [Fact]
    public void ASquareKeepsItsFrequencyLevelsAndDuration()
    {
        // ENGSTART's first effect: SquareLow, 13 Hz, 79 ms, +58 / -58 %, from the stick's right
        var e = WheelMap.Effect(File(Prim(FrcEffect.SquareLow, 90, 79, 13, 58, -58))[0], 1, axes: 2)!;
        Assert.Equal(WheelKind.Square, e.Kind);
        Assert.Equal(5800, e.Magnitude);
        Assert.Equal(0, e.Offset);
        Assert.Equal(76923u, e.PeriodUs);                  // 13 Hz
        Assert.Equal(18000, e.Phase);                      // low first
        Assert.Equal(79000u, e.DurationUs);
        Assert.Equal(9000, e.Direction);
        Assert.Equal(10000, e.Gain);
        Assert.False(e.HasEnvelope);
        // on a wheel the push from the right is the wheel's axis the other way: half a cycle on
        var w = WheelMap.Effect(File(Prim(FrcEffect.SquareLow, 90, 79, 13, 58, -58))[0], 1, axes: 1)!;
        Assert.Equal(0, w.Phase);
        Assert.Equal(0, w.Direction);
    }

    [Fact]
    public void UnevenLevelsAreAnOffsetAndTheEnvelopeIsInPercentOfTheDuration()
    {
        // ENGSTART's second: SquareHigh 26 Hz, 1246 ms, +49 / -31 %, attack 34, sustain 10, fade 56 %
        var e = WheelMap.Effect(File(Prim(FrcEffect.SquareHigh, 0, 1246, 26, 49, -31, sustain: 10, fade: 56))[0], 1, axes: 1)!;
        Assert.Equal(4000, e.Magnitude);
        Assert.Equal(900, e.Offset);
        Assert.Equal(0, e.Phase);
        Assert.Equal((uint)(1246000 * 0.34), e.AttackUs);
        Assert.Equal((uint)(1246000 * 0.56), e.FadeUs);
        Assert.Equal(0, e.AttackLevel);
        Assert.True(e.HasEnvelope);
    }

    [Fact]
    public void TheMissilesCosineIsOneCycleOfAOneWayPull()
    {
        // MISSILE2's kick: Cosine 4 Hz for 250 ms (one cycle), 0 to -100 %, aimed at 180 degrees
        var e = WheelMap.Effect(File(Prim(FrcEffect.Cosine, 180, 250, 4, 0, -100))[0], 0.7, axes: 2)!;
        Assert.Equal(WheelKind.Sine, e.Kind);
        Assert.Equal(9000, e.Phase);
        Assert.Equal(5000, e.Magnitude);
        Assert.Equal(-5000, e.Offset);
        Assert.Equal(250000u, e.PeriodUs);
        Assert.Equal(18000, e.Direction);
        Assert.Equal(7000, e.Gain);                        // the DLL's gain over the file's 100 %
        Assert.Equal(0, e.At(0), 0);                       // from nothing ...
        Assert.Equal(-7000, e.At(125000), 0);              // ... to the full pull half way
        // on a wheel the forward axis has no part: it is played on the wheel's axis whole
        Assert.Equal(-5000, WheelMap.Effect(File(Prim(FrcEffect.Cosine, 180, 250, 4, 0, -100))[0], 1, axes: 1)!.Offset);
    }

    [Fact]
    public void RampsConstantsAndCurvesMapToTheirTypes()
    {
        var ramp = WheelMap.Effect(File(Prim(FrcEffect.RampDown, 0, 70, 1, 100, -100))[0], 0.4, axes: 1)!;   // WPNCYCLE at 40 %
        Assert.Equal((WheelKind.Ramp, 10000, -10000, 4000), (ramp.Kind, ramp.Magnitude, ramp.End, ramp.Gain));
        Assert.Equal(0, ramp.At(35000), 0);
        // the left blowout's constant: -100 % from 270 degrees (the left), a pull to the left on a wheel
        var left = WheelMap.Effect(File(Prim(FrcEffect.Constant, 270, 3000, 1, -100, -100))[0], 1, axes: 1)!;
        Assert.Equal((WheelKind.Constant, -10000), (left.Kind, left.Magnitude));
        Assert.Null(WheelMap.Effect(File(Prim(FrcEffect.Constant, 0, 5000, 1, 0, 0))[0], 1, axes: 1));     // the sequences' rest
        var c = WheelMap.Effect(File(UserDefined(180, 50, 100, 102, -93, 22, 0))[0], 1, axes: 1)!;
        Assert.Equal(WheelKind.Custom, c.Kind);
        Assert.Equal([10000, 10000, -9300, 2200, 0], c.Samples!);   // percent, held to full
        Assert.Equal(10000u, c.SamplePeriodUs);            // 10 ms a sample
        Assert.Equal(-9300, c.At(25000), 0);
        Assert.Equal(0, c.At(50000), 0);                   // over
    }

    [Fact]
    public void ASequencePlaysItsEffectsOneAfterAnother()
    {
        var fx = File(Prim(FrcEffect.SquareLow, 90, 79, 13, 58, -58), Prim(FrcEffect.SquareHigh, 0, 1246, 26, 49, -31), Composite(FrcEffect.Sequence, 2));
        var list = WheelMap.File(fx, 1, axes: 1);
        Assert.Equal(2, list.Count);                       // the composite itself is no effect
        Assert.Equal(0u, list[0].StartDelayUs);
        Assert.Equal(79000u, list[1].StartDelayUs);
        var both = WheelMap.File(File(Prim(FrcEffect.Sine, 0, 3000, 9, 50, -50), Prim(FrcEffect.Constant, 270, 3000, 1, -100, -100), Composite(FrcEffect.Superimpose, 2)), 1, axes: 1);
        Assert.All(both, e => Assert.Equal(0u, e.StartDelayUs));
    }

    [Fact]
    public void TheDllsOwnEffects()
    {
        var r = WheelMap.Rattle(10, 50);                   // a square at the rate, half strength, from full over its first period
        Assert.Equal((WheelKind.Square, 5000, 100000u, 5000, 10000, 100000u), (r.Kind, r.Magnitude, r.PeriodUs, r.Gain, r.AttackLevel, r.AttackUs));
        Assert.Equal(WheelEffect.Infinite, r.DurationUs);
        var t = WheelMap.Thump(100);
        Assert.Equal((WheelKind.Triangle, 200000u, 2100u, 6300u), (t.Kind, t.DurationUs, t.AttackUs, t.FadeUs));
        Assert.Equal(21277u, t.PeriodUs);                  // 47 Hz
        var tri = WheelMap.Triangle(4, 70);
        Assert.Equal((5000, 2500, 250000u), (tri.Magnitude, tri.Offset, tri.PeriodUs));
        Assert.Equal(2000, WheelMap.Engine(20, 1000).Magnitude);
        Assert.Equal(125000u, WheelMap.Engine(20, 1000).PeriodUs);   // 8 Hz at idle
        Assert.Equal(250000u, WheelMap.Tyre(0.5, 1).PeriodUs);
        Assert.Equal(1500, WheelMap.Spring(0).Magnitude);
        Assert.Equal(6000, WheelMap.Spring(165).Magnitude);
    }

    [Fact]
    public void AHitFromTheRightPushesTheWheelLeft()
    {
        float ahead = WheelMap.Bearing(0, 10), right = WheelMap.Bearing(10, 10), left = WheelMap.Bearing(-10, 0), behind = WheelMap.Bearing(0, -10);
        Assert.Equal(0, ahead, 3);
        Assert.Equal(45, right, 3);
        Assert.Equal(270, left, 3);
        Assert.Equal(180, behind, 3);
        Assert.True(WheelMap.Jolt(5000, (int)right, 1).Magnitude < 0);
        Assert.True(WheelMap.Jolt(5000, (int)left, 1).Magnitude > 0);
        Assert.Equal(5000, WheelMap.Jolt(5000, 0, 1).Magnitude);     // head on: the wheel's axis whole
        var stick = WheelMap.Jolt(5000, 45, 2);
        Assert.Equal((5000, 4500), (stick.Magnitude, stick.Direction));
    }

    [Fact]
    public void AnEffectsShapeOverTime()
    {
        var c = new WheelEffect { Kind = WheelKind.Constant, Magnitude = -8000, DurationUs = 1000, AttackUs = 200, AttackLevel = 0, FadeUs = 200, FadeLevel = 0 };
        Assert.Equal(0, c.At(0), 3);
        Assert.Equal(-4000, c.At(100), 3);
        Assert.Equal(-8000, c.At(500), 3);
        Assert.Equal(-4000, c.At(900), 3);
        Assert.Equal(0, c.At(1000), 3);
        var s = new WheelEffect { Kind = WheelKind.Square, Magnitude = 5000, Offset = 1000, PeriodUs = 1000, Gain = 5000, StartDelayUs = 100 };
        Assert.Equal(0, s.At(50), 3);                      // not started
        Assert.Equal(3000, s.At(200), 3);
        Assert.Equal(-2000, s.At(700), 3);
        var u = new WheelEffect { Kind = WheelKind.Custom, Samples = [100, -200], SamplePeriodUs = 10 };
        Assert.Equal(-200, u.At(35), 3);                   // an infinite curve plays over again
        Assert.Equal(0, new WheelEffect { Kind = WheelKind.Spring, Magnitude = 3000 }.At(10), 3);
    }

    private static Dictionary<string, WheelSlot> Frame(params (string Slot, WheelEffect E, int Gen)[] slots) => slots.ToDictionary(s => s.Slot, s => new WheelSlot(s.E, s.Gen));

    [Fact]
    public void TheCoalescerSendsOnlyWhatChanged()
    {
        var co = new WheelCoalescer();
        var engine = WheelMap.Engine(20, 1000);
        var c = Assert.Single(co.Diff(Frame(("engine", engine, 0)), 0));
        Assert.Equal((WheelOp.Create, "engine"), (c.Op, c.Slot));
        Assert.Empty(co.Diff(Frame(("engine", engine, 0)), 0.1));                       // nothing changed, nothing sent
        Assert.Empty(co.Diff(Frame(("engine", engine with { Magnitude = 2050 }, 0)), 0.2));   // under a step
        var u = Assert.Single(co.Diff(Frame(("engine", engine with { Magnitude = 1000 }, 0)), 0.3));
        Assert.Equal((WheelOp.Update, WheelParams.TypeSpecific), (u.Op, u.Params));       // a magnitude: the one report
        Assert.Empty(co.Diff(Frame(("engine", engine with { Magnitude = 500 }, 0)), 0.31));  // too soon after the last
        Assert.Equal(WheelParams.TypeSpecific, Assert.Single(co.Diff(Frame(("engine", engine with { Magnitude = 500 }, 0)), 0.4)).Params);
        Assert.Equal(WheelParams.Gain, Assert.Single(co.Diff(Frame(("engine", engine with { Magnitude = 500, Gain = 5000 }, 0)), 0.5)).Params);
        // a restarted voice starts again, a gone one is removed
        Assert.Equal(WheelOp.Restart, Assert.Single(co.Diff(Frame(("engine", engine with { Magnitude = 500, Gain = 5000 }, 1)), 0.6)).Op);
        var r = Assert.Single(co.Diff(Frame(), 0.7));
        Assert.Equal((WheelOp.Remove, "engine"), (r.Op, r.Slot));
        Assert.Empty(co.Live);
    }

    [Fact]
    public void ClearingRemovesEverySlotOnce()
    {
        var co = new WheelCoalescer();
        co.Diff(Frame(("a", WheelMap.Spring(0), 0), ("b", WheelMap.Rattle(10, 50), 0)), 0);
        Assert.Equal(2, co.Clear().Count(c => c.Op == WheelOp.Remove));
        Assert.Empty(co.Clear());
        Assert.Equal(2, co.Diff(Frame(("a", WheelMap.Spring(0), 0), ("b", WheelMap.Rattle(10, 50), 0)), 1).Count(c => c.Op == WheelOp.Create));
    }

    [Fact]
    public void TheMailboxKeepsTheNewestFrameAndEveryControl()
    {
        var box = new WheelMailbox<string, int>();
        box.Post("a"); box.Post("b"); box.Send(1); box.Post("c"); box.Send(2);
        var (controls, frame) = box.Take(0);
        Assert.Equal("c", frame);
        Assert.Equal([1, 2], controls);
        Assert.Equal(2, box.Dropped);
        var (none, empty) = box.Take(0);
        Assert.Empty(none);
        Assert.Null(empty);
    }

    [Fact]
    public void TheModelFillsTheWheelsSlots()
    {
        byte[] Cust() => Riff(UserDefined(180, 330, 100, -90, -50));
        var files = new Dictionary<string, byte[]>
        {
            ["CANNON3"] = Cust(), ["EXPLOSN"] = Riff(Prim(FrcEffect.SawtoothDown, 0, 945, 12, 100, -100, sustain: 29, fade: 71)),
            ["WPNCYCLE"] = Riff(Prim(FrcEffect.RampDown, 0, 70, 1, 100, -100)),
        };
        var eng = new RumbleEngine(new NullSink());
        var wheel = new WheelFrame { Axes = 1 };
        var m = new ForceModel(FrcLibrary.Load(n => files.GetValueOrDefault(n)), eng, () => 0) { Wheel = wheel };

        var f = new ForceFrame { EngineOn = true, Rpm = 1000, Mph = 0 };
        f.Shots.Add(new WeaponShot(0, 2, 1, 10));                    // a machine gun: a rattle at 10 a second, 60 %
        f.Shots.Add(new WeaponShot(1, 2, 3, 3));                     // a cannon: CANNON3, looped, 70 %
        m.Frame(f, 0.05);
        var s = wheel.Slots;
        Assert.Equal(WheelKind.Square, s["hp0"].Effect.Kind);
        Assert.Equal((100000u, 6000), (s["hp0"].Effect.PeriodUs, s["hp0"].Effect.Gain));
        var cannon = s.Single(kv => kv.Key.StartsWith('v')).Value;
        Assert.Equal((WheelKind.Custom, 7000), (cannon.Effect.Kind, cannon.Effect.Gain));
        Assert.Equal(WheelKind.Spring, s["spring"].Effect.Kind);
        Assert.True(s.ContainsKey("engine"));                        // standing: the engine is felt (halved while weapons play)
        Assert.False(s.ContainsKey("jolt"));

        // the next frame without shots: the weapons are gone; a hit from the right is a push to the left and EXPLOSN
        var g = new ForceFrame { EngineOn = true, Rpm = 1000, HitDegrees = 90 };
        g.Hits.Add((0, 30));
        g.Hits.Add((1, 30));
        m.Frame(g, 0.05);
        Assert.False(s.ContainsKey("hp0"));
        Assert.DoesNotContain(s.Keys, k => k.StartsWith("v") && s[k].Effect.Kind == WheelKind.Custom);
        Assert.True(s["jolt"].Effect.Magnitude < 0);
        Assert.Contains(s.Values, v => v.Effect.Kind == WheelKind.SawtoothDown);
        Assert.False(s.ContainsKey("engine"));                       // silent while a hit is felt

        m.StopAll();
        Assert.Empty(s);
    }

    [Fact]
    public void ALoopingFileStartsOverEachLength()
    {
        var eng = new RumbleEngine(new NullSink());
        var fx = File(UserDefined(180, 100, 50, 50));
        var v = eng.Play(fx, 1, loop: true)!;
        Assert.Equal(0, v.Generation);
        eng.Output(0.06);
        Assert.Equal(0, v.Generation);
        eng.Output(0.06);
        Assert.Equal(1, v.Generation);
        v.Restart();
        Assert.Equal(1, v.Generation);                     // started over by hand: one start, no lap
        Assert.Equal(1, v.Starts);
    }

    private sealed class NullSink : IRumbleSink
    {
        public void Rumble(float weak, float strong) { }
        public void Stop() { }
    }
}
