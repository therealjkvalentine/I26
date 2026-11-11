using Godot;

namespace I76.Game.Sim;

/// <summary>
/// The modern look's rounder rear mirror ([port] mirror_round, DefaultRadius; I76_MIRROR_ROUND for a review sheet): the mirror's art is a frame on a tapered outline, wider at the
/// top, with corners a few of its pixels round, drawn on a flat quad. The shader keeps the art's own smooth outline (MirrorEdge) and
/// cuts its corners rounder: the frame's outline and the glass's, each fitted here as a trapezoid from the art's masks (its straight
/// sides, from the rows away from the corners), shrunk by the corner's radius; the shader's rounded shape is that trapezoid's
/// distance less the radius. Art pixels, the art's row order (row 0 its top). The 1997 look keeps the art's own pixels.
/// </summary>
public static class MirrorRound
{
    /// <summary>A trapezoid's corners: top left, top right, bottom right, bottom left (x right, y down, art pixels).</summary>
    public readonly record struct Quad(Vector2 TL, Vector2 TR, Vector2 BR, Vector2 BL);

    /// <summary>
    /// The trapezoid of a mask (w × h, row-major): top and bottom the first and last rows it covers, each side the line through its
    /// rows' ends over the middle half of its height (where the corners do not reach), at the pixels' outer edges.
    /// </summary>
    public static Quad? Fit(bool[] mask, int w, int h)
    {
        int top = -1, bottom = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (mask[y * w + x]) { if (top < 0) top = y; bottom = y; break; }
        if (top < 0 || bottom - top < 4) return null;
        int span = bottom - top + 1, y0 = top + span / 4, y1 = bottom - span / 4;
        var lefts = new List<(float Y, float X)>(); var rights = new List<(float Y, float X)>();
        for (int y = y0; y <= y1; y++)
        {
            int lo = -1, hi = -1;
            for (int x = 0; x < w; x++) if (mask[y * w + x]) { if (lo < 0) lo = x; hi = x; }
            if (lo < 0) continue;
            lefts.Add((y + 0.5f, lo)); rights.Add((y + 0.5f, hi + 1));
        }
        if (lefts.Count < 2) return null;
        var (la, lb) = Line(lefts); var (ra, rb) = Line(rights);
        float t = top, b = bottom + 1;
        return new Quad(new(la + lb * t, t), new(ra + rb * t, t), new(ra + rb * b, b), new(la + lb * b, b));
    }

    /// <summary>A least-squares line x = a + b·y.</summary>
    private static (float A, float B) Line(List<(float Y, float X)> p)
    {
        float my = 0, mx = 0;
        foreach (var (y, x) in p) { my += y; mx += x; }
        my /= p.Count; mx /= p.Count;
        float sxy = 0, syy = 0;
        foreach (var (y, x) in p) { sxy += (y - my) * (x - mx); syy += (y - my) * (y - my); }
        float b = syy > 1e-6f ? sxy / syy : 0;
        return (mx - b * my, b);
    }

    /// <summary>The trapezoid with every side moved inward by <paramref name="r"/> (its corners the sides' new crossings): the rounded shape's core.</summary>
    public static Quad Shrink(Quad q, float r)
    {
        float t = q.TL.Y + r, b = q.BL.Y - r;
        // a side x = a + b·y moved inward by r along its normal: by r·sqrt(1 + b²) across
        float lb = (q.BL.X - q.TL.X) / Mathf.Max(q.BL.Y - q.TL.Y, 1e-3f), rb = (q.BR.X - q.TR.X) / Mathf.Max(q.BR.Y - q.TR.Y, 1e-3f);
        float la = q.TL.X - lb * q.TL.Y + r * Mathf.Sqrt(1 + lb * lb), ra = q.TR.X - rb * q.TR.Y - r * Mathf.Sqrt(1 + rb * rb);
        return new Quad(new(la + lb * t, t), new(ra + rb * t, t), new(ra + rb * b, b), new(la + lb * b, b));
    }

    /// <summary>The art's two masks: the frame's outline (all but the surround) and the glass (transparent).</summary>
    public static (bool[] Outline, bool[] Glass) Masks(byte[] rgba, int w, int h)
    {
        var corner = new[] { rgba[0], rgba[1], rgba[2] };
        var outline = new bool[w * h]; var glass = new bool[w * h];
        for (int i = 0; i < w * h; i++)
        {
            bool opaque = rgba[i * 4 + 3] >= 128;
            outline[i] = !(opaque && MirrorEdge.Surround(rgba, i * 4, corner));
            glass[i] = !opaque;
        }
        return (outline, glass);
    }

    /// <summary>[port] mirror_round's default: 16 of the art's pixels (its corners are about 3; the frame about 6 wide), the review sheet's pick over 6 and 10.</summary>
    public const int DefaultRadius = 16;

    /// <summary>The outline's corner radius in the art's pixels (the glass's that less the frame's width at the top): I76_MIRROR_ROUND=R where it is set, else the setting; 0 the art's own corners.</summary>
    public static float Radius(string? hook, int setting = 0) =>
        float.TryParse(hook, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r) ? Mathf.Clamp(r, 0, 24) : Mathf.Clamp(setting, 0, 24);
}
