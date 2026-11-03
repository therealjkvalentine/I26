namespace I76.Formats.Imaging;

/// <summary>
/// An upscaled replacement for a world, car or cockpit texture, from a player's pack (none ships with the port):
/// a picture named by the member's stem ("pp11ftf1.png", "zsy_.webp"), the right way up, at a power-of-two
/// multiple of the original's size, the same both ways. Alpha under one half is the original's colour key.
/// A picture with no alpha at all takes the original's key, scaled; nothing else about it is checked.
/// </summary>
public static class HdTexture
{
    /// <summary>A .dds first: block-compressed with its mipmaps, written by tools/upscale-textures.py in the texture's stored row order and
    /// already keyed (alpha 0 or 255) and bled, so the game sends it to the GPU as it is. A picture is fitted on load (Fit).</summary>
    public static readonly string[] Extensions = [".dds", ".png", ".webp", ".jpg"];

    /// <summary>The pack paths that can hold the member's upscale, in the order tried.</summary>
    public static IEnumerable<string> Candidates(string member)
    {
        var stem = Path.GetFileNameWithoutExtension(member).ToLowerInvariant();
        return Extensions.Select(e => stem + e);
    }

    /// <summary>The picture's scale over the original: 1, 2, 4, 8, ... when it is one, the same both ways; 0 otherwise.</summary>
    public static int ScaleOf(int w, int h, int ow, int oh)
    {
        if (ow <= 0 || oh <= 0 || w % ow != 0 || h % oh != 0) return 0;
        int s = w / ow;
        return s == h / oh && s > 0 && (s & (s - 1)) == 0 ? s : 0;
    }

    /// <summary>
    /// The picture's RGBA8 (rows top-down, as decoded) in the texture's stored order: rows flipped when
    /// <paramref name="bottomUp"/> (a .map or .m16 keeps a DIB's order; a .vqm is top-down), alpha 0 under one half
    /// and 255 above, or, when the picture is opaque throughout, the original's keyed texels (alpha 0 in
    /// <paramref name="original"/>, already in stored order) each covering a <paramref name="scale"/>-square block.
    /// A keyed texel takes the colour of the original's texel under it (<paramref name="original"/> bled under its key,
    /// TextureFactory), so filtering and mipmaps pull no key colour into the edges whatever the pack put there, and the
    /// upscale needs no bleed of its own at sixteen times the texels.
    /// </summary>
    public static byte[] Fit(byte[] rgba, int w, int h, int scale, byte[] original, int ow, int oh, bool bottomUp)
    {
        var o = new byte[w * h * 4];
        bool anyAlpha = false;
        for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] < 255) { anyAlpha = true; break; }
        for (int y = 0; y < h; y++)
        {
            int sy = bottomUp ? h - 1 - y : y;
            Buffer.BlockCopy(rgba, sy * w * 4, o, y * w * 4, w * 4);
            int orow = (y / scale) * ow;
            for (int x = 0, i = y * w * 4; x < w; x++, i += 4)
            {
                int oi = (orow + x / scale) * 4;
                bool keyed = anyAlpha ? o[i + 3] < 128 : original[oi + 3] == 0;
                if (keyed) { o[i] = original[oi]; o[i + 1] = original[oi + 1]; o[i + 2] = original[oi + 2]; o[i + 3] = 0; }
                else o[i + 3] = 255;
            }
        }
        return o;
    }
}
