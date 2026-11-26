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

    /// <summary>
    /// [port] side_mirror_rim_out's default: 0, the housing's own outline (the moulding's dark filling the rounded glass's corners out
    /// to the housing's straight edge), so the housing reads as the 1997 look draws it and only the glass inside it is rounded. It was
    /// RoundedRimOut (sections GF to IJ), whose ring stands past the housing and cuts its corners away; the key still offers it.
    /// </summary>
    public const int DefaultRimOut = 0;
    /// <summary>The rounded outer edge reviewed for the turned heads: this many of the moulding's widths before the halving (MirrorRim.OuterShare) beyond the rounded glass's outline.</summary>
    public const int RoundedRimOut = 2;
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

    /// <summary>
    /// The ring's outer edge's shape: the pane's outline eroded by a corner radius and widened by it again, and <paramref name="grow"/>
    /// beyond that, as the glass's rounded shape (<paramref name="core"/>, <paramref name="r"/>) is; its corners as round as the
    /// glass's, or rounder in no corner, but tighter wherever a corner of the outline (the housing's edge) would stick out past it
    /// as a point: there the radius is the largest that keeps every corner of the outline <paramref name="margin"/> inside the outer
    /// edge. Along the straight runs the edge is where it was (the outline lies on them); only the corners differ.
    /// </summary>
    public static (List<Vector2> Core, float R) OuterShape(IReadOnlyList<Vector2> hull, List<Vector2> core, float r, float grow, float margin)
    {
        bool Holds(List<Vector2> c, float rr) => c.Count >= 3 && hull.All(v => Distance(c, v) - rr <= grow - margin);
        if (Holds(core, r)) return (core, r);
        float ro = r;
        for (int i = 0; i < 60; i++)
        {
            ro *= 0.9f;
            if (ro < 1e-4f) break;
            var c = Erode(hull, ro);
            if (c.Count is >= 3 and <= MaxCorners && Holds(c, ro)) return (c, ro);
        }
        // the outline itself, widened by grow: every corner inside it
        return ([.. hull], 0f);
    }

    /// <summary>
    /// The ring's outer edge as one smooth outline the same distance past the housing's outline all round (the default since a
    /// playtest asked for the border one thickness everywhere): the outline <paramref name="hull"/> eroded by a corner radius e and
    /// widened by e and <paramref name="over"/> again, so along every straight run the outer edge lies <paramref name="over"/> past
    /// the outline, and e the largest (at most <paramref name="r"/>, the glass's own) that keeps every corner of the outline
    /// <paramref name="margin"/> inside the edge. The glass's edge is this outer edge inset by the band's width (the shader's), so the
    /// band is one width all round and the glass grows to it; its corners' radius is e + over less the band's width
    /// <paramref name="ringW"/>, kept at least half the band (a smaller one would draw the glass's corner as a point), by reaching
    /// further past the outline where a corner is too sharp for that. Core and E as the shader's pane_ocore and pane_or, Over its rim_out.
    /// </summary>
    public static (List<Vector2> Core, float E, float Over) EvenShape(IReadOnlyList<Vector2> hull, float r, float over, float margin, float ringW)
    {
        bool Holds(float e, float o)
        {
            if (e <= 0) return o >= margin;
            var c = Erode(hull, e);
            return c.Count is >= 3 and <= MaxCorners && hull.All(v => Distance(c, v) - e <= o - margin + 1e-6f);
        }
        float lo = 0, hi = r;
        if (Holds(hi, over)) lo = hi;
        else
            for (int i = 0; i < 30; i++) { float m = (lo + hi) / 2; if (Holds(m, over)) lo = m; else hi = m; }
        // the glass's corners no tighter than half the band: the radius rho = e + over held there, over grown until the corners fit
        float rho = 1.5f * ringW;
        if (lo + over < rho && rho > 0)
        {
            float olo = over, ohi = rho;
            for (int i = 0; i < 30; i++) { float m = (olo + ohi) / 2; if (Holds(rho - m, m)) ohi = m; else olo = m; }
            over = ohi; lo = rho - ohi;
        }
        var core = lo > 0 ? Erode(hull, lo) : [.. hull];
        return (core, lo, over);
    }

    /// <summary>
    /// The outline the ring's outer edge covers: the glass's own outline and, of the housing's points (the door mirror head's
    /// vertices, picture units as seen from the eye, and their distance in front of the glass's plane, negative behind it), those
    /// within <see cref="HousingDepth"/> behind the glass: the housing's rim, which the eye sees past the glass's edge where the head
    /// is turned (the CC cars' right side, about 5 % of the glass's height); the arm it stands on, further back, is not covered.
    /// Their convex hull, counter-clockwise.
    /// </summary>
    public static List<Vector2> Housing(IReadOnlyList<Vector2> glass, IEnumerable<(Vector2 Uv, float Depth)> head)
    {
        var h = Hull([.. glass, .. head.Where(q => q.Depth >= -HousingDepth && q.Depth <= 0.01f).Select(q => q.Uv)]);
        return h.Count <= MaxCorners ? h : [.. glass];
    }

    /// <summary>How far behind the glass's plane a housing point is still the housing's rim (metres): the CC cars' housing is 1.6 cm deep, its arm 8 cm and more behind.</summary>
    public const float HousingDepth = 0.03f;

    /// <summary>
    /// A point as the eye sees it on a plane P = O + U·A + V·B: where the eye's ray through it meets the plane, as (U, V), and the
    /// point's distance in front of the plane along its normal (A × B, toward the eye when the eye is on that side); null when the ray
    /// runs along the plane.
    /// </summary>
    public static (Vector2 Uv, float Depth)? Seen(Vector3 eye, (Vector3 O, Vector3 A, Vector3 B) pl, Vector3 q)
    {
        var n = pl.A.Cross(pl.B).Normalized();
        if ((eye - pl.O).Dot(n) < 0) n = -n;
        var d = q - eye; float den = d.Dot(n); if (MathF.Abs(den) < 1e-6f) return null;
        var hit = eye + d * ((pl.O - eye).Dot(n) / den) - pl.O;
        float aa = pl.A.Dot(pl.A), ab = pl.A.Dot(pl.B), bb = pl.B.Dot(pl.B), ha = hit.Dot(pl.A), hb = hit.Dot(pl.B), det = aa * bb - ab * ab;
        if (MathF.Abs(det) < 1e-12f) return null;
        return (new Vector2((ha * bb - hb * ab) / det, (hb * aa - ha * ab) / det), (q - pl.O).Dot(n));
    }

    /// <summary>
    /// The box the glass's picture is spread over (UV: u 0..1 across, v 0..1 up): the glass's inside (<paramref name="core"/> widened
    /// by <paramref name="inner"/>, picture units of u times <paramref name="aspect"/>, v) where it reaches past the picture's own
    /// 0..1, so a glass grown past its pane shows the whole view squeezed a little rather than the picture's edge rows smeared.
    /// </summary>
    public static (Vector2 Min, Vector2 Max) PictureBox(IReadOnlyList<Vector2> core, float inner, float aspect)
    {
        var a = Around(core, Math.Max(inner, 0));
        return (new(Math.Min(0, a.Min(q => q.X) / aspect), Math.Min(0, a.Min(q => q.Y))), new(Math.Max(1, a.Max(q => q.X) / aspect), Math.Max(1, a.Max(q => q.Y))));
    }

    /// <summary>The ring's outer edge grown evenly instead (I76_MIRROR_RIM_POINTS=uniform): the glass's rounded shape widened just enough that every corner of the outline lies <paramref name="margin"/> inside it, or <paramref name="grow"/> where that is further.</summary>
    public static float UniformGrow(IReadOnlyList<Vector2> hull, IReadOnlyList<Vector2> core, float r, float grow, float margin) =>
        Math.Max(grow, Cover(hull, core, r) + margin);

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
    /// The points the grown glass's mesh is the hull of: the ring's outer edge (its core widened by <paramref name="radius"/>) and
    /// the pane's own outline, each <paramref name="margin"/> further out (room for the edge's one-pixel blend).
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
