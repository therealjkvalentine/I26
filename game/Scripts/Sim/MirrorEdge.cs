namespace I76.Game.Sim;

/// <summary>
/// The modern look's smooth mirror outline. The mirror's art cuts its frame and glass by pixel (the surround in its corner
/// colour, the glass at alpha 0), which drawn a few screen pixels to the texel is a staircase. Field takes the original
/// art's two masks (not surround, glass) as smooth outlines (KeyEdge: staircases straightened, uneven curves evened, corners
/// kept) in signed distance fields at four texels a pixel: the shader cuts each at 0.5 over one screen pixel. Fill is the art served
/// (the original or a pack's upscale) with the frame's colours spread into the surround and the glass, so the soft edge
/// takes the frame's colour. KeyField and KeyFill do the same for a keyed panel's outline (the compass). The 1997 look keeps
/// the art's own pixels.
/// </summary>
public static class MirrorEdge
{
    /// <summary>The field's texels per original pixel.</summary>
    public const int FieldScale = 4;

    /// <summary>Whether a texel is the art's surround: opaque and within 0.1 of the corner colour (the shaders' test).</summary>
    public static bool Surround(byte[] rgba, int i, byte[] corner)
    {
        if (rgba[i + 3] < 128) return false;
        float dr = (rgba[i] - corner[0]) / 255f, dg = (rgba[i + 1] - corner[1]) / 255f, db = (rgba[i + 2] - corner[2]) / 255f;
        return dr * dr + dg * dg + db * db < 0.01f;
    }

    /// <summary>
    /// The two masks of a w × h RGBA art as smooth outlines' distance fields at FieldScale (KeyEdge.Field): an RG8 picture
    /// (w·4 × h·4, the art's row order), R the frame and glass (not surround), G the glass (closed). 128 is the edge.
    /// </summary>
    public static byte[] Field(byte[] rgba, int w, int h)
    {
        var corner = new[] { rgba[0], rgba[1], rgba[2] };
        var solid = new bool[w * h];
        var glass = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            solid[i] = !Surround(rgba, i * 4, corner);
            glass[i] = rgba[i * 4 + 3] < 128 ? 1 : 0;
        }
        Close(glass, w, h);
        var a = I76.Formats.Imaging.KeyEdge.Field(solid, w, h, FieldScale);
        var b = I76.Formats.Imaging.KeyEdge.Field([.. glass.Select(v => v > 0.5f)], w, h, FieldScale);
        var o = new byte[a.Length * 2];
        for (int i = 0; i < a.Length; i++) { o[i * 2] = a[i]; o[i * 2 + 1] = b[i]; }
        return o;
    }

    /// <summary>
    /// Closes a mask (grown a pixel, four ways, then shrunk a pixel): the one-pixel lines the art paints on the glass (its
    /// cross-hair) count as glass, so the outline keeps the glass whole; the shaders draw them from the art.
    /// </summary>
    private static void Close(float[] m, int w, int h)
    {
        var grown = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                grown[y * w + x] = System.MathF.Max(m[y * w + x], System.MathF.Max(
                    System.MathF.Max(x > 0 ? m[y * w + x - 1] : 0, x < w - 1 ? m[y * w + x + 1] : 0),
                    System.MathF.Max(y > 0 ? m[(y - 1) * w + x] : 0, y < h - 1 ? m[(y + 1) * w + x] : 0)));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                m[y * w + x] = System.MathF.Min(grown[y * w + x], System.MathF.Min(
                    System.MathF.Min(x > 0 ? grown[y * w + x - 1] : 1, x < w - 1 ? grown[y * w + x + 1] : 1),
                    System.MathF.Min(y > 0 ? grown[(y - 1) * w + x] : 1, y < h - 1 ? grown[(y + 1) * w + x] : 1)));
    }

    /// <summary>
    /// The served art (w × h RGBA, <paramref name="scale"/> texels a pixel) with its frame's colours spread, a texel a
    /// pass, into the surround and the glass round it (three original pixels' reach), opaque: the colour the smooth
    /// edge takes where the art's own texel is not frame.
    /// </summary>
    public static byte[] Fill(byte[] rgba, int w, int h, int scale)
    {
        var corner = new[] { rgba[0], rgba[1], rgba[2] };
        var known = new bool[w * h];
        for (int i = 0; i < w * h; i++) known[i] = rgba[i * 4 + 3] >= 128 && !Surround(rgba, i * 4, corner);
        return Spread(rgba, w, h, scale, known);
    }

    /// <summary>
    /// A keyed panel's outline (the compass, whose art keys everything round it): its opaque mask (alpha 0.5 and over) as
    /// <see cref="Field"/>'s, an R8 picture (w·4 × h·4, the art's row order). 128 is the edge.
    /// </summary>
    public static byte[] KeyField(byte[] rgba, int w, int h) =>
        I76.Formats.Imaging.KeyEdge.Field(I76.Formats.Imaging.KeyEdge.Opaque(rgba, w, h), w, h, FieldScale);

    /// <summary>A keyed panel's served art with its opaque texels' colours spread into the keyed ones round them, opaque: the soft edge's colour.</summary>
    public static byte[] KeyFill(byte[] rgba, int w, int h, int scale)
    {
        var known = new bool[w * h];
        for (int i = 0; i < w * h; i++) known[i] = rgba[i * 4 + 3] >= 128;
        return Spread(rgba, w, h, scale, known);
    }

    /// <summary>The <paramref name="known"/> texels' colours spread a texel a pass into the rest, three original pixels' reach; all opaque.</summary>
    private static byte[] Spread(byte[] rgba, int w, int h, int scale, bool[] known)
    {
        var o = (byte[])rgba.Clone();
        var next = new bool[w * h];
        for (int pass = 0; pass < 3 * scale; pass++)
        {
            System.Array.Copy(known, next, known.Length);
            bool any = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (known[i]) continue;
                    int s = x > 0 && known[i - 1] ? i - 1 : x < w - 1 && known[i + 1] ? i + 1 : y > 0 && known[i - w] ? i - w : y < h - 1 && known[i + w] ? i + w : -1;
                    if (s < 0) continue;
                    System.Buffer.BlockCopy(o, s * 4, o, i * 4, 3);
                    next[i] = any = true;
                }
            (known, next) = (next, known);
            if (!any) break;
        }
        for (int i = 0; i < w * h; i++) o[i * 4 + 3] = 255;
        return o;
    }
}
