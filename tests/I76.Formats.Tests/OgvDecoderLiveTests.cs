using I76.Formats.Video;
using Xunit;
using Xunit.Abstractions;

namespace I76.Formats.Tests;

/// <summary>
/// Runs only with the native decoder built (native/ogv/build.ps1: game/native/&lt;platform&gt;/, or I76_OGV_LIB) and an upscaled
/// movie to read (I76_OGV_SAMPLE, or the user folder's movies/INT08F01.ogv); neither is in the repository.
/// </summary>
public sealed class OgvLiveFactAttribute : FactAttribute
{
    public static string? Library
    {
        get
        {
            if (Environment.GetEnvironmentVariable("I76_OGV_LIB") is { Length: > 0 } env && File.Exists(env)) return env;
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "I76.sln")))
                {
                    string p = Path.Combine(dir.FullName, "game", "native", OgvLibrary.Platform, OgvLibrary.FileName);
                    return File.Exists(p) ? p : null;
                }
            return null;
        }
    }

    public static string? Sample
    {
        get
        {
            if (Environment.GetEnvironmentVariable("I76_OGV_SAMPLE") is { Length: > 0 } s && File.Exists(s)) return s;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string p = Path.Combine(appData, "I26", "movies", "INT08F01.ogv");
            return File.Exists(p) ? p : null;
        }
    }

    public OgvLiveFactAttribute()
    {
        if (Library is null || Sample is null) Skip = "build native/ogv (or set I76_OGV_LIB) and set I76_OGV_SAMPLE to an .ogv to run";
    }
}

[Collection("ogv library")]   // (OgvLibrary's one load is shared: not alongside OgvLibraryTests)
public sealed class OgvDecoderLiveTests(ITestOutputHelper log)
{
    private static OgvDecoder OpenSample()
    {
        OgvLibrary.ResetForTests();
        Assert.True(OgvLibrary.Load([OgvLiveFactAttribute.Library]), OgvLibrary.Missing);
        var d = OgvDecoder.Open(OgvLiveFactAttribute.Sample!);
        Assert.NotNull(d);
        return d;
    }

    [OgvLiveFact]
    public void TheFramesComeInOrderAtTheirTimesAndRewindGivesTheFirstAgain()
    {
        using var d = OpenSample();
        var info = d.Info;
        log.WriteLine($"{OgvLibrary.Versions}; {info.Width}x{info.Height} {info.FpsNum}/{info.FpsDen} fps, {info.FrameCount} frames, sound {info.AudioRate} Hz x{info.AudioChannels}, {info.AudioFrames} samples");
        Assert.True(info.Is420);
        Assert.True(info.FrameCount > 30);
        var first = new byte[d.PackedBytes];
        var buf = new byte[d.PackedBytes];
        for (int i = 0; i < 30; i++)
        {
            Assert.True(d.NextPacked(i == 0 ? first : i % 2 == 0 ? buf : null, out long index, out double seconds));
            Assert.Equal(i, index);
            Assert.Equal(i * info.FrameDuration, seconds, 9);
        }
        var pcm = new float[4096 * Math.Max(1, info.AudioChannels)];
        if (info.AudioChannels > 0)
        {
            int n = d.ReadAudio(pcm);
            Assert.InRange(n, 1, 4096);
            Assert.All(pcm.Take(n * info.AudioChannels), s => Assert.InRange(s, -1f, 1f));
        }
        d.Rewind();
        Assert.True(d.NextPacked(buf, out long again, out _));
        Assert.Equal(0, again);
        Assert.Equal(first, buf);
        // the library's own RGBA conversion of the next frame
        var rgba = new byte[info.Width * info.Height * 4];
        Assert.True(d.NextRgba(rgba, out long next, out _));
        Assert.Equal(1, next);
        Assert.All(Enumerable.Range(0, 64), k => Assert.Equal(255, rgba[k * 4 + 3]));
    }

    [OgvLiveFact]
    public void TheWholeFileDecodesToItsLastPagesFrameCountAndSoundLength()
    {
        using var d = OpenSample();
        long frames = 0, last = -1;
        while (d.NextPacked(null, out long index, out _)) { Assert.Equal(last + 1, index); last = index; frames++; }
        Assert.Equal(d.Info.FrameCount, frames);
        if (d.Info.AudioChannels == 0) return;
        d.Rewind();
        long samples = 0;
        var pcm = new float[8192 * d.Info.AudioChannels];
        for (int n; (n = d.ReadAudio(pcm)) > 0;) samples += n;
        log.WriteLine($"{frames} frames ({frames * d.Info.FrameDuration:0.000} s), {samples} samples ({samples / (double)d.Info.AudioRate:0.000} s); last page says {d.Info.AudioFrames}");
        Assert.Equal(d.Info.AudioFrames, samples);
    }
}
