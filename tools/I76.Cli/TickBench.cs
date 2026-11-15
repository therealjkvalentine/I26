using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text;
using I76.Formats.Assembly;
using I76.Formats.Vfs;
using I76.Server;
using I76.Sim.Missions;
using I76.Sim.Session;
using I76.Sim.Vehicles;

namespace I76.Cli;

/// <summary>
/// i76 tick-bench: the simulation's tick timed headless (docs/perf/tick-bench.md).
///
///   i76 tick-bench SCENARIO [--rate 20|24|60...] [--minutes 5] [--split] [--hashes FILE] [--ticks FILE] [--cpu MASK] [--slow-ms 1]
///                  [--burst N] [--every K] [--gen0 BYTES]
///
/// SCENARIO: t01, t05, t06 (the mission from its start, nobody at the wheel, as i76 run plays it, ticked on past its end),
/// melee8 (M01 as a network melee of eight bots, the server's session), resim (melee8 plus, every K ticks, a client's
/// re-simulation burst: N predicted ticks of one bot's car from a capture, then the capture restored; the burst is timed apart).
/// --hashes writes the StateHash after every tick and times nothing (the hashing allocates; a timed run never hashes).
/// --split times the sections inside the tick (TickProbe; costs a little). --ticks writes a row a tick (CSV).
/// --cpu pins the process to the processors in MASK (hex, default c0: the last physical core's two threads) at high priority.
/// </summary>
public static class TickBench
{
    private const int Sections = 8;
    private static readonly string[] SectionNames = ["tick", "controls", "ai", "contacts", "physics", "script", "weapons", "damage"];

    /// <summary>The probe's timer: each section's exclusive time within the tick (a section's time less the sections nested in it).</summary>
    private sealed class Timer : ITickTimer
    {
        public bool Split;
        public readonly long[] Excl = new long[Sections];
        public long Start, Stop, AllocStart, AllocEnd;
        private readonly TickSection[] _stack = new TickSection[64];
        private int _depth;
        private long _mark;

        public void Begin(TickSection s)
        {
            long now = Stopwatch.GetTimestamp();
            if (s == TickSection.Tick) { AllocStart = GC.GetAllocatedBytesForCurrentThread(); Start = Stopwatch.GetTimestamp(); Array.Clear(Excl); _depth = 0; now = Start; }
            if (!Split && s != TickSection.Tick) return;
            if (_depth > 0) Excl[(int)_stack[_depth - 1]] += now - _mark;
            _stack[_depth++] = s;
            _mark = now;
        }

        public void End(TickSection s)
        {
            long now = Stopwatch.GetTimestamp();
            if (s == TickSection.Tick) { Stop = now; AllocEnd = GC.GetAllocatedBytesForCurrentThread(); }
            if (!Split && s != TickSection.Tick) return;
            if (_depth == 0) return;
            Excl[(int)_stack[--_depth]] += now - _mark;
            _mark = now;
        }
    }

    /// <summary>A built scenario: one tick of it, and what it reports a tick.</summary>
    private sealed class Scenario
    {
        public required SimSession Session;
        public required MissionSetup Setup;
        public required Action Tick;
        public Func<int>? Burst;                // runs a re-simulation burst if one is due this tick; the ticks it ran
    }

    public static int Run(GameInstall install, List<string> args)
    {
        string Opt(string name, string d) { int i = args.IndexOf(name); return i >= 0 && i + 1 < args.Count ? args[i + 1] : d; }
        string name = args.Count > 1 ? args[1].ToLowerInvariant() : throw new ArgumentException("tick-bench needs a scenario: t01, t05, t06, melee8, resim");
        int rateHz = int.Parse(Opt("--rate", "20"), CultureInfo.InvariantCulture);
        double minutes = double.Parse(Opt("--minutes", "5"), CultureInfo.InvariantCulture);
        bool split = args.Contains("--split");
        string? hashes = Opt("--hashes", "") is { Length: > 0 } hf ? hf : null;
        string? perTick = Opt("--ticks", "") is { Length: > 0 } tf ? tf : null;
        double slowMs = double.Parse(Opt("--slow-ms", "1"), CultureInfo.InvariantCulture);
        int burstN = int.Parse(Opt("--burst", "8"), CultureInfo.InvariantCulture);
        int every = int.Parse(Opt("--every", "5"), CultureInfo.InvariantCulture);
        long mask = long.Parse(Opt("--cpu", "c0"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var rate = SimRate.FromLabel(rateHz);
        if (rate.Label != rateHz) { Console.Error.WriteLine($"rate {rateHz} is not offered"); return 2; }

        var proc = Process.GetCurrentProcess();
        if (mask != 0)
        {
            try { proc.ProcessorAffinity = (IntPtr)mask; proc.PriorityClass = ProcessPriorityClass.High; }
            catch (Exception ex) { Console.Error.WriteLine($"pinning: {ex.Message}"); }
        }
        var loadWatch = Stopwatch.StartNew();
        var sc = Build(install, name, rate, burstN, every);
        double loadS = loadWatch.Elapsed.TotalSeconds;
        int ticks = rate.TicksFor((float)(minutes * 60));
        var timer = new Timer { Split = split };

        if (hashes is not null)
        {
            using var hw = new StreamWriter(hashes);
            hw.WriteLine($"# tick-bench {name} {rate.Label} Hz, {ticks} ticks: tick, StateHash after it");
            for (int t = 0; t < ticks; t++)
            {
                sc.Tick();
                sc.Burst?.Invoke();
                hw.WriteLine($"{t} {sc.Session.StateHash():x16}");
            }
            Console.WriteLine($"{name} {rate.Label} Hz: {ticks} hashes to {hashes}, last {sc.Session.StateHash():x16}");
            return 0;
        }

        var us = new double[ticks];
        var alloc = new long[ticks];
        var excl = new double[Sections, ticks];
        var burstUs = new List<double>();
        var slow = new List<string>();
        using var csv = perTick is null ? null : new StreamWriter(perTick);
        csv?.WriteLine("tick,us,alloc_bytes,gc0,gc1,gc2,gc_pause_us,jit_methods,jit_us,cars,rounds," + string.Join(",", SectionNames.Skip(1)) + ",other");
        string log = "";
        sc.Setup.Script!.Host.Logged += m => log += (log.Length > 0 ? " | " : "") + m;
        TickProbe.Timer = timer;
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        int gc0 = 0, gc1 = 0, gc2 = 0;
        var pauseAll = GC.GetTotalPauseDuration();
        double f = 1e6 / Stopwatch.Frequency;
        long cpu0 = proc.TotalProcessorTime.Ticks;
        var wall = Stopwatch.StartNew();
        for (int t = 0; t < ticks; t++)
        {
            log = "";
            var pause = GC.GetTotalPauseDuration();
            long jm = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true);
            var jt = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
            int c0 = GC.CollectionCount(0), c1 = GC.CollectionCount(1), c2 = GC.CollectionCount(2);
            sc.Tick();
            us[t] = (timer.Stop - timer.Start) * f;
            alloc[t] = timer.AllocEnd - timer.AllocStart;
            int d0 = GC.CollectionCount(0) - c0, d1 = GC.CollectionCount(1) - c1, d2 = GC.CollectionCount(2) - c2;
            double pauseUs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds * 1000;
            long jitN = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true) - jm;
            double jitUs = (System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - jt).TotalMilliseconds * 1000;
            gc0 += d0; gc1 += d1; gc2 += d2;
            double sum = 0;
            for (int s = 1; s < Sections; s++) { excl[s, t] = timer.Excl[s] * f; sum += excl[s, t]; }
            excl[0, t] = split ? System.Math.Max(0, us[t] - sum) : 0;      // what no section holds: the tick's own bookkeeping
            int cars = sc.Setup.World.Vehicles.Count(v => !v.Wreck), rounds = sc.Setup.Combat.Rounds.Count;
            csv?.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{t},{us[t]:0.0},{alloc[t]},{d0},{d1},{d2},{pauseUs:0},{jitN},{jitUs:0},{cars},{rounds},{string.Join(",", Enumerable.Range(1, Sections - 1).Select(s => excl[s, t].ToString("0.0", CultureInfo.InvariantCulture)))},{excl[0, t]:0.0}"));
            if (us[t] >= slowMs * 1000)
            {
                var parts = split ? string.Join(" ", Enumerable.Range(1, Sections - 1).Where(s => excl[s, t] >= 50).Select(s => $"{SectionNames[s]} {excl[s, t] / 1000:0.00}")) : "";
                slow.Add(string.Create(CultureInfo.InvariantCulture,
                    $"tick {t} ({t * rate.TickSeconds:0.0} s): {us[t] / 1000:0.00} ms; gc {d0}/{d1}/{d2} pause {pauseUs / 1000:0.00} ms; jit {jitN} methods {jitUs / 1000:0.00} ms; alloc {alloc[t] / 1024.0:0.0} kB; cars {cars} rounds {rounds}{(parts.Length > 0 ? "; " + parts : "")}{(log.Length > 0 ? "; log: " + log : "")}"));
            }
            if (sc.Burst is { } burst)
            {
                long b0 = Stopwatch.GetTimestamp();
                int ran = burst();
                if (ran > 0) burstUs.Add((Stopwatch.GetTimestamp() - b0) * f);
            }
        }
        double wallS = wall.Elapsed.TotalSeconds;
        double cpuS = (proc.TotalProcessorTime.Ticks - cpu0) / 1e7;
        TickProbe.Timer = null;
        double pauseTotal = (GC.GetTotalPauseDuration() - pauseAll).TotalMilliseconds;

        var sorted = (double[])us.Clone();
        Array.Sort(sorted);
        double P(double q) => sorted[System.Math.Min(sorted.Length - 1, (int)System.Math.Ceiling(q * sorted.Length) - 1)];
        var steady = alloc.Skip(System.Math.Min(alloc.Length - 1, rate.TicksFor(10))).ToArray();
        Array.Sort(steady);
        var inv = CultureInfo.InvariantCulture;
        var o = new StringBuilder();
        o.AppendLine(string.Create(inv, $"tick-bench {name} {rate.Label} Hz: {ticks} ticks ({minutes:0.#} min of game time), load {loadS:0.0} s, run {wallS:0.0} s wall, {cpuS:0.0} s cpu; affinity 0x{(long)proc.ProcessorAffinity:x}; split {(split ? "on" : "off")}; {Environment.ProcessorCount} processors"));
        o.AppendLine(string.Create(inv, $"  tick ms: mean {us.Average() / 1000:0.000}  median {P(0.5) / 1000:0.000}  p95 {P(0.95) / 1000:0.000}  p99 {P(0.99) / 1000:0.000}  p99.9 {P(0.999) / 1000:0.000}  max {sorted[^1] / 1000:0.000}"));
        o.AppendLine(string.Create(inv, $"  over 1 ms: {us.Count(x => x >= 1000)}  over 3 ms: {us.Count(x => x >= 3000)}  over 5 ms: {us.Count(x => x >= 5000)}"));
        o.AppendLine(string.Create(inv, $"  alloc a tick: mean {alloc.Average() / 1024:0.00} kB, steady (after 10 s) median {steady[steady.Length / 2] / 1024.0:0.00} kB, p99 {steady[(int)(steady.Length * 0.99)] / 1024.0:0.00} kB, max {alloc.Max() / 1024.0:0.0} kB; total {alloc.Sum() / 1048576.0:0.0} MB"));
        o.AppendLine(string.Create(inv, $"  gc during ticks: gen0 {gc0} gen1 {gc1} gen2 {gc2}; whole run gen0 {GC.CollectionCount(0) - g0} gen1 {GC.CollectionCount(1) - g1} gen2 {GC.CollectionCount(2) - g2}, pauses {pauseTotal:0.0} ms"));
        if (split)
        {
            o.Append("  split, mean us a tick:");
            for (int s = 1; s < Sections; s++) o.Append(string.Create(inv, $" {SectionNames[s]} {Enumerable.Range(0, ticks).Average(t => excl[s, t]):0.0}"));
            o.AppendLine(string.Create(inv, $" other {Enumerable.Range(0, ticks).Average(t => excl[0, t]):0.0}"));
        }
        if (burstUs.Count > 0)
        {
            var bs = burstUs.OrderBy(x => x).ToArray();
            o.AppendLine(string.Create(inv, $"  re-sim burst ({burstN} predicted ticks of one car, every {every} ticks): {bs.Length} bursts, median {bs[bs.Length / 2] / 1000:0.000} ms, p99 {bs[(int)(bs.Length * 0.99)] / 1000:0.000} ms, max {bs[^1] / 1000:0.000} ms"));
        }
        o.AppendLine($"  slow ticks (>= {slowMs} ms): {slow.Count}");
        foreach (var line in slow.Take(40)) o.AppendLine("    " + line);
        o.AppendLine(string.Create(inv, $"  last StateHash {sc.Session.StateHash():x16}"));
        Console.Write(o.ToString());
        return 0;
    }

    private static Scenario Build(GameInstall install, string name, SimRate rate, int burstN, int every)
    {
        if (name is "melee8" or "resim")
        {
            var host = new MeleeHost(new ServerOptions
            {
                Install = install.Root, Arena = "m01", Seed = 1, Bots = 8, MaxPlayers = 8, TimeLimit = 3600, Rate = rate,
            });
            var s = new Scenario { Session = host.Session, Setup = host.Setup, Tick = host.Step };
            if (name == "resim")
            {
                // a client's re-simulation: its own car rewound to the acknowledged tick and stepped over the inputs the server
                // has not applied (OwnCarPredictor.Reconcile), here a bot's car each time, from a capture that is put back after
                int pick = 0;
                s.Burst = () =>
                {
                    var session = host.Session;
                    if (session.Ticks % every != 0) return 0;
                    var cars = session.World.Vehicles;
                    var live = cars.Where(v => !v.Wreck && !v.IsEmplacement).ToList();
                    if (live.Count == 0) return 0;
                    var car = live[pick++ % live.Count];
                    var d = new Driver(car, 99);
                    var snap = session.Capture(d);
                    uint seed = session.World.Rand.Seed;
                    int start = session.Ticks - burstN;
                    for (int k = 0; k < burstN; k++) session.PredictTick(d, default, start + k, cars, damage: null);
                    session.Restore(snap);
                    session.World.Rand.Seed = seed;
                    return burstN;
                };
            }
            return s;
        }
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var msnPath = install.FindMission(name) ?? throw new FileNotFoundException(name);
        var w = new MissionWorld(vfs, asm, msnPath);
        var setup = MissionSetup.Build(vfs, w, asm, null, melee: null, rand: new I76.Sim.Rng.CrtRand(1));
        if (setup.Script is null) throw new InvalidOperationException($"{name}: no script");
        var session2 = new SimSession(setup, rate: rate);
        if (setup.World.Player is { } driven) session2.AddDriver(driven);
        CarInput[] none = [default];
        return new Scenario
        {
            Session = session2, Setup = setup,
            Tick = () =>
            {
                session2.Tick(none);
                // what the game's views take from the tick
                setup.Combat.Events.Clear();
                setup.Damage.Events.Clear();
            },
        };
    }
}
