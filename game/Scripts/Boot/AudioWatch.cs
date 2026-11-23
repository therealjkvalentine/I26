using System.Collections.Concurrent;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// I76_AUDIO_WATCH=PREFIX: what the sound does through a mission's load and its first seconds, for measuring the first start against
/// a second one (docs/dev-hooks.md). A thread of its own (none of it on the main thread) every millisecond or so: the audio driver's
/// time since its last mix (each wait between mixes over <see cref="StallMs"/> is written down: the Dummy driver mixes about 93 ms at
/// a time, so its usual wait is taken off when reading them; what is left is how late the mixing thread got, which a device's short
/// buffer, 5 ms here, would not have covered), and every 10 ms the game's own sounds (the SFX, Voice and Cues buses, not the music or a movie) and the whole mix
/// (Master, ahead of I76_AUDIO_SILENT's cut) taken off capture effects. PREFIX.csv has a row a block (wall clock, the mix's frames so
/// far, the game's and the mix's peak) and a row for each stall and each load stamp (<see cref="Mark"/>); PREFIX.f32 is the whole mix,
/// stereo float32 at the mix rate, for listening and for looking for gaps and repeats; PREFIX.game.f32 the game's buses, mono, frame for
/// frame beside it.
/// </summary>
public static class AudioWatch
{
    private static readonly string? Prefix = System.Environment.GetEnvironmentVariable("I76_AUDIO_WATCH") is { Length: > 0 } p ? p : null;
    public static bool On => Prefix is not null;

    /// <summary>A wait between two mixes longer than this is written down (ms).</summary>
    public const double StallMs = 20;

    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly ConcurrentQueue<string> Marks = new();
    private static readonly object Lock = new();
    private static readonly System.Collections.Generic.List<AudioEffectCapture> Game = [];
    private static AudioEffectCapture? _mix;
    private static System.Threading.Thread? _thread;
    private static volatile bool _stop;

    /// <summary>The captures put on the buses (main thread; again after the buses are made: once each), and the thread started.</summary>
    public static void Attach()
    {
        if (!On) return;
        lock (Lock)
        {
            foreach (var name in new[] { AudioMix.Sfx, AudioMix.Voice, AudioMix.Cues })
            {
                int bus = AudioServer.GetBusIndex(name);
                if (bus < 0 || Captured(bus)) continue;
                var cap = new AudioEffectCapture { BufferLength = 2f, ResourceName = "audio_watch" };
                AudioServer.AddBusEffect(bus, cap);
                Game.Add(cap);
            }
            if (_mix is null)
            {
                // on Master after its limiter, ahead of I76_AUDIO_SILENT's -80 dB (the last effect, when there)
                int at = AudioServer.GetBusEffectCount(0);
                if (at > 0 && AudioServer.GetBusEffect(0, at - 1) is AudioEffectAmplify { VolumeDb: <= -79 }) at--;
                _mix = new AudioEffectCapture { BufferLength = 2f, ResourceName = "audio_watch" };
                AudioServer.AddBusEffect(0, _mix, at);
            }
        }
        if (_thread is null)
        {
            _thread = new System.Threading.Thread(Run) { IsBackground = true, Name = "AudioWatch", Priority = System.Threading.ThreadPriority.AboveNormal };
            _thread.Start();
            // stopped before the engine's servers go
            if (Engine.GetMainLoop() is SceneTree tree) tree.Root.TreeExiting += () => { _stop = true; _thread?.Join(500); };
        }
    }

    private static bool Captured(int bus)
    {
        for (int k = 0; k < AudioServer.GetBusEffectCount(bus); k++)
            if (AudioServer.GetBusEffect(bus, k) is AudioEffectCapture { ResourceName: "audio_watch" }) return true;
        return false;
    }

    /// <summary>A stamp on the watch's clock (a load stamp, the first frame after the card), from any thread.</summary>
    public static void Mark(string what)
    {
        if (!On) return;
        Marks.Enqueue(System.FormattableString.Invariant($"{Clock.Elapsed.TotalMilliseconds:0.0},mark,{what.Replace(',', ';')}"));
        // the frames drawn once the card has gone: the first picture of the mission, against the sounds heard before it
        if (what == "loading card gone" && LoadProfiler.IsMain) FramesAfter(3);
        if (ListPlayers && LoadProfiler.IsMain && !what.StartsWith("frame drawn") && !what.StartsWith("first use") && !what.StartsWith("playing")) Players();
    }

    /// <summary>I76_AUDIO_WATCH_PLAYERS=1: at each main-thread stamp, the players newly heard (a walk of the tree: it costs the load time).</summary>
    private static readonly bool ListPlayers = System.Environment.GetEnvironmentVariable("I76_AUDIO_WATCH_PLAYERS") == "1";
    private static readonly System.Collections.Generic.HashSet<ulong> Heard = [];

    /// <summary>The players heard now (playing, and not held by a paused tree) that were not at the last stamp, each as a mark.</summary>
    private static void Players()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var now = new System.Collections.Generic.HashSet<ulong>();
        void Walk(Node n)
        {
            (bool playing, AudioStream? st, string bus) = n switch
            {
                AudioStreamPlayer p => (p.Playing && !p.StreamPaused, p.Stream, p.Bus.ToString()),
                AudioStreamPlayer3D p => (p.Playing && !p.StreamPaused, p.Stream, p.Bus.ToString()),
                AudioStreamPlayer2D p => (p.Playing && !p.StreamPaused, p.Stream, p.Bus.ToString()),
                _ => (false, null, ""),
            };
            if (playing && n.CanProcess())
            {
                ulong id = n.GetInstanceId();
                now.Add(id);
                if (!Heard.Contains(id)) Mark($"playing {n.GetParent()?.Name}/{n.Name} {st?.ResourceName ?? ""} on {bus}");
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(tree.Root);
        Heard.Clear();
        Heard.UnionWith(now);
    }

    private static void FramesAfter(int n)
    {
        if (n <= 0) return;
        RenderingServer.Singleton.Connect(RenderingServer.SignalName.FramePostDraw, Callable.From(() => { Mark($"frame drawn after the card ({4 - n})"); FramesAfter(n - 1); }),
            (uint)GodotObject.ConnectFlags.OneShot);
    }

    /// <summary>A first use of a sound (AudioBank): its name, how long it took to read and make, and on which thread.</summary>
    public static void FirstUse(string name, double ms) =>
        Mark(System.FormattableString.Invariant($"first use {name} {ms:0.0} ms{(LoadProfiler.IsMain ? " (main thread)" : "")}"));

    private static void Run()
    {
        using var csv = new System.IO.StreamWriter(Prefix + ".csv") { AutoFlush = false };
        using var wav = new System.IO.BinaryWriter(System.IO.File.Create(Prefix + ".f32"));
        using var game_ = new System.IO.BinaryWriter(System.IO.File.Create(Prefix + ".game.f32"));
        csv.WriteLine($"# i26 audio watch: mix rate {AudioServer.GetMixRate()}, output latency {AudioServer.GetOutputLatency() * 1000:0.0} ms, stall over {StallMs} ms");
        csv.WriteLine("ms,kind,a,b,c");
        double lastSince = 0, worst = 0, nextDrain = 0, nextFlush = 1000;
        long frames = 0;
        while (!_stop)
        {
            double now = Clock.Elapsed.TotalMilliseconds;
            double since = AudioServer.GetTimeSinceLastMix() * 1000;
            if (since < lastSince)
            {
                // a mix came: the wait before it was a stall if long
                if (worst > StallMs) csv.WriteLine(System.FormattableString.Invariant($"{now:0.0},stall,{worst:0.0},,"));
                worst = 0;
            }
            worst = System.Math.Max(worst, since);
            lastSince = since;
            while (Marks.TryDequeue(out var m)) csv.WriteLine(m);
            if (now >= nextDrain)
            {
                nextDrain = now + 10;
                float game = 0, mix = 0;
                lock (Lock)
                {
                    if (_mix is { } c)
                    {
                        var buf = c.GetBuffer(c.GetFramesAvailable());
                        // the game's buses summed beside the mix (a silent bus is not mixed, so gives fewer frames: its frames are the
                        // last ones, the rest silence)
                        var sum = new float[buf.Length];
                        foreach (var g in Game)
                        {
                            var gb = g.GetBuffer(System.Math.Min(g.GetFramesAvailable(), buf.Length));
                            for (int i = 0; i < gb.Length; i++) sum[buf.Length - gb.Length + i] += (gb[i].X + gb[i].Y) * 0.5f;
                            g.ClearBuffer();
                        }
                        for (int i = 0; i < buf.Length; i++)
                        {
                            var f = buf[i];
                            wav.Write(f.X); wav.Write(f.Y); game_.Write(sum[i]);
                            mix = System.Math.Max(mix, System.Math.Max(System.Math.Abs(f.X), System.Math.Abs(f.Y)));
                            game = System.Math.Max(game, System.Math.Abs(sum[i]));
                        }
                        frames += buf.Length;
                    }
                }
                csv.WriteLine(System.FormattableString.Invariant($"{now:0.0},block,{frames},{Db(game):0.0},{Db(mix):0.0}"));
            }
            if (now >= nextFlush) { nextFlush = now + 1000; csv.Flush(); wav.Flush(); game_.Flush(); }
            System.Threading.Thread.Sleep(1);
        }
        while (Marks.TryDequeue(out var m)) csv.WriteLine(m);
    }

    private static double Db(float peak) => peak <= 1e-6f ? -120 : 20 * System.Math.Log10(peak);
}
