using System.Globalization;
using I76.Formats.Audio;
using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Cli;

/// <summary>
/// The spoken lines of generated jobs (docs/remix-missions.md "Spoken lines"):
///   i76 remix lines [--nitro DIR]                 every line: voice, rating, moments, needs, and whether this install builds it
///   i76 remix say ID [--nitro DIR] --out F.wav    one line rendered (with the radio) as a WAV file
///   i76 remix voices [--seeds A-B] [--out FILE]   rolled jobs' radio moments, the line each chose, and the false-statement check
/// --nitro (or I76_NITRO_INSTALL) names a Nitro Pack install beside the base game: its voices, the squelch bursts and the static bed.
/// </summary>
internal static class RemixVoiceCommand
{
    public static int Run(GameInstall install, List<string> argv)
    {
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        var nitroDir = Opt("--nitro") ?? Environment.GetEnvironmentVariable("I76_NITRO_INSTALL");
        AssetVfs? nitro = nitroDir is { Length: > 0 } && Directory.Exists(nitroDir) ? new AssetVfs(new GameInstall(nitroDir)) : null;
        var i76 = install.IsNitro ? null : new AssetVfs(install);
        switch (argv[1])
        {
            case "lines":
            {
                var without = new RemixVoice.Source(i76, null);
                var with = nitro is null ? null : new RemixVoice.Source(i76, nitro);
                int a = 0, b = 0, used = 0;
                foreach (var l in RemixVoice.Lines)
                {
                    string? w0 = RemixVoice.WhyNot(l, without), w1 = with is null ? "no Nitro Pack given" : RemixVoice.WhyNot(l, with);
                    if (w0 is null) a++;
                    if (w1 is null) b++;
                    if (l.Unused is null) used++;
                    string what = l.Unused is null ? $"{string.Join(",", l.Moments)} [{string.Join(" ", l.Needs)}]" : $"unused: {l.Unused}";
                    Console.WriteLine($"{l.Id,-11} {l.Sound,-8} {l.Voice,-6} {l.Rating,-4} {(l.Nitro ? "N" : "-")} {RemixVoice.Seconds(l),5:0.00}s  base:{(w0 is null ? "yes" : "no")} +nitro:{(w1 is null ? "yes" : "no")}  {what}  \"{l.Text}\"{(w1 is null ? "" : $"  ({w1})")}");
                }
                Console.WriteLine($"{RemixVoice.Lines.Count} lines, {used} said by generated jobs; build on this install: {a} without the Nitro Pack, {(with is null ? "? (no Nitro Pack given)" : b.ToString(CultureInfo.InvariantCulture))} with it");
                if (argv.Contains("--crc"))
                    foreach (var key in RemixVoice.Data.Clips.Keys)
                    {
                        var parts = key.Split(':');
                        var vfs = parts[0] == "nitro" ? nitro : i76;
                        if (vfs is not null && SoundFiles.Read(vfs, parts[1] + ".wav") is { } p) Console.WriteLine($"crc {key} {I76.Formats.Installers.Crc32.Compute(p.Data)}");
                    }
                // the table's source clips as 8-bit WAVs under DIR/<game>/<stem>.wav (for the reference renderer, in a scratch folder: never the repo)
                if (Opt("--dump-clips") is { } dump)
                    foreach (var key in RemixVoice.Data.Clips.Keys)
                    {
                        var parts = key.Split(':');
                        var vfs = parts[0] == "nitro" ? nitro : i76;
                        if (vfs is null || SoundFiles.Read(vfs, parts[1] + ".wav") is not { } p) continue;
                        Directory.CreateDirectory(Path.Combine(dump, parts[0]));
                        using var f = File.Create(Path.Combine(dump, parts[0], parts[1] + ".wav"));
                        var w = new BinaryWriter(f);
                        w.Write("RIFF"u8); w.Write(36 + p.Data.Length); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
                        w.Write(p.Rate); w.Write(p.Rate); w.Write((short)1); w.Write((short)8); w.Write("data"u8); w.Write(p.Data.Length); w.Write(p.Data);
                    }
                return 0;
            }
            case "say":
            {
                if (argv.Count < 3 || RemixVoice.ById(argv[2]) is not { } l || Opt("--out") is not { } outFile) { Console.Error.WriteLine("usage: i76 remix say ID [--nitro DIR] --out FILE.wav"); return 2; }
                var src = new RemixVoice.Source(i76, nitro);
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                double[]? x = argv.Contains("--plain") ? SpliceRender.Plain(l.Pieces, l.Total, l.Gain, src.Clip) : RemixVoice.Render(l, src);
                if (x is null) { Console.Error.WriteLine($"{l.Id}: cannot be built here ({RemixVoice.WhyNot(l, src)})"); return 1; }
                File.WriteAllBytes(outFile, SpliceRender.Wav(x));
                Console.WriteLine($"wrote {outFile}: {l.Id} \"{l.Text}\" {x.Length / (double)SpliceRender.Rate:0.00} s in {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms");
                return 0;
            }
            case "voices":
            {
                var (s0, s1) = Opt("--seeds") is { } r && r.Split('-') is [var x0, var x1] ? (int.Parse(x0, CultureInfo.InvariantCulture), int.Parse(x1, CultureInfo.InvariantCulture)) : (1, 300);
                var tables = RemixNodes.Builtin();
                var o = new System.Text.StringBuilder();
                int jobs = 0, moments = 0, spoken = 0, problems = 0;
                var byLine = new Dictionary<string, int>();
                var byMap = new SortedDictionary<string, int>();
                for (int seed = s0; seed <= s1; seed++)
                {
                    // across every map: the seed's job on map seed mod 16, so 300 seeds visit each map about 19 times
                    var map = tables[(seed - s0) % tables.Count];
                    RemixSpec spec;
                    try { spec = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Tables = tables, Map = map.Base, Challenge = 1.0 }); }
                    catch (RemixError ex) { o.AppendLine($"seed {seed} {map.Base}: {ex.Message}"); continue; }
                    jobs++;
                    byMap[map.Base] = byMap.GetValueOrDefault(map.Base) + 1;
                    var bad = RemixVoiceCheck.Check(spec, map);
                    problems += bad.Count;
                    foreach (var ch in spec.Voice)
                    {
                        moments++;
                        var line = ch.Line is null ? null : RemixVoice.ById(ch.Line);
                        if (line is not null) { spoken++; byLine[line.Id] = byLine.GetValueOrDefault(line.Id) + 1; }
                        o.AppendLine($"{seed}\t{map.Base}\t{spec.Objective.Template}\t{ch.Moment}\t{string.Join(" ", ch.Facts.Order())}\t{line?.Id ?? "-"}\t{(line is null ? "" : $"{line.Speaker}: {line.Text}")}");
                    }
                    foreach (var b in bad) o.AppendLine($"PROBLEM seed {seed} {map.Base}: {b}");
                }
                var summary = $"{jobs} jobs on {byMap.Count} maps; {moments} radio moments, {spoken} with a spoken line ({100.0 * spoken / Math.Max(1, moments):0} %), {byLine.Count} distinct lines; false-statement check: {problems} problems";
                o.AppendLine(summary);
                o.AppendLine("lines by use: " + string.Join(", ", byLine.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
                if (Opt("--out") is { } f) { File.WriteAllText(f, o.ToString()); Console.WriteLine($"wrote {f}"); }
                else Console.Write(o.ToString());
                Console.WriteLine(summary);
                return problems == 0 ? 0 : 1;
            }
        }
        return 2;
    }
}
