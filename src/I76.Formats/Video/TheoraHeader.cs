namespace I76.Formats.Video;

/// <summary>
/// The picture size and frame rate from an .ogv file's first page (the Theora identification header), read without a decoder:
/// the engine's own player does not tell its frame rate, which the movie player's pace measure needs on that path too.
/// </summary>
public static class TheoraHeader
{
    public readonly record struct Info(int Width, int Height, int FpsNum, int FpsDen)
    {
        public double FrameDuration => FpsNum > 0 ? FpsDen / (double)FpsNum : 0;
    }

    public static Info? Read(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var b = new byte[27 + 255 + 64];
            int n = f.Read(b, 0, b.Length);
            return Parse(b.AsSpan(0, n));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The header from the file's first bytes, or null when they are not an Ogg page starting a Theora stream.</summary>
    public static Info? Parse(ReadOnlySpan<byte> b)
    {
        if (b.Length < 27 || b[0] != 'O' || b[1] != 'g' || b[2] != 'g' || b[3] != 'S') return null;
        int at = 27 + b[26];
        if (b.Length < at + 30 || b[at] != 0x80 || !b.Slice(at + 1, 6).SequenceEqual("theora"u8)) return null;
        var p = b[at..];
        static int W24(ReadOnlySpan<byte> p, int o) => (p[o] << 16) | (p[o + 1] << 8) | p[o + 2];
        static int W32(ReadOnlySpan<byte> p, int o) => (p[o] << 24) | (p[o + 1] << 16) | (p[o + 2] << 8) | p[o + 3];
        int w = W24(p, 14), h = W24(p, 17), num = W32(p, 22), den = W32(p, 26);
        return w > 0 && h > 0 && num > 0 && den > 0 ? new Info(w, h, num, den) : null;
    }
}
