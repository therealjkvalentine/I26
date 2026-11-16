using System.Diagnostics;
using System.Globalization;
using Godot;
using I76.Formats.Input;

namespace I76.Game;

public partial class BassShaker
{
    /// <summary>
    /// I76_SHAKER_TIMES=PATH.csv: the bass shaker's cost on the main thread a frame (its process step and the ticks' gathering, in
    /// microseconds, with the frame's wall time), the worker's passes (their time and the blocks pushed), each tick's delay (the frames
    /// the generator held when the tick was stepped) and the generator's underruns, over a window from I76_SHAKER_TIMES_SKIP_MS
    /// (8000) after the shaker opened for I76_SHAKER_TIMES_RUN_MS (12000); then the file is written and the game quits
    /// (I76_SHAKER_TIMES_QUIT=0: it plays on). Arrays made at the start: nothing allocated while measuring.
    /// </summary>
    private static class Times
    {
        public static readonly string? Path = System.Environment.GetEnvironmentVariable("I76_SHAKER_TIMES") is { Length: > 0 } p ? p : null;
        public static readonly bool On = Path is not null;
        private static readonly long SkipTicks = Ms("I76_SHAKER_TIMES_SKIP_MS", 8000), RunTicks = Ms("I76_SHAKER_TIMES_RUN_MS", 12000);
        private static readonly bool Quit = System.Environment.GetEnvironmentVariable("I76_SHAKER_TIMES_QUIT") != "0";
        private const int Max = 40000;
        private static readonly double[] FrameDt = new double[On ? Max : 0], FrameUs = new double[On ? Max : 0];
        private static readonly double[] PassT = new double[On ? Max * 4 : 0], PassUs = new double[On ? Max * 4 : 0];
        private static readonly int[] PassBlocks = new int[On ? Max * 4 : 0];
        private static readonly int[] Lat = new int[On ? Max : 0];
        private static int _frames, _passes, _lats;
        private static long _open, _tickTicks, _lastFrame;
        private static bool _threaded, _done, _opened;
        private static int _rate, _capacity, _depth, _skips0 = -1;
        private static Func<int>? _skips;

        private static long Ms(string key, int def) =>
            (int.TryParse(System.Environment.GetEnvironmentVariable(key), out int ms) ? ms : def) * Stopwatch.Frequency / 1000;

        private static bool In(long now) => _opened && !_done && now - _open >= SkipTicks && now - _open < SkipTicks + RunTicks;

        public static void Opened(bool threaded, int rate, int capacity, int depth)
        {
            if (_opened) return;   // the first opening of the run (a reload keeps its window)
            _opened = true;
            _threaded = threaded; _rate = rate; _capacity = capacity; _depth = depth;
            _open = Stopwatch.GetTimestamp();
            _skips = _instance?._playback is { } pb ? pb.GetSkips : null;
        }

        public static void Attach(TactileStream s, int rate)
        {
            s.Stepped = q => { if (In(Stopwatch.GetTimestamp()) && _lats < Lat.Length) Lat[_lats++] = q; };
            s.Passed = (ticks, blocks) =>
            {
                long now = Stopwatch.GetTimestamp();
                if (!In(now) || _passes >= PassT.Length) return;
                PassT[_passes] = (now - _open) / (double)Stopwatch.Frequency;
                PassUs[_passes] = ticks * 1e6 / Stopwatch.Frequency;
                PassBlocks[_passes++] = blocks;
            };
        }

        public static void Tick(long ticks) => _tickTicks += ticks;

        public static void Latency(int queued)
        {
            if (In(Stopwatch.GetTimestamp()) && _lats < Lat.Length) Lat[_lats++] = queued;
        }

        public static void Frame(double delta, long processTicks, SceneTree tree)
        {
            long now = Stopwatch.GetTimestamp();
            long main = processTicks + _tickTicks;
            _tickTicks = 0;
            // the wall time since the last frame (Godot's delta is clamped to the physics steps a frame may run)
            double wall = _lastFrame == 0 ? delta * 1000 : (now - _lastFrame) * 1000.0 / Stopwatch.Frequency;
            _lastFrame = now;
            if (!_opened || _done) return;
            if (In(now))
            {
                if (_skips0 < 0) _skips0 = _skips?.Invoke() ?? 0;
                if (_frames < FrameDt.Length) { FrameDt[_frames] = wall; FrameUs[_frames++] = main * 1e6 / Stopwatch.Frequency; }
                return;
            }
            if (now - _open < SkipTicks + RunTicks) return;
            _done = true;
            Write(_skips is { } sk && _skips0 >= 0 ? sk() - _skips0 : -1);
            if (Quit) tree.Quit();
        }

        private static void Write(int skips)
        {
            var inv = CultureInfo.InvariantCulture;
            using var w = new System.IO.StreamWriter(Path!);
            w.WriteLine(string.Create(inv, $"# mode {(_threaded ? "worker" : "main")}, rate {_rate}, generator {_capacity} frames, queue {_depth} frames, underruns {skips}, running workers {TactileStream.Running}, driver {AudioServer.GetDriverName()}"));
            w.WriteLine("kind,a,b,c");
            for (int i = 0; i < _frames; i++) w.WriteLine(string.Create(inv, $"frame,{FrameDt[i]:0.000},{FrameUs[i]:0.00},"));
            for (int i = 0; i < _passes; i++) w.WriteLine(string.Create(inv, $"pass,{PassT[i]:0.0000},{PassUs[i]:0.00},{PassBlocks[i]}"));
            for (int i = 0; i < _lats; i++) w.WriteLine(string.Create(inv, $"lat,{Lat[i]},{Lat[i] * 1000.0 / _rate:0.00},"));
            GD.Print($"shaker times: {_frames} frames, {_passes} passes, {_lats} ticks, underruns {skips} -> {Path}");
        }
    }
}
