using I76.Formats.Vfs;

namespace I76.Formats.Audio;

/// <summary>PCM sound data: interleaved samples, little-endian, as stored.</summary>
public sealed record Pcm(int Channels, int Rate, int Bits, byte[] Data)
{
    public float Seconds => Rate <= 0 || Channels <= 0 || Bits <= 0 ? 0 : Data.Length / (float)(Channels * Bits / 8) / Rate;
}

/// <summary>
/// The game's sounds: plain .wav, or .gpw (docs/formats/misc-data-files.md in
/// the original game): a 28-byte "GAS0" header, then a RIFF/WAVE stream. A ".wav"
/// name with no such file resolves to the .gpw of the same stem, which is
/// how engsnd.dat's names (aheli.wav, eishp.wav) reach their data.
/// </summary>
public static class SoundFiles
{
    public static byte[]? ReadRiff(AssetVfs vfs, string name)
    {
        var d = vfs.TryRead(name);
        if (d is null)
        {
            int dot = name.LastIndexOf('.');
            var stem = dot < 0 ? name : name[..dot];
            d = vfs.TryRead(stem + ".gpw") ?? vfs.TryRead(stem + ".wav");
        }
        if (d is null) return CompanionSounds.For(vfs, name) is { } other ? ReadRiff(other, name) : null;
        if (d.Length > 32 && d[0] == 'G' && d[1] == 'A' && d[2] == 'S' && d[3] == '0') d = d[28..];
        return d;
    }

    /// <summary>
    /// The voice limits a sound carries: its GAS0 header's priority (-1: 50) and how many voices
    /// of its name may play at once (-1: 1). A plain .wav has no header, so 50 and 1.
    /// </summary>
    public static (int Priority, int MaxVoices) Limits(AssetVfs vfs, string name)
    {
        if (Header(vfs, name) is not { } d) return (50, 1);
        int pri = BitConverter.ToInt32(d, 4), max = BitConverter.ToInt32(d, 8);
        return (pri < 0 ? 50 : pri, max < 0 ? 1 : max);
    }

    /// <summary>
    /// Whether a sound started with no voice settings of its own loops: its GAS0 header's mode (-1: 0) is 1 or 2; mode 0 plays
    /// once. Of the cars' horn files only the police car's siren, aps2.wav, is mode 1: started by setSiren or by an AI's honk,
    /// it sounds until something stops it.
    /// </summary>
    public static bool Loops(AssetVfs vfs, string name) => Header(vfs, name) is { } d && BitConverter.ToInt32(d, 12) > 0;

    /// <summary>The sound file's bytes when they begin with a GAS0 header, else null.</summary>
    private static byte[]? Header(AssetVfs vfs, string name)
    {
        var d = vfs.TryRead(name);
        if (d is null)
        {
            int dot = name.LastIndexOf('.');
            var stem = dot < 0 ? name : name[..dot];
            d = vfs.TryRead(stem + ".gpw") ?? vfs.TryRead(stem + ".wav");
        }
        if (d is null && CompanionSounds.For(vfs, name) is { } other) return Header(other, name);
        return d is null || d.Length < 28 || d[0] != 'G' || d[1] != 'A' || d[2] != 'S' || d[3] != '0' ? null : d;
    }

    public static Pcm? Read(AssetVfs vfs, string name) => Cut(name) is { } c ? CutOf(vfs, c) : ReadRiff(vfs, name) is { } r ? Parse(r) : null;

    /// <summary>The fades a cut clip gets at its ends, milliseconds: in, and out.</summary>
    public const int CutFadeIn = 12, CutFadeOut = 35;

    /// <summary>
    /// A cut of a clip (the port's, for the generated missions' radio; docs/remix-missions.md "The radio"): "STEM@START-END" with an
    /// optional extension, START and END in milliseconds into the clip STEM's sound. Null for an ordinary name.
    /// </summary>
    public static (string Stem, int StartMs, int EndMs)? Cut(string name)
    {
        int at = name.IndexOf('@');
        if (at <= 0) return null;
        int dot = name.IndexOf('.', at);
        var span = (dot < 0 ? name[(at + 1)..] : name[(at + 1)..dot]).Split('-');
        if (span.Length != 2 || !int.TryParse(span[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int a)
            || !int.TryParse(span[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int b) || b <= a) return null;
        return (name[..at], a, b);
    }

    /// <summary>The cut's samples: the clip's from START to END (kept inside it), faded in and out over a few milliseconds.</summary>
    private static Pcm? CutOf(AssetVfs vfs, (string Stem, int StartMs, int EndMs) c)
    {
        if (ReadRiff(vfs, c.Stem + ".wav") is not { } r || Parse(r) is not { } p || p.Rate <= 0 || p.Bits is not (8 or 16)) return null;
        int frame = p.Channels * p.Bits / 8, frames = p.Data.Length / frame;
        int f0 = Math.Clamp((int)((long)c.StartMs * p.Rate / 1000), 0, frames), f1 = Math.Clamp((int)((long)c.EndMs * p.Rate / 1000), f0, frames);
        var d = new byte[(f1 - f0) * frame];
        Array.Copy(p.Data, f0 * frame, d, 0, d.Length);
        int n = f1 - f0, fin = Math.Min(n / 2, CutFadeIn * p.Rate / 1000), fout = Math.Min(n / 2, CutFadeOut * p.Rate / 1000);
        for (int i = 0; i < n; i++)
        {
            int g = i < fin ? i * 1024 / Math.Max(1, fin) : n - 1 - i < fout ? (n - 1 - i) * 1024 / Math.Max(1, fout) : 1024;
            if (g >= 1024) continue;
            for (int ch = 0; ch < p.Channels; ch++)
            {
                int o = (i * p.Channels + ch) * (p.Bits / 8);
                if (p.Bits == 16) { int s = (short)(d[o] | d[o + 1] << 8); s = s * g / 1024; d[o] = (byte)s; d[o + 1] = (byte)(s >> 8); }
                else { int s = d[o] - 128; d[o] = (byte)(s * g / 1024 + 128); }
            }
        }
        return p with { Data = d };
    }

    /// <summary>
    /// The volume a sound's GAS0 header asks for, in percent: the int32 at offset 16, -1 (or no header) meaning 100. The original
    /// starts the voice at 400·log2(v/100) millibels (<see cref="VolumeDb"/>). The Moth trucks', buses' and tractor's engine
    /// (eimarx) says 60; the tyre skid and turn sounds 50.
    /// </summary>
    public static int Volume(AssetVfs vfs, string name)
    {
        var d = vfs.TryRead(name);
        if (d is null)
        {
            int dot = name.LastIndexOf('.');
            var stem = dot < 0 ? name : name[..dot];
            d = vfs.TryRead(stem + ".gpw") ?? vfs.TryRead(stem + ".wav");
        }
        if (d is null && CompanionSounds.For(vfs, name) is { } other) return Volume(other, name);
        return d is null ? 100 : Volume(d);
    }

    /// <summary>The same, from the file's bytes.</summary>
    public static int Volume(ReadOnlySpan<byte> file)
    {
        if (file.Length < 28 || file[0] != 'G' || file[1] != 'A' || file[2] != 'S' || file[3] != '0') return 100;
        int v = BitConverter.ToInt32(file[16..]);
        return v < 0 ? 100 : v;
    }

    /// <summary>A header volume in percent as decibels: 4 dB per halving, 0 dB at 100 (60 is -2.9 dB, 50 is -4 dB).</summary>
    public static float VolumeDb(int percent) => percent <= 0 ? -80f : 4f * MathF.Log2(percent / 100f);

    public static Pcm? Parse(byte[] riff)
    {
        if (riff.Length < 12 || Bin.Latin1(riff.AsSpan(0, 4)) != "RIFF" || Bin.Latin1(riff.AsSpan(8, 4)) != "WAVE") return null;
        int p = 12, channels = 1, rate = 11025, bits = 8;
        byte[]? data = null;
        while (p + 8 <= riff.Length)
        {
            string id = Bin.Latin1(riff.AsSpan(p, 4));
            long size = Bin.U32(riff, p + 4);
            int body = p + 8;
            // a size past the end (some files carry garbage or a too-large data size) takes what is there
            if (size > riff.Length - body) size = riff.Length - body;
            if (id == "fmt " && body + 16 <= riff.Length)
            {
                channels = Bin.U16(riff, body + 2);
                rate = (int)Bin.U32(riff, body + 4);
                bits = Bin.U16(riff, body + 14);
            }
            else if (id == "data")
                data = riff.AsSpan(body, (int)size).ToArray();
            p = body + (int)size + (int)(size & 1);
        }
        return data is null ? null : new Pcm(channels, rate, bits, data);
    }
}
