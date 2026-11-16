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

    // ---- the moulding's outer edge rounded too ([port] side_mirror_rim_out) ----

    /// <summary>[port] side_mirror_rim_out's default: the moulding's outer edge this many of its widths before the halving (MirrorRim.OuterShare) beyond the rounded glass's outline.</summary>
    public const int DefaultRimOut = 2;
    /// <summary>The largest growth offered, in the moulding's widths.</summary>
    public const int MaxRimOut = 6;

    /// <summary>
    /// How the moulding's outer edge is drawn: Off, the housing's straight outline with the moulding's dark in the corners (the look
    /// before the outer edge was rounded); otherwise rounded, Widths of the moulding's own width beyond the rounded glass's outline
    /// (0: the rounded outline itself, inside the housing's), or as far as covers the housing's corners (Cover); outside it the housing's
    /// corners cut away (their painted glass not drawn), or with Dark the moulding's dark kept out to the housing's straight edge.
    /// </summary>
    public readonly record struct RimOut(bool On, float Widths, bool Cover = false, bool Dark = false)
    {
        public static readonly RimOut Housing = new(false, 0);
    }

    /// <summary>
    /// The outer edge: I76_SIDE_MIRROR_RIM_OUT where it is set (N widths, "in" for 0, "cover"; ",dark" after either keeps the housing's
    /// corners dark; "0" or "off" the housing's outline), else the setting (0 the housing's outline, else that many widths).
    /// </summary>
    public static RimOut Out(string? hook, int setting)
    {
        if (!string.IsNullOrWhiteSpace(hook))
        {
            var parts = hook.Split(',', StringSplitOptions.TrimEntries);
            bool dark = parts.Skip(1).Any(p => p.Equals("dark", StringComparison.OrdinalIgnoreCase));
            string w = parts[0].ToLowerInvariant();
            if (w is "off" or "0") return RimOut.Housing;
            if (w == "in") return new(true, 0, false, dark);
            if (w == "cover") return new(true, 0, true, dark);
            if (float.TryParse(w, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                return new(true, Math.Clamp(f, 0, MaxRimOut), false, dark);
        }
        return setting <= 0 ? RimOut.Housing : new(true, Math.Min(setting, MaxRimOut));
    }

    /// <summary>
    /// Where a pixel lies in the ring, as cockpit_mirror.gdshader draws it with the outer edge rounded: screen pixels in from the outer
    /// edge (the rounded outline widened by <paramref name="rimOut"/>; negative outside it), the outline's distance <paramref name="sd"/>
    /// over its gradient on screen (<paramref name="unitsPerPx"/>, picture units a screen pixel along it), and the ring's width in
    /// screen pixels, its picture width <paramref name="ringW"/> (MirrorRim.RingWidth) at the glass's scale as seen (<paramref name="pxPerUnit"/>,
    /// the mean scale across its outline round it). The glass shows from that width in, so its edge is the outer edge inset by one
    /// width all round, however the glass is foreshortened.
    /// </summary>
    public static (float In, float Width) RingOnScreen(float sd, float unitsPerPx, float rimOut, float ringW, float pxPerUnit) =>
        ((rimOut - sd) / Math.Max(unitsPerPx, 1e-7f), Math.Max(ringW * pxPerUnit, 1f));

    /// <summary>Screen pixels the ring's outer edge reaches past the pane's own outline (cockpit_mirror.gdshader's HULL_OVER), and the blend where the two meet (HULL_SMOOTH).</summary>
    public const float HullOver = 0.5f, HullSmooth = 1.5f;

    /// <summary>
    /// Screen pixels in from the ring's outer edge as cockpit_mirror.gdshader draws it: the rounded ring's (<paramref name="pxRing"/>,
    /// RingOnScreen's In) and the pane's own outline's (<paramref name="pxOutline"/>, its distance in over its gradient on screen),
    /// whichever is further in, blended over HullSmooth pixels where they meet: the outer edge takes in a housing corner sharper than
    /// the ring's and HullOver pixels past it, and elsewhere is the ring's.
    /// </summary>
    public static float OuterIn(float pxRing, float pxOutline)
    {
        float h = MathF.Max(HullSmooth - MathF.Abs(pxRing - (pxOutline + HullOver)), 0) / HullSmooth;
        return MathF.Max(pxRing, pxOutline + HullOver) + h * h * HullSmooth * 0.25f;
    }

    /// <summary>The distance from a point to a convex counter-clockwise polygon, 0 inside it.</summary>
    public static float Distance(IReadOnlyList<Vector2> poly, Vector2 p)
    {
        bool inside = true;
        float d = float.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var e = poly[(i + 1) % poly.Count] - a; var w = p - a;
            float len = e.LengthSquared();
            var q = len > 1e-12f ? w - e * Math.Clamp(w.Dot(e) / len, 0f, 1f) : w;
            d = Math.Min(d, q.Length());
            if (e.X * w.Y - e.Y * w.X < 0) inside = false;
        }
        return inside ? 0 : d;
    }

    /// <summary>
    /// The growth beyond the rounded outline (the core widened by <paramref name="r"/>) that takes in every corner of the hull: the
    /// housing's corners furthest outside the rounded glass.
    /// </summary>
    public static float Cover(IReadOnlyList<Vector2> hull, IReadOnlyList<Vector2> core, float r) =>
        hull.Count == 0 ? 0 : Math.Max(0, hull.Max(v => Distance(core, v)) - r);

    /// <summary>
    /// The points the grown glass's mesh is the hull of: the rounded ring's outer edge (the core widened by <paramref name="radius"/>) and
    /// the pane's own outline, each <paramref name="margin"/> further out (room for the edge's one-pixel blend). The ring's outer edge
    /// takes in the outline (cockpit_mirror.gdshader), so the moulding's dark reaches a housing corner sharper than the ring's.
    /// </summary>
    public static List<Vector2> GrownOutline(IReadOnlyList<Vector2> hull, IReadOnlyList<Vector2> core, float radius, float margin)
    {
        var mid = hull.Count == 0 ? Vector2.Zero : hull.Aggregate(Vector2.Zero, (a, b) => a + b) / hull.Count;
        return Hull([.. Around(core, radius + margin), .. hull.Select(q => q + (q - mid).Normalized() * margin)]);
    }

    /// <summary>
    /// A convex polygon holding the core widened by <paramref name="radius"/> (every point within it of the core): a circle of
    /// <paramref name="steps"/> points round each corner, on the polygon that holds the circle, their hull. The mesh the rounded
    /// moulding is drawn on when its outer edge reaches past the housing's.
    /// </summary>
    public static List<Vector2> Around(IReadOnlyList<Vector2> core, float radius, int steps = 16)
    {
        float far = radius / MathF.Cos(MathF.PI / steps);
        var pts = new List<Vector2>();
        foreach (var c in core)
            for (int k = 0; k < steps; k++)
            {
                float a = 2 * MathF.PI * k / steps;
                pts.Add(c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * far);
            }
        return Hull(pts);
    }

    /// <summary>
    /// The plane a pane lies in, as its picture coordinates (U, V: the shader's, u times the aspect and v) map onto it: the
    /// least-squares P = O + U·A + V·B through its corners, so a point outside the glass's own outline (the moulding grown past it)
    /// lands in the glass's plane with the picture coordinates the shader reads there.
    /// </summary>
    public static (Vector3 O, Vector3 A, Vector3 B)? Plane(IReadOnlyList<(Vector2 Uv, Vector3 P)> pts)
    {
        if (pts.Count < 3) return null;
        // the normal equations of [1 U V] against each of x, y, z
        double n = pts.Count, su = 0, sv = 0, suu = 0, svv = 0, suv = 0;
        foreach (var (uv, _) in pts) { su += uv.X; sv += uv.Y; suu += uv.X * uv.X; svv += uv.Y * uv.Y; suv += uv.X * uv.Y; }
        double[,] m = { { n, su, sv }, { su, suu, suv }, { sv, suv, svv } };
        double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        if (Math.Abs(det) < 1e-12) return null;
        double[] Solve(Func<Vector3, float> axis)
        {
            double b0 = 0, b1 = 0, b2 = 0;
            foreach (var (uv, p) in pts) { double c = axis(p); b0 += c; b1 += c * uv.X; b2 += c * uv.Y; }
            var b = new[] { b0, b1, b2 };
            var x = new double[3];
            for (int col = 0; col < 3; col++)
            {
                var k = (double[,])m.Clone();
                for (int row = 0; row < 3; row++) k[row, col] = b[row];
                x[col] = (k[0, 0] * (k[1, 1] * k[2, 2] - k[1, 2] * k[2, 1]) - k[0, 1] * (k[1, 0] * k[2, 2] - k[1, 2] * k[2, 0]) + k[0, 2] * (k[1, 0] * k[2, 1] - k[1, 1] * k[2, 0])) / det;
            }
            return x;
        }
        var X = Solve(p => p.X); var Y = Solve(p => p.Y); var Z = Solve(p => p.Z);
        return (new Vector3((float)X[0], (float)Y[0], (float)Z[0]), new Vector3((float)X[1], (float)Y[1], (float)Z[1]), new Vector3((float)X[2], (float)Y[2], (float)Z[2]));
    }
}
