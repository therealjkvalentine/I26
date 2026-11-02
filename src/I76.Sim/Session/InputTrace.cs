using System.Globalization;
using I76.Sim.Vehicles;

namespace I76.Sim.Session;

/// <summary>
/// The player's <see cref="CarInput"/> for every tick of a mission, with what the run was started from (mission, car,
/// rand seed, tick rate, melee roster): a recording (I76_INPUT_RECORD, <c>i76 run --record</c>) that drives the same car
/// again tick for tick (I76_INPUT, <c>--replay</c>). The sim is deterministic, so a replay on the same build runs the same
/// game; the optional hash column is <see cref="SimSession.StateHash"/> after each tick, to find the first tick a replay
/// leaves it. An input source only: no sim state. The format is docs/dev-hooks.md's "Input traces".
/// </summary>
public sealed class InputTrace
{
    public const string Magic = "I76_INPUT";
    public const int Version = 1;
    public const string Columns = "tick,buttons,commands,steer,throttle,hash";

    public string? Mission;
    public string? Car;
    public uint Seed = 1;
    public int Rate = SimRate.Default.Label;
    public IReadOnlyList<string>? Melee;
    // what else reaches the sim at the start, where the recorder knew it (null: not recorded): the build, the install's identity
    // (SessionBuild.InstallIdentity), the Play Options flags and Difficulty Level, Modern Physics, the AI's mode (--ai)
    public string? Build;
    public ulong? Install;
    public int? Options, Difficulty;
    public bool? ModernPhysics;
    public string? Ai;
    /// <summary>The Developer panel's actions during the recording ("# dev" rows): a change no input made, so a replay leaves the recording there.</summary>
    public List<(int Tick, string Action)> Dev { get; } = [];
    public List<CarInput> Inputs { get; } = [];
    /// <summary>The state hash after each tick, where it was recorded.</summary>
    public List<ulong?> Hashes { get; } = [];

    public int Count => Inputs.Count;

    /// <summary>The input of tick <paramref name="tick"/>; past the end, none.</summary>
    public CarInput At(int tick) => tick >= 0 && tick < Inputs.Count ? Inputs[tick] : default;

    public ulong? HashAt(int tick) => tick >= 0 && tick < Hashes.Count ? Hashes[tick] : null;

    public void Add(CarInput input, ulong? hash = null) { Inputs.Add(input); Hashes.Add(hash); }

    /// <summary>
    /// The first line: <c># I76_INPUT 1 mission=t01 car=x.vcf seed=N rate=20 [melee=a.vcf+b.vcf] [build=V] [install=HEX] [options=0xN]
    /// [difficulty=N] [physics=original|modern] [ai=MODE]</c>; a field not known is left out, and a reader skips a field it does not know.
    /// </summary>
    public string HeaderLine()
    {
        var s = new System.Text.StringBuilder($"# {Magic} {Version} mission={Mission ?? ""} car={Car ?? ""} seed={Seed.ToString(CultureInfo.InvariantCulture)} rate={Rate.ToString(CultureInfo.InvariantCulture)}");
        if (Melee is { Count: > 0 } m) s.Append(" melee=").Append(string.Join('+', m));
        if (Build is { Length: > 0 } b) s.Append(" build=").Append(Word(b));
        if (Install is { } i) s.Append(" install=").Append(i.ToString("x16", CultureInfo.InvariantCulture));
        if (Options is { } o) s.Append(" options=0x").Append(o.ToString("x", CultureInfo.InvariantCulture));
        if (Difficulty is { } d) s.Append(" difficulty=").Append(d.ToString(CultureInfo.InvariantCulture));
        if (ModernPhysics is { } mp) s.Append(" physics=").Append(mp ? "modern" : "original");
        if (Ai is { Length: > 0 } ai) s.Append(" ai=").Append(Word(ai));
        return s.ToString();
    }

    private static string Word(string s) => s.Trim().Replace(' ', '_');

    /// <summary>
    /// What differs between the run a recording was made in and this one (<paramref name="now"/>), field by field, where both name it:
    /// a replay under any of these runs another game from tick 0.
    /// </summary>
    public IEnumerable<string> Differences(InputTrace now)
    {
        static bool Same(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        if (!Same(Mission, now.Mission)) yield return $"mission {Mission} (now {now.Mission})";
        if (!Same(Car, now.Car)) yield return $"car {Car ?? "(the mission's)"} (now {now.Car ?? "(the mission's)"})";
        if (!Same(string.Join('+', Melee ?? []), string.Join('+', now.Melee ?? []))) yield return $"melee {string.Join(", ", Melee ?? [])} (now {string.Join(", ", now.Melee ?? [])})";
        if (Build is not null && now.Build is not null && !Same(Word(Build), Word(now.Build))) yield return $"build {Build} (now {now.Build})";
        if (Install is { } i && now.Install is { } ni && i != ni) yield return $"install {i:x16} (now {ni:x16})";
        if (Options is { } o && now.Options is { } no && o != no) yield return $"Play Options 0x{o:x} (now 0x{no:x})";
        if (Difficulty is { } d && now.Difficulty is { } nd && d != nd) yield return $"Difficulty {d} (now {nd})";
        if (ModernPhysics is { } p && now.ModernPhysics is { } np && p != np) yield return $"physics {(p ? "modern" : "original")} (now {(np ? "modern" : "original")})";
        if (Ai is not null && now.Ai is not null && !Same(Word(Ai), Word(now.Ai))) yield return $"ai {Ai} (now {now.Ai})";
    }

    /// <summary>A Developer panel action's row before tick <paramref name="tick"/>: <c># dev restore tick 300</c> (a comment to an older reader).</summary>
    public static string DevRow(int tick, string action) =>
        string.Create(CultureInfo.InvariantCulture, $"# dev {Word(action.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "action")} tick {tick}");

    public static string Row(int tick, CarInput i, ulong? hash) => string.Create(CultureInfo.InvariantCulture,
        $"{tick},0x{(uint)i.Buttons:x},0x{(uint)i.Commands:x},{i.Steer},{i.Throttle},{(hash is { } h ? h.ToString("x16") : "")}");

    /// <summary>The header, the column line, then one row a tick.</summary>
    public void Write(TextWriter w)
    {
        w.WriteLine(HeaderLine());
        w.WriteLine(Columns);
        int dev = 0;
        for (int t = 0; t < Inputs.Count; t++)
        {
            for (; dev < Dev.Count && Dev[dev].Tick <= t; dev++) w.WriteLine(DevRow(Dev[dev].Tick, Dev[dev].Action));
            w.WriteLine(Row(t, Inputs[t], Hashes[t]));
        }
        for (; dev < Dev.Count; dev++) w.WriteLine(DevRow(Dev[dev].Tick, Dev[dev].Action));
    }

    public static InputTrace Read(TextReader r)
    {
        var trace = new InputTrace();
        string? line = r.ReadLine() ?? throw new InvalidDataException("empty input trace");
        var head = line.TrimStart('#').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!line.StartsWith('#') || head.Length < 2 || head[0] != Magic) throw new InvalidDataException($"not an input trace: {line}");
        if (!int.TryParse(head[1], CultureInfo.InvariantCulture, out int version) || version != Version) throw new InvalidDataException($"input trace version {head[1]}, this build reads {Version}");
        foreach (var kv in head.Skip(2).Select(f => f.Split('=', 2)).Where(f => f.Length == 2))
        {
            string v = kv[1];
            switch (kv[0])
            {
                case "mission": trace.Mission = v.Length > 0 ? v : null; break;
                case "car": trace.Car = v.Length > 0 ? v : null; break;
                case "seed": trace.Seed = uint.Parse(v, CultureInfo.InvariantCulture); break;
                case "rate": trace.Rate = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "melee": trace.Melee = v.Split('+', StringSplitOptions.RemoveEmptyEntries); break;
                case "build": trace.Build = v; break;
                case "install": trace.Install = ulong.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id) ? id : null; break;
                case "options": trace.Options = (int)ParseUInt(v); break;
                case "difficulty": trace.Difficulty = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "physics": trace.ModernPhysics = v == "modern"; break;
                case "ai": trace.Ai = v; break;
            }
        }
        while ((line = r.ReadLine()) is not null)
        {
            if (line.StartsWith("# dev ", StringComparison.Ordinal))
            {
                var d = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (d.Length >= 5 && d[3] == "tick" && int.TryParse(d[4], CultureInfo.InvariantCulture, out int at)) trace.Dev.Add((at, d[2]));
                continue;
            }
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("tick", StringComparison.Ordinal)) continue;
            var f = line.Split(',');
            if (f.Length < 5) throw new InvalidDataException($"input trace row: {line}");
            int tick = int.Parse(f[0], CultureInfo.InvariantCulture);
            if (tick < trace.Inputs.Count) throw new InvalidDataException($"input trace: tick {tick} out of order");
            while (trace.Inputs.Count < tick) trace.Add(default);   // a missing tick holds nothing down
            var input = new CarInput
            {
                Buttons = (CarButtons)ParseUInt(f[1]),
                Commands = (CarCommands)ParseUInt(f[2]),
                Steer = short.Parse(f[3], CultureInfo.InvariantCulture),
                Throttle = short.Parse(f[4], CultureInfo.InvariantCulture),
            };
            trace.Add(input, f.Length > 5 && f[5].Length > 0 ? ulong.Parse(f[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null);
        }
        return trace;
    }

    private static uint ParseUInt(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? uint.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
        : uint.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>
/// A recording written as it runs (I76_INPUT_RECORD): the header, the column line, then a row each tick and a "# dev" row for
/// each Developer panel action, each flushed as written, so a run that crashes or is killed leaves its trace up to that tick.
/// </summary>
public sealed class InputTraceWriter(TextWriter w) : IDisposable
{
    private bool _started;

    /// <summary>The header and the column line (once, before the first row).</summary>
    public void Start(InputTrace header)
    {
        if (_started) return;
        _started = true;
        w.WriteLine(header.HeaderLine());
        w.WriteLine(InputTrace.Columns);
        w.Flush();
    }

    public void Tick(int tick, CarInput input, ulong? hash) { w.WriteLine(InputTrace.Row(tick, input, hash)); w.Flush(); }

    public void Dev(int tick, string action) { w.WriteLine(InputTrace.DevRow(tick, action)); w.Flush(); }

    public void Dispose() => w.Dispose();
}

/// <summary>
/// One row a tick of a car's motion (I76_MOTION_CSV, <c>i76 run --motion</c>): position, attitude, velocity, body rates,
/// acceleration and the driver's controls, in the game's axes and units (x right, y up, z forward; angles in radians,
/// heading from +z toward +x). For plotting a run or laying it over a trace of the original's.
/// </summary>
public static class MotionTrace
{
    public const string Columns = "tick,t,x,y,z,heading,pitch,roll,vx,vy,vz,speed,pitch_rate,yaw_rate,roll_rate,ax,ay,az,steer,throttle,gear,rpm,flags";

    public static string Row(int tick, float seconds, Vehicle v)
    {
        var f = v.Rot.Forward;
        float heading = MathF.Atan2(f.X, f.Z), pitch = MathF.Asin(System.Math.Clamp(f.Y, -1f, 1f)), roll = MathF.Asin(System.Math.Clamp(v.Rot.Right.Y, -1f, 1f));
        return string.Create(CultureInfo.InvariantCulture,
            $"{tick},{seconds:0.######},{v.Pos.X:0.####},{v.Pos.Y:0.####},{v.Pos.Z:0.####},{heading:0.#####},{pitch:0.#####},{roll:0.#####},"
            + $"{v.Vel.X:0.####},{v.Vel.Y:0.####},{v.Vel.Z:0.####},{v.Speed:0.####},{v.AngVel.X:0.#####},{v.AngVel.Y:0.#####},{v.AngVel.Z:0.#####},"
            + $"{v.Accel.X:0.####},{v.Accel.Y:0.####},{v.Accel.Z:0.####},{v.Steer:0.####},{v.Throttle:0.####},{v.Engine.Gear},{v.Engine.Rpm:0.#},0x{(uint)v.Flags:x}");
    }
}
