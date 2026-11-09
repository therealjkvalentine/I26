using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using I76.Sim.Session;

namespace I76.Game.Boot;

/// <summary>
/// The port's "Report a Bug" (the Esc menu's Options Menu pad, Ctrl+F11; docs/playing.md "Reporting a bug"): a folder
/// <c>bug-reports/&lt;stamp&gt;/</c> in the user folder with report.txt (the build, the system, the game, the car, the
/// settings, the texture packs, a network game's roster), state.sav (the session's snapshot, docs/savestate.md), shell.cfg
/// (a copy), screenshot.png, log.txt (the last lines of this run's output) and inputs.csv (the last seconds of the player's
/// inputs), then the folder zipped beside it as <c>i26-bug-&lt;stamp&gt;.zip</c>. Nothing personal: every path that names
/// the user (the user folder, the home folder, the user name, the install's and the program's folders) is blanked in every
/// text file. Engine-free, so the tests write and read reports; <c>MainBugReport.cs</c> gathers the game's part.
/// </summary>
public static class BugReport
{
    public const string Folder = "bug-reports";
    public const string ReportFile = "report.txt", StateFile = "state.sav", SettingsFile = "shell.cfg", ScreenshotFile = "screenshot.png",
        LogFile = "log.txt", InputsFile = "inputs.csv";

    /// <summary>The sections of report.txt, in order; every one is written (an empty one says so).</summary>
    public static readonly string[] Sections = ["Build", "System", "Game", "Car", "State", "Settings", "Texture packs", "Screenshot", "Log", "Inputs", "Network"];

    /// <summary>What a report is made of: text per section from the gatherer, and the attachments as bytes or lines.</summary>
    public sealed class Data
    {
        public DateTime Stamp { get; init; } = DateTime.Now;
        public string Build { get; init; } = "";
        public string System { get; init; } = "";
        public string Game { get; init; } = "";
        public string Car { get; init; } = "";
        public string Settings { get; init; } = "";
        public string Packs { get; init; } = "";
        public string Network { get; init; } = "";
        /// <summary>The session's snapshot, or null with <see cref="StateNote"/> saying why not.</summary>
        public byte[]? Snapshot { get; init; }
        public int SnapshotTick { get; init; }
        public string? StateNote { get; init; }
        /// <summary>shell.cfg's text, or null when there is none.</summary>
        public string? SettingsFile { get; init; }
        /// <summary>The frame as a PNG, or null with <see cref="ScreenshotNote"/>.</summary>
        public byte[]? Screenshot { get; init; }
        public string? ScreenshotNote { get; init; }
        public IReadOnlyList<string> LogLines { get; init; } = [];
        public string? LogNote { get; init; }
        /// <summary>inputs.csv's lines (the input trace format, docs/dev-hooks.md), or empty with <see cref="InputsNote"/>.</summary>
        public IReadOnlyList<string> InputLines { get; init; } = [];
        public string? InputsNote { get; init; }
        /// <summary>Paths and names blanked in every text file: the user folder, the home folder, the user name, the install's folder, the program's.</summary>
        public IReadOnlyList<(string Secret, string Mask)> Redact { get; init; } = [];
    }

    /// <summary>The folder's name for a report written at <paramref name="at"/>, and the zip's.</summary>
    public static string StampOf(DateTime at) => at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    public static string ZipName(DateTime at) => $"i26-bug-{StampOf(at)}.zip";

    /// <summary>
    /// Writes the report's folder under <paramref name="root"/> (the user folder's bug-reports) and zips it beside the folder.
    /// Returns the folder and the zip. Throws on an I/O failure: the caller says so.
    /// </summary>
    public static (string Dir, string Zip) Write(string root, Data d)
    {
        string dir = Path.Combine(root, StampOf(d.Stamp));
        Directory.CreateDirectory(dir);
        string Clean(string s) => Redact(s, d.Redact);
        File.WriteAllText(Path.Combine(dir, ReportFile), Clean(Text(d)), new UTF8Encoding(false));
        if (d.Snapshot is { } snap) File.WriteAllBytes(Path.Combine(dir, StateFile), snap);
        if (d.SettingsFile is { } cfg) File.WriteAllText(Path.Combine(dir, SettingsFile), Clean(cfg), new UTF8Encoding(false));
        if (d.Screenshot is { } png) File.WriteAllBytes(Path.Combine(dir, ScreenshotFile), png);
        if (d.LogLines.Count > 0) File.WriteAllLines(Path.Combine(dir, LogFile), d.LogLines.Select(Clean), new UTF8Encoding(false));
        if (d.InputLines.Count > 0) File.WriteAllLines(Path.Combine(dir, InputsFile), d.InputLines.Select(Clean), new UTF8Encoding(false));
        string zip = Path.Combine(root, ZipName(d.Stamp));
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(dir, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
        return (dir, zip);
    }

    /// <summary>report.txt: a heading, then every section in <see cref="Sections"/>' order, each "== Title ==" over its lines.</summary>
    public static string Text(Data d)
    {
        var sb = new StringBuilder();
        sb.Append("I26 bug report, ").Append(d.Stamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" (local time)\n");
        sb.Append("Drag the zip this came in into Discord or a GitHub issue, with a line on what went wrong.\n");
        sb.Append("Nothing personal is in it: paths that name the user are blanked. Files: ").Append(string.Join(", ", Files(d))).Append('\n');
        foreach (var title in Sections)
        {
            sb.Append("\n== ").Append(title).Append(" ==\n");
            string body = title switch
            {
                "Build" => d.Build,
                "System" => d.System,
                "Game" => d.Game,
                "Car" => d.Car,
                "State" => d.Snapshot is { } s
                    ? $"{StateFile}: the session's snapshot at tick {d.SnapshotTick} ({s.Length / 1024.0:0.#} KB; docs/savestate.md).\nReopen it: i26 -- --bug-report-open <this zip> --dev (with --1997 for the 1997 look)."
                    : d.StateNote ?? "",
                "Settings" => (d.SettingsFile is not null ? $"{SettingsFile}: a copy of the settings file.\n" : "no settings file.\n") + d.Settings,
                "Texture packs" => d.Packs,
                "Screenshot" => d.Screenshot is not null ? $"{ScreenshotFile}: {d.ScreenshotNote ?? "the frame"}" : d.ScreenshotNote ?? "",
                "Log" => d.LogLines.Count > 0 ? $"{LogFile}: the last {d.LogLines.Count} lines of this run's output.{(d.LogNote is { } ln ? " " + ln : "")}" : d.LogNote ?? "",
                "Inputs" => d.InputLines.Count > 0 ? $"{InputsFile}: the player's inputs, the last {Math.Max(0, d.InputLines.Count - 2)} ticks (the input trace format, docs/dev-hooks.md).{(d.InputsNote is { } nn ? " " + nn : "")}" : d.InputsNote ?? "",
                "Network" => d.Network,
                _ => "",
            };
            body = body.Trim();
            sb.Append(body.Length > 0 ? body : "(none)").Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>The files the report's folder holds.</summary>
    public static IEnumerable<string> Files(Data d)
    {
        yield return ReportFile;
        if (d.Snapshot is not null) yield return StateFile;
        if (d.SettingsFile is not null) yield return SettingsFile;
        if (d.Screenshot is not null) yield return ScreenshotFile;
        if (d.LogLines.Count > 0) yield return LogFile;
        if (d.InputLines.Count > 0) yield return InputsFile;
    }

    /// <summary>
    /// Every secret blanked: a path in either slash form (and with a trailing separator or not), a bare user name wherever it
    /// stands as a path segment or alone between non-letters; case does not matter.
    /// </summary>
    public static string Redact(string text, IEnumerable<(string Secret, string Mask)> secrets)
    {
        foreach (var (secret, mask) in secrets.Where(s => s.Secret.Length > 0).OrderByDescending(s => s.Secret.Length))
        {
            if (secret.IndexOfAny(['\\', '/']) >= 0)
            {
                string trimmed = secret.TrimEnd('\\', '/');
                if (trimmed.Length == 0) continue;
                // the same folder written with either separator
                string pattern = string.Join(@"[\\/]+", trimmed.Split(new[] { '\\', '/' },StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
                text = Regex.Replace(text, pattern, mask, RegexOptions.IgnoreCase);
            }
            else
                text = Regex.Replace(text, @"(?<![\p{L}\p{N}_.-])" + Regex.Escape(secret) + @"(?![\p{L}\p{N}_.-])", mask, RegexOptions.IgnoreCase);
        }
        return text;
    }

    /// <summary>The report's snapshot read back from its zip (or its folder), for <c>--bug-report-open</c>; null when the report has none.</summary>
    public static byte[]? ReadState(string zipOrDir)
    {
        if (Directory.Exists(zipOrDir))
        {
            string f = Path.Combine(zipOrDir, StateFile);
            return File.Exists(f) ? File.ReadAllBytes(f) : null;
        }
        using var zip = ZipFile.OpenRead(zipOrDir);
        if (zip.GetEntry(StateFile) is not { } e) return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>The last N lines of something (the engine's output, for the report's log.txt): oldest first when read.</summary>
public sealed class LineRing(int capacity)
{
    private readonly string[] _lines = new string[Math.Max(1, capacity)];
    private int _next, _count;
    private readonly object _lock = new();

    public int Capacity => _lines.Length;
    public int Count { get { lock (_lock) return _count; } }

    public void Add(string line)
    {
        lock (_lock)
        {
            _lines[_next] = line;
            _next = (_next + 1) % _lines.Length;
            if (_count < _lines.Length) _count++;
        }
    }

    public List<string> Lines()
    {
        lock (_lock)
        {
            var list = new List<string>(_count);
            int start = (_next - _count + _lines.Length) % _lines.Length;
            for (int i = 0; i < _count; i++) list.Add(_lines[(start + i) % _lines.Length]);
            return list;
        }
    }
}

/// <summary>
/// The player's inputs of the last seconds (the report's inputs.csv), a tick each, with the run's input trace header so a
/// reader knows the mission, car, seed and rate; [port] bug_report_inputs=false leaves the ring out.
/// </summary>
public sealed class InputRing(InputTrace header, int capacity)
{
    /// <summary>How many seconds of inputs a report keeps (the ring's capacity is this many ticks at the session's rate).</summary>
    public const double Seconds = 10;
    private readonly (int Tick, CarInput Input)[] _ring = new (int, CarInput)[Math.Max(1, capacity)];
    private int _next, _count;

    public InputTrace Header { get; } = header;
    public int Capacity => _ring.Length;
    public int Count => _count;

    public void Add(int tick, CarInput input)
    {
        _ring[_next] = (tick, input);
        _next = (_next + 1) % _ring.Length;
        if (_count < _ring.Length) _count++;
    }

    /// <summary>The header line, the column line, then a row a tick, oldest first (InputTrace.Read takes them; the ticks before the first row read as nothing held).</summary>
    public List<string> Rows()
    {
        var rows = new List<string>(_count + 2) { Header.HeaderLine(), InputTrace.Columns };
        int start = (_next - _count + _ring.Length) % _ring.Length;
        for (int i = 0; i < _count; i++)
        {
            var (tick, input) = _ring[(start + i) % _ring.Length];
            rows.Add(InputTrace.Row(tick, input, null));
        }
        return rows;
    }
}
