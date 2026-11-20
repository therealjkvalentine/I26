using I76.Formats.Shell;

namespace I76.Game.Shell;

/// <summary>
/// A highlight sprite cut from the background it is drawn on, at the window's resolution (the modern look). CHOOSE YOUR
/// VEHICLE's bank 0x40 holds one rectangle per vehicle, copied from record 27 with a glow painted round the vehicle: inside
/// the rectangle every other pixel is the background's own, so the original's blit changes only the glow. An upscale of the
/// whole rectangle never matches the upscaled background around and under it (the menu packs drew these three in another
/// screen's palette as well), so the port draws only the glow: the pixels where the sprite differs from the background under
/// it, in the background's palette, with a coverage smoothed across them (a tent filter, then bilinear at the picture's
/// scale, cut at the middle with a soft edge) and their colours spread by the same filter (normalised by the coverage, so no
/// background colour bleeds in). The rest of the rectangle is left see-through over the upscaled background.
/// </summary>
public static class HaloSprite
{
    /// <summary>The banks drawn this way: 0x40, CHOOSE YOUR VEHICLE's highlights (record 27).</summary>
    public static bool Applies(Sprite s) => s.Bank == 0x40;

    /// <summary>The coverage's soft edge, from transparent to opaque.</summary>
    private const float Edge0 = 0.3f, Edge1 = 0.6f;

    /// <summary>Where the sprite, blitted at (x, y), changes the 640x480 background (row by row, its own size).</summary>
    public static bool[] Changed(Sprite s, ReadOnlySpan<byte> background, int x, int y)
    {
        var m = new bool[s.Width * s.Height];
        for (int r = 0; r < s.Height; r++)
            for (int c = 0; c < s.Width; c++)
            {
                int i = r * s.Width + c, fx = x + c, fy = y + r;
                if (!s.Opaque[i]) continue;
                bool inside = fx is >= 0 and < ShellFrame.W && fy is >= 0 and < ShellFrame.H;
                m[i] = !inside || background[fy * ShellFrame.W + fx] != s.Pixels[i];
            }
        return m;
    }

    /// <summary>
    /// The glow as RGBA8 (rows from the top), <paramref name="scale"/> texels a sprite pixel: the sprite's size times the scale.
    /// </summary>
    public static byte[] Rgba(Sprite s, ReadOnlySpan<byte> background, int x, int y, byte[] palette, int scale)
    {
        int w = s.Width, h = s.Height;
        var changed = Changed(s, background, x, y);
        // coverage and premultiplied colour, each through a [1 2 1]/4 tent across and down
        var cov = new float[w * h];
        var col = new float[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            if (!changed[i]) continue;
            cov[i] = 1;
            int p = s.Pixels[i] * 3;
            col[i * 3] = palette[p]; col[i * 3 + 1] = palette[p + 1]; col[i * 3 + 2] = palette[p + 2];
        }
        cov = Tent(cov, w, h, 1);
        col = Tent(col, w, h, 3);
        int tw = w * scale, th = h * scale;
        var o = new byte[tw * th * 4];
        for (int ty = 0; ty < th; ty++)
        {
            float sy = (ty + 0.5f) / scale - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(sy), 0, h - 1), y1 = Math.Min(y0 + 1, h - 1);
            float fy = Math.Clamp(sy - MathF.Floor(sy), 0, 1);
            for (int tx = 0; tx < tw; tx++)
            {
                float sx = (tx + 0.5f) / scale - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(sx), 0, w - 1), x1 = Math.Min(x0 + 1, w - 1);
                float fx = Math.Clamp(sx - MathF.Floor(sx), 0, 1);
                float Bil(float[] f, int ch, int n) =>
                    (f[(y0 * w + x0) * n + ch] * (1 - fx) + f[(y0 * w + x1) * n + ch] * fx) * (1 - fy)
                    + (f[(y1 * w + x0) * n + ch] * (1 - fx) + f[(y1 * w + x1) * n + ch] * fx) * fy;
                float m = Bil(cov, 0, 1);
                float a = Smooth(m);
                if (a <= 0) continue;
                int q = (ty * tw + tx) * 4;
                for (int ch = 0; ch < 3; ch++) o[q + ch] = (byte)Math.Clamp(MathF.Round(Bil(col, ch, 3) / Math.Max(m, 1e-4f)), 0, 255);
                o[q + 3] = (byte)MathF.Round(a * 255);
            }
        }
        return o;
    }

    private static float Smooth(float m)
    {
        float t = Math.Clamp((m - Edge0) / (Edge1 - Edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>A [1 2 1]/4 tent across then down, edges repeated, <paramref name="n"/> channels.</summary>
    private static float[] Tent(float[] a, int w, int h, int n)
    {
        var t = new float[a.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < n; c++)
                    t[(y * w + x) * n + c] = (a[(y * w + Math.Max(x - 1, 0)) * n + c] + 2 * a[(y * w + x) * n + c] + a[(y * w + Math.Min(x + 1, w - 1)) * n + c]) / 4;
        var u = new float[a.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < n; c++)
                    u[(y * w + x) * n + c] = (t[(Math.Max(y - 1, 0) * w + x) * n + c] + 2 * t[(y * w + x) * n + c] + t[(Math.Min(y + 1, h - 1) * w + x) * n + c]) / 4;
        return u;
    }
}
