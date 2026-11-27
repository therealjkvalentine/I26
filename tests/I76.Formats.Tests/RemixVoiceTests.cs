using I76.Formats.Audio;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// The spoken lines of generated jobs (docs/remix-missions.md "Spoken lines"): the table, the renderer, and that a job never says
/// something untrue of it.
/// </summary>
public class RemixVoiceTests
{
    [Fact]
    public void TheTableHoldsOurLinesAndNumbersOnly()
    {
        var lines = RemixVoice.Lines;
        Assert.Equal(116, lines.Count);
        Assert.Equal(lines.Count, lines.Select(l => l.Sound).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var l in lines)
        {
            Assert.True(l.Sound.Length + 4 <= 15, l.Sound);   // the CB queue keeps 15 characters, ".wav" included
            Assert.Same(l, RemixVoice.Of(l.Sound + ".WAV"));
            Assert.Contains(l.Voice, new[] { "taurus", "jade", "groove" });
            Assert.True(l.Unused is not null || l.Moments.Count > 0, l.Id);
            Assert.All(l.Moments, m => Assert.Contains(m, RemixVoice.Moments));
            Assert.NotEmpty(l.Pieces);
            Assert.All(l.Pieces, p => Assert.True(RemixVoice.Data.Clips.ContainsKey(p.Game + ":" + p.Clip), p.Clip));
            Assert.True(RemixVoice.Seconds(l) > l.Total / 11025f, l.Id);
        }
        Assert.Null(RemixVoice.Of("gdsgc32.wav"));
        Assert.Equal(lines.Count(l => l.Pieces.Any(p => p.Game == "nitro")), lines.Count(l => l.Nitro));
    }

    [Fact]
    public void TheStretchKeepsThePitchAndMakesTheLength()
    {
        // 150 Hz for half a second at x0.9: as long as asked, and as many zero crossings a second
        var x = Enumerable.Range(0, 5512).Select(i => Math.Sin(2 * Math.PI * 150 * i / 11025.0) * 0.5).ToArray();
        var y = SpliceRender.Wsola(x, 0.9);
        Assert.Equal((int)Math.Round(x.Length * 0.9), y.Length);
        static double Rate(double[] s) { int n = 0; for (int i = 400; i < s.Length - 400; i++) if (s[i - 1] < 0 && s[i] >= 0) n++; return n / ((s.Length - 800) / 11025.0); }
        Assert.InRange(Rate(y), 145, 155);
    }

    /// <summary>
    /// Over 300 jobs on all sixteen maps every line chosen is true of its job (RemixVoiceCheck reads the description itself and the
    /// line's words), and most radio moments get a line.
    /// </summary>
    [Fact]
    public void NoRolledJobSaysSomethingUntrue()
    {
        var tables = RemixNodes.Builtin();
        int said = 0, moments = 0;
        var problems = new List<string>();
        for (int seed = 1; seed <= 300; seed++)
        {
            var map = tables[(seed - 1) % tables.Count];
            var spec = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Tables = tables, Map = map.Base });
            problems.AddRange(RemixVoiceCheck.Check(spec, map).Select(p => $"seed {seed} {map.Base}: {p}"));
            moments += spec.Voice.Count;
            said += spec.Voice.Count(v => v.Line is not null);
            // no line twice in a job, and each said line's sound is in the radio
            var ids = spec.Voice.Where(v => v.Line is not null).Select(v => v.Line!).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            var all = spec.Radio.All.Concat(spec.Objective.Reinforce.Select(r => r.Say).OfType<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(ids, id => Assert.Contains(RemixVoice.ById(id)!.Sound, all));
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
        Assert.True(said > moments * 0.8, $"{said} of {moments}");
    }

    /// <summary>The check catches a line that names something the job lacks (the truck stop on a map with none).</summary>
    [Fact]
    public void TheCheckCatchesAFalseLine()
    {
        var map = RemixNodes.Builtin().First(t => t.Base == "t07");
        var spec = RemixDirector.Roll(5, new RemixDirector.JobAsk { Map = "t07", Template = "deliver" });
        spec.Voice.Add(new VoiceChoice("go", new HashSet<string> { "place:truck stop" }, "taurus-05"));
        Assert.Contains(RemixVoiceCheck.Check(spec, map), p => p.Contains("taurus-05"));
    }

    [Fact]
    public void TheSameSeedChoosesTheSameLines()
    {
        var a = RemixDirector.Roll(77, new RemixDirector.JobAsk { Map = "t14" });
        var b = RemixDirector.Roll(77, new RemixDirector.JobAsk { Map = "t14" });
        Assert.Equal(a.Voice.Select(v => v.Line), b.Voice.Select(v => v.Line));
    }

    [InstallFact]
    public void WithoutTheNitroPackItsVoicesAreSkippedAndTheBaseLinesBuildWithoutBursts()
    {
        var src = new RemixVoice.Source(TestInstall.Vfs, null);
        Assert.Equal(RemixVoice.Lines.Count(l => !l.Nitro), RemixVoice.Lines.Count(l => RemixVoice.WhyNot(l, src) is null));
        Assert.Contains("no Nitro Pack", RemixVoice.WhyNot(RemixVoice.ById("taurus-44")!, src));
        var l = RemixVoice.ById("taurus-108")!;
        var x = RemixVoice.Render(l, src)!;
        Assert.Equal(l.Total, x.Length);   // no squelch bursts and pads
        Assert.True(x.Max(Math.Abs) > 0.3);
    }

    [NitroFact]
    public void AnotherReleasesClipIsSkipped()
    {
        // the Nitro Pack's files where the base game's are wanted: no such clip, or a different one (its checksum)
        var src = new RemixVoice.Source(new AssetVfs(new GameInstall(NitroFactAttribute.Dir!)), null);
        Assert.NotNull(RemixVoice.WhyNot(RemixVoice.ById("taurus-108")!, src));
        Assert.Null(RemixVoice.Render(RemixVoice.ById("taurus-108")!, src));
    }

    /// <summary>
    /// The C# renderer against the splice work's reference renderer (render_recipe.py), lines of each voice including stretched and
    /// Nitro (matched) ones. I76_VOICE_REF names a scratch folder of its renders (ID-plain.wav, ID-radio.wav: never in the repository).
    /// Tolerance: the plain line sample for sample (better than -90 dB) except where the EQ match adds its seeded noise floor (our
    /// stream, not numpy's: 20 dB or better overall), and the radio within 1.5 dB in every 50 ms window above -45 dBFS (its hiss is
    /// our stream too).
    /// </summary>
    [NitroFact]
    public void TheRendererMatchesTheReference()
    {
        if (Environment.GetEnvironmentVariable("I76_VOICE_REF") is not { Length: > 0 } refDir || !Directory.Exists(refDir)) return;
        var src = new RemixVoice.Source(TestInstall.Vfs, new AssetVfs(new GameInstall(NitroFactAttribute.Dir!)));
        // stretched (taurus-110's "county line"; groove-61 and 104's "the warehouse" at x0.80), Nitro and matched (taurus-44, 21, jade),
        // and lines as first heard (taurus-11, 77, 119: fades before the match, silence from a sample)
        string[] ids = ["taurus-108", "taurus-110", "taurus-11", "taurus-77", "taurus-119", "taurus-44", "taurus-21", "jade-58", "jade-122", "groove-64", "groove-61", "groove-104"];
        Assert.Contains(ids, id => RemixVoice.ById(id)!.SilenceFrom is not null);
        Assert.Contains(ids, id => RemixVoice.ById(id)!.Pieces.Any(p => p.FadeBeforeMatch));
        Assert.Contains(ids, id => RemixVoice.ById(id)!.Pieces.Any(p => Math.Abs(p.Stretch - 0.8) < 0.01));
        foreach (var id in ids)
        {
            var l = RemixVoice.ById(id)!;
            var plain = SpliceRender.Plain(l.Pieces, l.Total, l.Gain, src.Clip, l.SilenceFrom)!;
            var radio = RemixVoice.Render(l, src)!;
            var (rp, rr) = (Read16(Path.Combine(refDir, id + "-plain.wav")), Read16(Path.Combine(refDir, id + "-radio.wav")));
            Assert.Equal(rp.Length, plain.Length);
            Assert.Equal(rr.Length, radio.Length);
            double snr = Snr(rp, Quant(plain));
            Assert.True(l.Pieces.Any(p => p.Match is not null) ? snr > 20 : snr > 90, $"{id} plain {snr:0.0} dB");
            double worst = Windows(rr, Quant(radio));
            Assert.True(worst < 1.5, $"{id} radio: a 50 ms window {worst:0.00} dB off");
        }
    }

    private static double[] Quant(double[] x) => x.Select(v => (short)(Math.Clamp(v, -1, 1) * 32767) / 32768.0).ToArray();

    private static double Snr(double[] r, double[] x)
    {
        double s = 0, d = 0;
        for (int i = 0; i < r.Length; i++) { s += r[i] * r[i]; d += (r[i] - x[i]) * (r[i] - x[i]); }
        return 10 * Math.Log10(s / Math.Max(d, 1e-20));
    }

    private static double Windows(double[] r, double[] x)
    {
        const int w = 551;
        double worst = 0;
        for (int a = 0; a + w <= r.Length; a += w)
        {
            double er = 0, ex = 0;
            for (int i = a; i < a + w; i++) { er += r[i] * r[i]; ex += x[i] * x[i]; }
            double dr = 10 * Math.Log10(er / w + 1e-18), dx = 10 * Math.Log10(ex / w + 1e-18);
            if (dr > -45) worst = Math.Max(worst, Math.Abs(dr - dx));
        }
        return worst;
    }

    private static double[] Read16(string path)
    {
        var b = File.ReadAllBytes(path);
        int p = 12;
        while (p + 8 <= b.Length && !(b[p] == 'd' && b[p + 1] == 'a' && b[p + 2] == 't' && b[p + 3] == 'a')) p += 8 + BitConverter.ToInt32(b, p + 4);
        int n = BitConverter.ToInt32(b, p + 4) / 2;
        var o = new double[n];
        for (int i = 0; i < n; i++) o[i] = BitConverter.ToInt16(b, p + 8 + 2 * i) / 32768.0;
        return o;
    }
}
