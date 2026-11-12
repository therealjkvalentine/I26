using Godot;

namespace I76.Game.Sim;

/// <summary>
/// The modern look's rear mirror's corners ([port] mirror_round, the art's own by default; I76_MIRROR_ROUND for a review sheet): the mirror's art is a frame on a tapered outline, wider at the
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

    /// <summary>[port] mirror_round's default: 0, the art's own corners (about 3 of its pixels; the frame about 6 wide). The door mirrors' glass is the one rounded (GlassRound).</summary>
    public const int DefaultRadius = 0;

    /// <summary>The outline's corner radius in the art's pixels (the glass's that less the frame's width at the top): I76_MIRROR_ROUND=R where it is set, else the setting; 0 the art's own corners.</summary>
    public static float Radius(string? hook, int setting = 0) =>
        float.TryParse(hook, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r) ? Mathf.Clamp(r, 0, 24) : Mathf.Clamp(setting, 0, 24);
}

/// <summary>
/// The modern look's rounder door-mirror glass ([port] side_mirror_round; I76_SIDE_MIRROR_ROUND for a review sheet). The door
/// mirrors' glass is flat faces of the car's interior (MirrorGlass), straight-sided: the Piranha's left a hexagon, its right and the CC
/// cars' left a four-sided pane, each in a housing of the same straight outline. The glass's mesh keeps its outline; its shader draws
/// the view inside a rounded shape and the rim's moulding round it (MirrorRim's shading), the corners outside the rounded shape the
/// moulding's dark, so the housing's outline is kept and the glass in it is rounded. The shape: the pane's outline as the driver sees
/// it (its picture coordinates, u times the glass's width over height, v: the glass's height is 1), its convex hull eroded by the
/// radius (Erode) and widened by it again in the shader (the hull's distance less the radius). The radius is a share of the
/// outline's shorter side, at most just under the largest circle inside it (50 %: a stadium or an oval).
/// </summary>
public static class GlassRound
{
    /// <summary>[port] side_mirror_round's default, percent of the glass's shorter side.</summary>
    public const int DefaultShare = 30;
    /// <summary>The largest share: half the shorter side, a round end.</summary>
    public const int MaxShare = 50;
    /// <summary>The shader's corners at most.</summary>
    public const int MaxCorners = 12;

    /// <summary>The share: I76_SIDE_MIRROR_ROUND=P where it is set, else the setting; 0 the glass's own corners.</summary>
    public static int Share(string? hook, int setting) =>
        int.TryParse(hook, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int p) ? Math.Clamp(p, 0, MaxShare) : Math.Clamp(setting, 0, MaxShare);

    /// <summary>The convex hull of a set of points, counter-clockwise (x right, y up), without repeated or collinear points.</summary>
    public static List<Vector2> Hull(IEnumerable<Vector2> points)
    {
        var p = points.Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        if (p.Count < 3) return p;
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var h = new List<Vector2>();
        foreach (var pass in new[] { p, Enumerable.Reverse(p).ToList() })
        {
            int start = h.Count;
            foreach (var q in pass)
            {
                while (h.Count >= start + 2 && Cross(h[^2], h[^1], q) <= 1e-9f) h.RemoveAt(h.Count - 1);
                h.Add(q);
            }
            h.RemoveAt(h.Count - 1);
        }
        return h;
    }

    /// <summary>A convex counter-clockwise polygon with every side moved inward by <paramref name="r"/>: the sides' half-planes clipped in turn (a side that vanishes drops out); empty when nothing is left.</summary>
    public static List<Vector2> Erode(IReadOnlyList<Vector2> hull, float r)
    {
        var poly = hull.ToList();
        for (int i = 0; i < hull.Count && poly.Count > 0; i++)
        {
            var a = hull[i]; var e = hull[(i + 1) % hull.Count] - a;
            if (e.LengthSquared() < 1e-12f) continue;
            var n = new Vector2(-e.Y, e.X).Normalized();   // inward: the left of a counter-clockwise side
            float D(Vector2 q) => (q - a).Dot(n) - r;
            var next = new List<Vector2>();
            for (int k = 0; k < poly.Count; k++)
            {
                var p = poly[k]; var q = poly[(k + 1) % poly.Count];
                float dp = D(p), dq = D(q);
                if (dp >= 0) next.Add(p);
                if ((dp >= 0) != (dq >= 0)) next.Add(p + (q - p) * (dp / (dp - dq)));
            }
            poly = next;
        }
        return poly.Count >= 3 ? poly : [];
    }

    /// <summary>The radius of the largest circle inside a convex polygon (the largest erosion that leaves something), found by halving.</summary>
    public static float Inradius(IReadOnlyList<Vector2> hull)
    {
        if (hull.Count < 3) return 0;
        float lo = 0, hi = Math.Max(hull.Max(q => q.X) - hull.Min(q => q.X), hull.Max(q => q.Y) - hull.Min(q => q.Y));
        for (int i = 0; i < 24; i++) { float m = (lo + hi) / 2; if (Erode(hull, m).Count > 0) lo = m; else hi = m; }
        return lo;
    }

    /// <summary>
    /// The rounded glass for a share: its core (the hull eroded by the radius, at most <see cref="MaxCorners"/> corners) and the radius;
    /// null for 0 or a hull that cannot take it (the glass keeps its own corners).
    /// </summary>
    public static (List<Vector2> Core, float R)? Shape(IReadOnlyList<Vector2> hull, int share)
    {
        if (share <= 0 || hull.Count < 3) return null;
        float w = hull.Max(q => q.X) - hull.Min(q => q.X), h = hull.Max(q => q.Y) - hull.Min(q => q.Y);
        float r = Math.Min(share / 100f * Math.Min(w, h), 0.97f * Inradius(hull));
        if (r <= 0) return null;
        var core = Erode(hull, r);
        return core.Count is >= 3 and <= MaxCorners ? (core, r) : null;
    }
}
