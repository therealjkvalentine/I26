using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>The bass shaker's synthesis on its worker (<see cref="TactileStream"/>): the same samples as the synth run in place, underruns, start and stop.</summary>
public class TactileStreamTests
{
    private const int Rate = 48000;
    private const float TickSeconds = 102f / 2048f;

    /// <summary>
    /// A scripted drive's tick <paramref name="k"/>: the engine cranked and run up, asphalt, a dirt road, planks and off-road, wheels
    /// over road edges, shots, hits, explosions near and far, a helicopter passing over (every source the shaker has).
    /// </summary>
    internal static void Script(TactileEvents e, int k)
    {
        float t = k * TickSeconds;
        e.Cranking = t < 0.6f;
        e.EngineOn = t >= 0.6f;
        e.Rpm = e.EngineOn ? 900 + 3000 * MathF.Min(t / 3f, 1f) + 400 * MathF.Sin(t) : 300;
        e.Mph = MathF.Min(t * 15f, 70f);
        e.Surface = t < 2.5f ? 3 : t < 4 ? 4 : t < 5 ? 5 : t < 6.5f ? 1 : t < 7 ? 9 : 3;
        e.FlatTyres = t > 7.5f ? 1 : 0;
        for (int i = 0; i < 4; i++) e.Travel[i] = 0.3f * MathF.Sin(t * (3 + i) + i);
        e.TickSeconds = TickSeconds;
        e.Shoulder = t is > 1.5f and < 2.2f;
        if (k % 37 == 5) e.Crossings.Add(0.4f + 0.1f * (k % 5));
        if (t is > 3 and < 4 && k % 3 == 0) e.Shots.Add(new WeaponShot(0, 1u, 1u, 10f));
        if (k % 53 == 20) e.Hits.Add((k % 4, 30 + k % 90));
        if (k == 60) e.Blasts.Add(12f);
        if (k == 120) e.Blasts.Add(900f);
        float heli = MathF.Abs(300f - 80f * (t - 2f));
        if (t > 2 && heli < 400) e.Rotors.Add(heli + 40f);
    }

    private static TactileSynth Synth() => new(7)
    {
        Gain = AudioOutputs.ShakerGain(100),
        LowPassHz = AudioOutputs.DefaultCrossover,
        Eq = TactileEq.Parse("13:4, 18:4, 20:3.7, 22:1.6, 25:0, 28:-1.3, 31:-2.5, 35:-3.5, 40:-2.5, 45:-1.3, 50:0, 80:0, 90:1.6, 100:3.7"),
    };

    private static long At(int k) => (long)(k * TickSeconds * Rate) + 1000;

    /// <summary>The synth run in place, as the main thread ran it: each tick stepped at its sample, filled in frames of <paramref name="frame"/> between.</summary>
    internal static float[] InPlace(TactileSynth s, int ticks, int total, int frame)
    {
        var o = new float[total];
        var e = new TactileEvents();
        int pos = 0, k = 0;
        while (pos < total)
        {
            while (k < ticks && At(k) <= pos) { e.Clear(); Script(e, k); s.Step(e); k++; }
            int n = Math.Min(frame, total - pos);
            if (k < ticks) n = (int)Math.Min(n, At(k) - pos);
            s.Fill(o.AsSpan(pos, n), Rate);
            pos += n;
        }
        return o;
    }

    /// <summary>A generator's ring: the worker pushes, the test reads; an underrun is read as silence and counted.</summary>
    private sealed class FakeSink(int capacity) : ITactileSink
    {
        private readonly Queue<float> _q = new();
        public readonly List<float> Pushed = [];
        public int Underruns;
        public ManualResetEventSlim Gate { get; } = new(true);
        public int Capacity => capacity;
        public int Space { get { lock (_q) return capacity - _q.Count; } }
        public void Push(ReadOnlySpan<float> block)
        {
            Gate.Wait();
            lock (_q)
            {
                Assert.True(capacity - _q.Count >= block.Length, "pushed over the sink's space");
                foreach (var v in block) { _q.Enqueue(v); Pushed.Add(v); }
            }
        }

        /// <summary>The mixer's read: n frames, zeros past what is there.</summary>
        public void Read(Span<float> dst, List<bool>? real = null)
        {
            lock (_q)
            {
                bool under = false;
                for (int i = 0; i < dst.Length; i++)
                {
                    bool have = _q.Count > 0;
                    dst[i] = have ? _q.Dequeue() : 0f;
                    real?.Add(have);
                    under |= !have;
                }
                if (under) Underruns++;
            }
        }
    }

    /// <summary>The worker fed the same ticks at the same samples, read by a mixer of its own pace: the stream it pushes.</summary>
    private static (float[] Out, FakeSink Sink) Threaded(int ticks, int total, int readFrames, Action<FakeSink, int>? during = null)
    {
        var sink = new FakeSink(4095);
        using var st = new TactileStream(Synth(), sink, Rate, 0.05f);
        int k = 0;
        // the first ticks published before the worker starts, so none is late (the game's are placed at once: no such race)
        while (k < ticks && st.Begin() is { } e0) { Script(e0, k); st.Commit(At(k)); k++; }
        st.Start();
        var mix = new float[readFrames];
        int reads = 0;
        while (sink.Pushed.Count < total)
        {
            // the game's thread: each tick's slot published ahead of its sample
            while (k < ticks && (st.Begin() is { } e))
            {
                Script(e, k);
                st.Commit(At(k));
                k++;
            }
            sink.Read(mix);
            during?.Invoke(sink, reads++);
            Thread.Sleep(1);
        }
        st.Stop();
        return (sink.Pushed.Take(total).ToArray(), sink);
    }

    [Fact]
    public void The_worker_makes_the_samples_the_synth_makes_in_place()
    {
        int ticks = (int)(8 / TickSeconds), total = Rate * 8;
        var reference = InPlace(Synth(), ticks, total, 800);
        // other frame sizes in place are the same stream (the synth's state carries over between fills)
        Assert.Equal(reference, InPlace(Synth(), ticks, total, 137));
        var (got, _) = Threaded(ticks, total, 1024);
        Assert.Equal(reference.Length, got.Length);
        int diff = 0;
        for (int i = 0; i < total; i++) if (BitConverter.SingleToInt32Bits(got[i]) != BitConverter.SingleToInt32Bits(reference[i])) diff++;
        Assert.Equal(0, diff);
        Assert.True(reference.Max(MathF.Abs) > 0.3f, "the script plays");
    }

    [Fact]
    public void A_starved_worker_leaves_silence_then_carries_on_where_it_stopped()
    {
        int ticks = (int)(4 / TickSeconds), total = Rate * 4;
        var reference = InPlace(Synth(), ticks, total, 800);
        var real = new List<bool>();
        var heard = new List<float>();
        var sink = new FakeSink(4095);
        using var st = new TactileStream(Synth(), sink, Rate, 0.05f);
        int k = 0, reads = 0;
        while (k < ticks && st.Begin() is { } e0) { Script(e0, k); st.Commit(At(k)); k++; }
        st.Start();
        var mix = new float[480];
        while (sink.Pushed.Count < total)
        {
            while (k < ticks && st.Begin() is { } e) { Script(e, k); st.Commit(At(k)); k++; }
            // the worker held for 200 ms (descheduled), the mixer reading on
            if (reads == 100) sink.Gate.Reset();
            if (reads == 300) sink.Gate.Set();
            sink.Read(mix, real);
            heard.AddRange(mix);
            reads++;
            Thread.Sleep(1);
        }
        st.Stop();
        Assert.True(sink.Underruns > 0, "the mixer ran dry");
        // what the mixer read was silence or the next sample of the stream: never a repeat, a skip or garbage
        int next = 0;
        for (int i = 0; i < heard.Count; i++)
        {
            if (!real[i]) { Assert.Equal(0f, heard[i]); continue; }
            Assert.Equal(reference[next], heard[i]);
            next++;
        }
        Assert.True(next > Rate * 1, "the stream went on after the stall");
        int m = Math.Min(reference.Length, sink.Pushed.Count);
        Assert.Equal(reference.Take(m).ToArray(), sink.Pushed.Take(m).ToArray());
    }

    [Fact]
    public void The_sink_is_kept_at_its_depth_and_never_overfilled()
    {
        var sink = new FakeSink(4095);
        var s = Synth();
        using var st = new TactileStream(s, sink, Rate, 0.05f);
        Assert.Equal(2560, st.Depth);   // 50 ms at 48 kHz, up to whole blocks
        Assert.Equal(10, st.Pass());
        Assert.Equal(0, st.Pass());
        Assert.Equal(2560, sink.Capacity - sink.Space);
        sink.Read(new float[300]);
        Assert.Equal(1, st.Pass());
        // a depth over the sink is the sink's whole blocks
        Assert.Equal(3840, new TactileStream(new TactileSynth(), new FakeSink(4095), Rate, 1f).Depth);
    }

    [Fact]
    public void Start_and_stop_leave_no_thread_running()
    {
        int before = TactileStream.Running;
        for (int i = 0; i < 40; i++)
        {
            var sink = new FakeSink(4095);
            var st = new TactileStream(Synth(), sink, Rate, 0.05f);
            st.Start();
            st.Start();   // a second start is nothing
            SpinWait.SpinUntil(() => sink.Pushed.Count > 0, 2000);
            st.Dispose();
            st.Stop();    // and a second stop
        }
        Assert.Equal(before, TactileStream.Running);
        Assert.True(TactileStream.Running <= before);
    }

    [Fact]
    public void A_full_ring_drops_the_tick_and_counts_it()
    {
        using var st = new TactileStream(new TactileSynth(), new FakeSink(4095), Rate, 0.05f);
        for (int i = 0; i < TactileStream.Slots - 1; i++) { Assert.NotNull(st.Begin()); st.Commit(); }
        Assert.Null(st.Begin());
        Assert.Equal(1, st.Dropped);
        st.Pass();   // the worker steps them all
        Assert.NotNull(st.Begin());
    }

    [Fact]
    public void Settings_and_hold_reach_the_synth_through_the_worker()
    {
        var sink = new FakeSink(1 << 20);
        var s = new TactileSynth(3);
        using var st = new TactileStream(s, sink, Rate, 10f);
        var e = st.Begin()!;
        e.EngineOn = true; e.Rpm = 3000;
        st.Commit();
        st.Settings = new TactileSettings(AudioOutputs.ShakerGain(200), null, 90f, TactileSources.Defaults());
        st.Pass();
        Assert.Equal(3f, s.Gain);
        Assert.True(st.Peak > 0.05f);
        st.Hold = true;
        st.Pass();   // full: nothing pushed, but the synth reset
        sink.Read(new float[sink.Capacity]);
        st.Pass();
        Assert.True(st.Peak < 1e-3f, $"held: {st.Peak}");
        // with every source at 0 the engine is silent too
        st.Hold = false;
        st.Settings = new TactileSettings(1.5f, null, 90f, [.. TactileSources.Keys.Select(_ => 0)]);
        e = st.Begin()!;
        e.EngineOn = true; e.Rpm = 3000;
        st.Commit();
        sink.Read(new float[sink.Capacity]);
        st.Pass();
        Assert.True(st.Peak < 1e-3f);
    }
}
