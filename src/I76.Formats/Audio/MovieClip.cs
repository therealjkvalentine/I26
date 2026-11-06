using I76.Formats.Video;
using I76.Formats.Vfs;

namespace I76.Formats.Audio;

/// <summary>
/// A stretch of a briefing movie's soundtrack as a sound of its own, cut at run time from the user's own smk/ files (nothing is
/// copied into the port). The movies' audio is 22050 Hz 8-bit mono (Smacker's lossless audio), twice the kit's 11025 Hz, and
/// recorded and mixed fuller: the engine start-ups at the end of the briefings are the cleanest engine sound the game ships.
/// </summary>
public static class MovieClip
{
    /// <summary>The modern look's ignition ([port] movie_ignition): INT03F01.SMK's start-up, 52.45..55.8 s: 0.25 s of quiet, the
    /// engine catching, three seconds of it settling and rising. <see cref="StartUpCatch"/> is when it catches, from the clip's start.</summary>
    public const string StartUpMovie = "INT03F01.SMK";
    public const double StartUpFrom = 52.45, StartUpTo = 55.8, StartUpCatch = 0.25, StartUpFadeOut = 0.8;

    /// <summary>The start-up from the install's smk/ folder, or null without that movie.</summary>
    public static Pcm? StartUp(GameInstall install)
    {
        if (install.FindDir("smk") is not { } dir) return null;
        var path = Directory.EnumerateFiles(dir).FirstOrDefault(f => Path.GetFileName(f).Equals(StartUpMovie, StringComparison.OrdinalIgnoreCase));
        return path is null ? null : Cut(path, StartUpFrom, StartUpTo, 0.005, StartUpFadeOut);
    }

    /// <summary>Track 0 of the movie from <paramref name="from"/> to <paramref name="to"/> seconds, as 16-bit PCM with linear
    /// fades in and out. Decodes the audio only (no video), up to <paramref name="to"/>.</summary>
    public static Pcm? Cut(string smkPath, double from, double to, double fadeIn, double fadeOut)
    {
        using var m = Smacker.Open(smkPath);
        if (!m.HasAudio(0)) return null;
        int rate = m.SampleRate(0), ch = m.Stereo(0) ? 2 : 1, bytes = (m.Is16Bit(0) ? 2 : 1) * ch;
        long need = (long)Math.Ceiling(to * rate) * bytes;
        var all = new MemoryStream();
        while (all.Length < need && m.NextFrame((t, pcm) => { if (t == 0) all.Write(pcm); }, video: false)) { }
        return Window(new Pcm(ch, rate, bytes / ch * 8, all.ToArray()), from, to, fadeIn, fadeOut);
    }

    /// <summary>A window of PCM (8- or 16-bit, any channels) as 16-bit, faded in and out linearly; clipped to what there is.</summary>
    public static Pcm Window(Pcm src, double from, double to, double fadeIn, double fadeOut)
    {
        int ch = src.Channels, bps = src.Bits / 8, frames = src.Data.Length / (bps * ch);
        int a = Math.Clamp((int)Math.Round(from * src.Rate), 0, frames), b = Math.Clamp((int)Math.Round(to * src.Rate), a, frames);
        int n = b - a, fi = (int)(fadeIn * src.Rate), fo = (int)(fadeOut * src.Rate);
        var o = new byte[n * ch * 2];
        for (int i = 0; i < n; i++)
        {
            float g = 1f;
            if (fi > 0 && i < fi) g = i / (float)fi;
            if (fo > 0 && i >= n - fo) g = Math.Min(g, (n - 1 - i) / (float)fo);
            for (int c = 0; c < ch; c++)
            {
                int k = (a + i) * ch + c;
                float s = bps == 1 ? (src.Data[k] - 128) * 256f : BitConverter.ToInt16(src.Data, k * 2);
                short v = (short)Math.Clamp(MathF.Round(s * g), short.MinValue, short.MaxValue);
                o[(i * ch + c) * 2] = (byte)v; o[(i * ch + c) * 2 + 1] = (byte)(v >> 8);
            }
        }
        return new Pcm(ch, src.Rate, 16, o);
    }
}
