namespace I76.Game.Sim;

/// <summary>
/// The modern look's smooth mirror outline. The mirror's art cuts its frame and glass by pixel (the surround in its corner
/// colour, the glass at alpha 0), which drawn a few screen pixels to the texel is a staircase. Field takes the original
/// art's two masks (not surround, glass), blurred (a Gaussian, <see cref="Sigma"/> original pixels) and sampled at four
/// texels a pixel: the shader cuts each at 0.5 over one screen pixel, a smooth antialiased curve. Fill is the art served
/// (the original or a pack's upscale) with the frame's colours spread into the surround and the glass, so the soft edge
/// takes the frame's colour. The 1997 look keeps the art's own pixels.
/// </summary>
public static class MirrorEdge
{
    /// <summary>The blur, in original pixels: wide enough to straighten the art's one-pixel steps, its corners rounded no more than they are drawn.</summary>
    public const float Sigma = 2f;

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
    /// The two masks of a w × h RGBA art, blurred and sampled at FieldScale: an RG8 picture (w·4 × h·4, the art's row
    /// order), R the frame and glass (not surround), G the glass (closed). 128 is the edge.
    /// </summary>
    public static byte[] Field(byte[] rgba, int w, int h, float sigma = Sigma)
    {
        var corner = new[] { rgba[0], rgba[1], rgba[2] };
        var solid = new float[w * h];
        var glass = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            solid[i] = Surround(rgba, i * 4, corner) ? 0 : 1;
            glass[i] = rgba[i * 4 + 3] < 128 ? 1 : 0;
        }
        int fw = w * FieldScale, fh = h * FieldScale;
        Close(glass, w, h);
        var a = Blur(solid, w, h, sigma);
        var b = Blur(glass, w, h, sigma);
        var o = new byte[fw * fh * 2];
        for (int i = 0; i < fw * fh; i++)
        {
            o[i * 2] = (byte)System.Math.Clamp((int)(a[i] * 255f + 0.5f), 0, 255);
            o[i * 2 + 1] = (byte)System.Math.Clamp((int)(b[i] * 255f + 0.5f), 0, 255);
        }
        return o;
    }

    /// <summary>
    /// Closes a mask (grown a pixel, four ways, then shrunk a pixel): the one-pixel lines the art paints on the glass (its
    /// cross-hair) count as glass, so the blur keeps the glass whole; the shaders draw them from the art.
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

    /// <summary>A mask's normalised Gaussian blur sampled at the FieldScale grid's texel centres, edges clamped; separable.</summary>
    private static float[] Blur(float[] m, int w, int h, float sigma)
    {
        int fw = w * FieldScale, fh = h * FieldScale, r = (int)System.MathF.Ceiling(3 * sigma) + 1;
        var tmp = new float[h * fw];
        for (int X = 0; X < fw; X++)
        {
            float xs = (X + 0.5f) / FieldScale - 0.5f;
            int x0 = (int)System.MathF.Floor(xs);
            Span<float> wt = stackalloc float[2 * r + 1];
            float sum = 0;
            for (int d = -r; d <= r; d++) { float t = (x0 + d - xs) / sigma; sum += wt[d + r] = System.MathF.Exp(-0.5f * t * t); }
            for (int y = 0; y < h; y++)
            {
                float acc = 0;
                for (int d = -r; d <= r; d++) acc += wt[d + r] * m[y * w + System.Math.Clamp(x0 + d, 0, w - 1)];
                tmp[y * fw + X] = acc / sum;
            }
        }
        var o = new float[fh * fw];
        for (int Y = 0; Y < fh; Y++)
        {
            float ys = (Y + 0.5f) / FieldScale - 0.5f;
            int y0 = (int)System.MathF.Floor(ys);
            Span<float> wt = stackalloc float[2 * r + 1];
            float sum = 0;
            for (int d = -r; d <= r; d++) { float t = (y0 + d - ys) / sigma; sum += wt[d + r] = System.MathF.Exp(-0.5f * t * t); }
            for (int X = 0; X < fw; X++)
            {
                float acc = 0;
                for (int d = -r; d <= r; d++) acc += wt[d + r] * tmp[System.Math.Clamp(y0 + d, 0, h - 1) * fw + X];
                o[Y * fw + X] = acc / sum;
            }
        }
        return o;
    }

    /// <summary>
    /// The served art (w × h RGBA, <paramref name="scale"/> texels a pixel) with its frame's colours spread, a texel a
    /// pass, into the surround and the glass round it (three original pixels' reach), opaque: the colour the smooth
    /// edge takes where the art's own texel is not frame.
    /// </summary>
    public static byte[] Fill(byte[] rgba, int w, int h, int scale)
    {
        var corner = new[] { rgba[0], rgba[1], rgba[2] };
        var o = (byte[])rgba.Clone();
        var known = new bool[w * h];
        for (int i = 0; i < w * h; i++) known[i] = rgba[i * 4 + 3] >= 128 && !Surround(rgba, i * 4, corner);
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
