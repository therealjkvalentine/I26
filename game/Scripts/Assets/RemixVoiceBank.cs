using Godot;
using I76.Formats.Audio;
using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Game.Assets;

/// <summary>
/// The spoken lines of a generated job (docs/remix-missions.md "Spoken lines"), rebuilt from the player's own clips when the job
/// loads: the clips are read on the main thread (the archive's reader is not shared across threads), the lines rendered on a
/// worker (SpliceRender: plain C#, no engine call), and kept in memory for the job; the radio asks for a line by its sound name
/// (AudioBank.Get). A line whose clips this machine lacks or whose checksum differs (another release, no Nitro Pack for a Nitro
/// voice) is skipped: the job letters its text all the same. Nothing the simulation sees depends on any of it (the radio's
/// length for a line is the table's).
/// </summary>
public static class RemixVoiceBank
{
    private static Task<Dictionary<string, double[]>>? _job;
    private static readonly Dictionary<string, AudioStreamWav?> Streams = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>I76_REMIX_VOICE_DUMP=DIR: each line as it is played written to DIR/&lt;id&gt;-game.wav (a scratch folder), to compare with the reference.</summary>
    private static readonly string? Dump = System.Environment.GetEnvironmentVariable("I76_REMIX_VOICE_DUMP") is { Length: > 0 } d ? d : null;

    /// <summary>The Nitro Pack beside a base-game install (its voices, squelch bursts and static bed), or null.</summary>
    public static Func<string, string?> FindNitro { get; set; } = _ => null;

    /// <summary>Starts building a job's lines (by sound name) from the running install; the last job's are dropped.</summary>
    public static void Prepare(AssetVfs vfs, IEnumerable<string> names)
    {
        Streams.Clear();
        var lines = names.Select(RemixVoice.Of).OfType<VoiceLine>().DistinctBy(l => l.Id).ToList();
        if (lines.Count == 0) { _job = null; return; }
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        AssetVfs? i76 = vfs.Install.IsNitro ? CompanionSounds.Vfs : vfs, nitro = null;
        if (!vfs.Install.IsNitro && FindNitro(vfs.Install.Root) is { } nd)
            try { nitro = new AssetVfs(new GameInstall(nd)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { GD.PrintErr($"[voice] Nitro Pack at {nd}: {ex.Message}"); }
        else if (vfs.Install.IsNitro) nitro = vfs;
        var src = new RemixVoice.Source(i76, nitro);
        // every clip read here, on the main thread; the worker only reads the source's cache
        var r = RemixVoice.Data.Radio;
        foreach (var l in lines) foreach (var p in l.Pieces) src.Clip(p.Game, p.Clip);
        foreach (var c in r.Bed.Sources) src.Clip(c[0].GetString()!, c[1].GetString()!);
        foreach (var c in new[] { r.Head, r.Tail }) if (SpliceRadio.Cut(c) is { } q) src.Clip(q.Game, q.Clip);
        double readMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _job = Task.Run(() =>
        {
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            var o = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            var skipped = new List<string>();
            foreach (var l in lines)
            {
                if (RemixVoice.WhyNot(l, src) is { } why) { skipped.Add($"{l.Id} ({why})"); continue; }
                if (RemixVoice.Render(l, src) is { } x) o[l.Sound] = x;
            }
            GD.Print($"[voice] built {o.Count} of {lines.Count} lines for the job in {System.Diagnostics.Stopwatch.GetElapsedTime(t1).TotalMilliseconds:0} ms (clips read in {readMs:0} ms; Nitro Pack {(src.HasNitro ? "present" : "absent: no squelch bursts")})"
                + (skipped.Count > 0 ? "; text only: " + string.Join(", ", skipped) : ""));
            return o;
        });
    }

    /// <summary>A line's stream by its sound name, or null (not built here). Waits for the build a little if it is still running.</summary>
    public static AudioStream? Stream(string name)
    {
        if (RemixVoice.Of(name) is not { } line) return null;
        if (Streams.TryGetValue(line.Sound, out var s)) return s;
        double[]? x = null;
        if (_job is { } job)
        {
            if (!job.IsCompleted) job.Wait(TimeSpan.FromSeconds(3));
            if (job.IsCompletedSuccessfully) job.Result.TryGetValue(line.Sound, out x);
        }
        if (x is null) { GD.Print($"[voice] {line.Id} not built: its text only"); return Streams[line.Sound] = null; }
        var pcm = SpliceRender.Pcm16(x);
        s = new AudioStreamWav { Data = pcm, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = SpliceRender.Rate, Stereo = false };
        GD.Print($"[voice] ready {line.Id} ({x.Length / (double)SpliceRender.Rate:0.00} s, {line.Speaker}): {line.Text}");
        if (Dump is { } dir)
            try { System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, line.Id + "-game.wav"), SpliceRender.Wav(x)); GD.Print($"[voice] wrote {line.Id}-game.wav"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PrintErr($"[voice] dump: {ex.Message}"); }
        return Streams[line.Sound] = s;
    }
}
