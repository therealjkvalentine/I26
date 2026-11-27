using Godot;

namespace I76.Game.Sim;

/// <summary>
/// The modern look's door mirror face ([port] side_mirror_face=proud; the default until the turned glass, side_mirror_face=turned): the glass and its thin dark rim as a plate of
/// its own on the housing's end, as tall as the end (its top and bottom on the end's top and bottom edges as the driver sees them)
/// and wider than it, its rounded sides standing out past the end's angled side edges so none of the end's corners shows, as a real
/// mirror glass on a shell a little smaller than it. Where the end has a top and bottom edge (the Piranha's left hexagon, the CC
/// cars' four-sided end) the face follows its outline: each angled side moved out by the reach and every corner rounded, the top
/// and bottom kept on the end's; an end seen nearly edge-on (the Piranha's right) gets an upright rounded rectangle. Its frame is the
/// end's plane (or the plane the face is turned to, side_mirror_tilt_right / _left), in metres: x along the end's level edge, y
/// across it, up. The face's own units are its height: q = (x, y) / height from its middle, the rim RimShare of it. "inset" keeps
/// the glass inside the housing's end with the moulding's dark in its corners (the look of sections IS and JG).
/// </summary>
public static class DoorFace
{
    /// <summary>[port] side_mirror_face: "inset" the glass inside the housing (the look before); anything else, or none, the proud face.</summary>
    public static bool Proud(string? word) => string.Equals(word?.Trim(), "proud", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// [port] side_mirror_face: "turned", or none (the default since the playtest that preferred the turned heads' glass), the turned
    /// head's glass and its rounded ring (side_mirror_head=turned's, exactly) on the housing where the art places it; "proud" the
    /// proud face; "inset" the glass inside the housing's end.
    /// </summary>
    public static bool Turned(string? word) => !Proud(word) && !string.Equals(word?.Trim(), "inset", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// [port] side_mirror_face_over_left / _right: how far each rounded side stands past the least that covers the end's corners,
    /// percent of the face's height (0 just covers them).
    /// </summary>
    public const int DefaultOverLeft = 0, DefaultOverRight = 15, MaxOver = 60;

    /// <summary>I76_SIDE_MIRROR_FACE_OVER=L,R: each face's reach in place of the settings (a review sheet), within 0..MaxOver; null unset or unreadable.</summary>
    public static (int L, int R)? OverOverride(string? hook) =>
        hook?.Split(',') is { Length: 2 } s && int.TryParse(s[0], out int l) && int.TryParse(s[1], out int r) ? (Math.Clamp(l, 0, MaxOver), Math.Clamp(r, 0, MaxOver)) : null;

    /// <summary>The face's dark rim, a share of its height (the owner's paint-overs: a thin dark band, about 2 to 4 % of the glass's height).</summary>
    public const float RimShare = 0.03f;

    /// <summary>How far inside the face's rounded sides the end's corners lie at the least, in face heights (about a pixel at 3440x1440).</summary>
    public const float Margin = 0.01f;

    /// <summary>The face in the end's plane: its middle and axes (metres in the plane's own orthonormal frame), its height in metres, and in its own units (heights) its rounded outline, a convex core (counter-clockwise) widened by the corner radius R, and its width.</summary>
    public readonly record struct Shape(Vector2 Mid, Vector2 X, Vector2 Y, float Height, List<Vector2> Core, float R, float Width)
    {
        /// <summary>A point of the plane (metres) in the face's units.</summary>
        public Vector2 ToFace(Vector2 m) { var d = m - Mid; return new Vector2(d.Dot(X), d.Dot(Y)) / Height; }
        /// <summary>Whether a point in the face's units lies inside its rounded outline.</summary>
        public bool Inside(Vector2 q) => GlassRound.Distance(Core, q) <= R + 1e-5f;
        /// <summary>A point in the face's units in the plane (metres).</summary>
        public Vector2 ToPlane(Vector2 q) => Mid + (X * q.X + Y * q.Y) * Height;
    }

    /// <summary>
    /// The face's x axis: of the end's outline (<paramref name="hull"/>, in the plane's metres), the edge nearest square to
    /// <paramref name="up"/> (the world's up in the plane) where one lies within <see cref="LevelDegrees"/> of level (Edge true), so the
    /// face's top and bottom run along the end's own (the Piranha's left hexagon); else level in the plane (an end seen nearly edge-on,
    /// the Piranha's right, whose edges as projected on the face's plane all run steeply: the face then stands upright, its top and
    /// bottom at the end's highest and lowest points). Pointing so that its y (x turned a quarter counter-clockwise) is up.
    /// </summary>
    public static (Vector2 X, bool Edge) Level(IReadOnlyList<Vector2> hull, Vector2 up)
    {
        up = up.LengthSquared() > 1e-12f ? up.Normalized() : new Vector2(0, 1);
        var best = new Vector2(up.Y, -up.X); float bestDot = MathF.Sin(LevelDegrees * MathF.PI / 180f);
        bool edge = false;
        float span = Span(hull);
        for (int i = 0; i < hull.Count; i++)
        {
            var e = hull[(i + 1) % hull.Count] - hull[i];
            // an edge under a fifth of the outline's span is a chamfer, not the top
            if (e.Length() < 0.2f * span || e.LengthSquared() < 1e-12f) continue;
            e = e.Normalized();
            float d = MathF.Abs(e.Dot(up));
            if (d < bestDot) { bestDot = d; best = e; edge = true; }
        }
        var y = new Vector2(-best.Y, best.X);
        return (y.Dot(up) >= 0 ? best : -best, edge);
    }

    /// <summary>How far from level an edge of the end may run and still be the face's top or bottom, degrees.</summary>
    public const float LevelDegrees = 20f;

    private static float Span(IReadOnlyList<Vector2> hull)
    {
        float s = 0;
        foreach (var a in hull) foreach (var b in hull) s = MathF.Max(s, (a - b).Length());
        return s;
    }

    /// <summary>
    /// The face for an end's outline (<paramref name="hull"/>, plane metres): as tall as the outline across its level edge, its corners
    /// <paramref name="roundShare"/> percent of the height round (just under half at most), reaching far enough past the end's sides that
    /// every corner of the outline lies <see cref="Margin"/> inside it, and <paramref name="overShare"/> percent of the height further.
    /// Following the outline (Level's Edge): the outline's sides moved out by that reach, cut at its top and bottom, its corners rounded;
    /// otherwise a rounded rectangle as wide as the outline and the reach each side.
    /// </summary>
    public static Shape Fit(IReadOnlyList<Vector2> hull, Vector2 up, int roundShare, int overShare)
    {
        var (x, edge) = Level(hull, up);
        var y = new Vector2(-x.Y, x.X);
        float y0 = hull.Min(p => p.Dot(y)), y1 = hull.Max(p => p.Dot(y));
        float h = MathF.Max(y1 - y0, 1e-6f);
        float yc = (y0 + y1) / 2, xc = hull.Average(p => p.Dot(x));
        var mid0 = x * xc + y * yc;
        var pts = GlassRound.Hull(hull.Select(p => new Vector2((p - mid0).Dot(x), (p - mid0).Dot(y)) / h));
        float r = Math.Min(Math.Clamp(roundShare, 0, GlassRound.MaxShare) / 100f, MaxRound);
        float over = Math.Clamp(overShare, 0, MaxOver) / 100f;
        // every corner inside, half a Margin in from the sides; one on the top or bottom on the face's straight edge there, not past it
        bool Covers(List<Vector2> core) => core.Count >= 3 && pts.All(p => GlassRound.Distance(core, p) - r <= (MathF.Abs(p.Y) > 0.5f - 1e-3f ? 1e-6f : -0.5f * Margin));
        List<Vector2> Outline(float reach)
        {
            if (edge) return Rounded(Slab(Offset(pts, reach)), r);
            float left = pts.Min(p => p.X) - reach, right = pts.Max(p => p.X) + reach;
            if (right - left < 2 * r) { float c = (left + right) / 2; left = c - r; right = c + r; }
            return [new(left + r, -0.5f + r), new(right - r, -0.5f + r), new(right - r, 0.5f - r), new(left + r, 0.5f - r)];
        }
        float lo = 0, hi = 1;
        if (!Covers(Outline(hi))) hi = 3;
        for (int i = 0; i < 30; i++) { float m = (lo + hi) / 2; if (Covers(Outline(m))) hi = m; else lo = m; }
        var core = Outline(hi + over);
        float cx = (core.Min(p => p.X) + core.Max(p => p.X)) / 2, w = core.Max(p => p.X) - core.Min(p => p.X) + 2 * r;
        core = [.. core.Select(p => p - new Vector2(cx, 0))];
        return new Shape(mid0 + x * (cx * h), x, y, h, core, r, w);
    }

    /// <summary>The face's corners at most just under half its height round (half would leave its core no area).</summary>
    public const float MaxRound = 0.48f;

    /// <summary>A convex counter-clockwise polygon with every side moved outward by <paramref name="d"/>: each corner where its two sides' moved lines meet.</summary>
    public static List<Vector2> Offset(IReadOnlyList<Vector2> poly, float d)
    {
        int n = poly.Count;
        var res = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[(i + n - 1) % n], b = poly[i], c = poly[(i + 1) % n];
            Vector2 e0 = (b - a).Normalized(), e1 = (c - b).Normalized();
            Vector2 n0 = new(e0.Y, -e0.X), n1 = new(e1.Y, -e1.X);   // outward: the right of a counter-clockwise side
            // the corner moves along the two normals' bisector by d over the cosine of half the turn
            var bis = (n0 + n1).Normalized();
            float cosHalf = MathF.Max(bis.Dot(n0), 0.05f);
            res.Add(b + bis * (d / cosHalf));
        }
        return res;
    }

    /// <summary>A convex counter-clockwise polygon cut to -0.5 &lt;= y &lt;= 0.5 (the face's top and bottom).</summary>
    public static List<Vector2> Slab(List<Vector2> poly)
    {
        static List<Vector2> Clip(List<Vector2> pl, Func<Vector2, float> f)
        {
            var o = new List<Vector2>();
            for (int k = 0; k < pl.Count; k++)
            {
                var p = pl[k]; var q = pl[(k + 1) % pl.Count];
                float fp = f(p), fq = f(q);
                if (fp >= 0) o.Add(p);
                if ((fp >= 0) != (fq >= 0)) o.Add(p + (q - p) * (fp / (fp - fq)));
            }
            return o;
        }
        return Clip(Clip(poly, p => 0.5f - p.Y), p => p.Y + 0.5f);
    }

    /// <summary>A convex polygon's core for corners <paramref name="r"/> round: the polygon eroded by r (GlassRound.Erode), so the core widened by r is the polygon with its corners rounded.</summary>
    public static List<Vector2> Rounded(List<Vector2> poly, float r) =>
        poly.Count < 3 ? [] : r <= 0 ? GlassRound.Hull(poly) : GlassRound.Erode(GlassRound.Hull(poly), r);

    /// <summary>Where a ray from <paramref name="from"/> along <paramref name="dir"/> meets a triangle, in units of dir; null for a miss (either side faces).</summary>
    public static float? Hit(Vector3 from, Vector3 dir, Vector3 a, Vector3 b, Vector3 c)
    {
        var e1 = b - a; var e2 = c - a; var p = dir.Cross(e2);
        float det = e1.Dot(p);
        if (MathF.Abs(det) < 1e-12f) return null;
        float inv = 1f / det; var s = from - a;
        float u = s.Dot(p) * inv; if (u < 0 || u > 1) return null;
        var q = s.Cross(e1);
        float v = dir.Dot(q) * inv; if (v < 0 || u + v > 1) return null;
        float t = e2.Dot(q) * inv;
        return t > 0 ? t : null;
    }

    /// <summary>
    /// How much nearer the eye a face point must come, along the eye's ray through it, to stand <paramref name="clear"/> metres in front
    /// of the housing where that ray meets it (<paramref name="tris"/>, the head's triangles); negative where it stands further out,
    /// float.MinValue for a ray that misses the housing.
    /// </summary>
    public static float Short(Vector3 eye, Vector3 point, IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> tris, float clear)
    {
        var d = point - eye; float len = d.Length();
        if (len < 1e-6f) return 0;
        d /= len;
        float near = float.MaxValue;
        foreach (var (a, b, c) in tris) if (Hit(eye, d, a, b, c) is { } t && t < near) near = t;
        return near == float.MaxValue ? float.MinValue : len - (near - clear);
    }

    /// <summary>
    /// The turned glass (side_mirror_face=turned) in front of the art-placed housing: the share of its distance from the eye it is
    /// scaled to about the eye (which leaves it exactly where it was on screen) so that every sampled point of it (<paramref name="glass"/>'s
    /// triangles, a grid of 12 steps a side) stands <paramref name="clear"/> metres in front of the housing (<paramref name="housing"/>) along
    /// the eye's ray, and every housing corner seen through it lies behind it; 1 where it already does, at least 0.2.
    /// </summary>
    public static float TurnedShare(Vector3 eye, IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> glass, IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> housing, float clear)
    {
        float share = 1f;
        const int N = 12;
        foreach (var (a, b, c) in glass)
            for (int i = 0; i <= N; i++)
                for (int j = 0; i + j <= N; j++)
                {
                    var x = a + (b - a) * (i / (float)N) + (c - a) * (j / (float)N);
                    var d = x - eye; float len = d.Length(); if (len < 1e-5f) continue; d /= len;
                    foreach (var (ta, tb, tc) in housing)
                        if (Hit(eye, d, ta, tb, tc) is { } t) share = MathF.Min(share, (t - clear) / len);
                }
        foreach (var v in housing.SelectMany(t => new[] { t.A, t.B, t.C }).Distinct())
        {
            var d = v - eye; float len = d.Length(); if (len < 1e-5f) continue; d /= len;
            foreach (var (ta, tb, tc) in glass)
                if (Hit(eye, d, ta, tb, tc) is { } t) share = MathF.Min(share, (len - clear) / t);
        }
        return Math.Clamp(share, 0.2f, 1f);
    }

    /// <summary>
    /// The turned glass's ring as the eye sees it on the glass's plane, its lowest and highest along <paramref name="up"/>: the outer
    /// edge RoundGlass draws (GlassRound.EvenShape of <paramref name="cover"/> at the rounded glass's radius from <paramref name="hull"/>,
    /// <paramref name="over"/> past it).
    /// </summary>
    public static (float Lo, float Hi, List<Vector2> Core, float Reach) TurnedRing(IReadOnlyList<Vector2> hull, IReadOnlyList<Vector2> cover, Vector2 up, int share, float over, float margin, float ringW)
    {
        float r = GlassRound.Shape(hull, share)?.R ?? 0f;
        var es = GlassRound.EvenShape(cover, r, over, margin, ringW);
        float reach = es.E + es.Over;
        return (es.Core.Min(q => q.Dot(up)) - reach, es.Core.Max(q => q.Dot(up)) + reach, es.Core, reach);
    }

    /// <summary>
    /// The turned glass (side_mirror_face=turned) fitted within the art-placed housing's end as the eye sees it: its glass outline
    /// (<paramref name="hull"/>) and the outline its ring covers (<paramref name="cover"/>), picture units on the glass's plane, scaled
    /// about the cover's middle, widened across by up to 16 % where its height cannot grow, and moved (its rounded shape, ring and turn kept) so that the ring's top stands no higher than the
    /// housing's highest point near it and its bottom no lower than its lowest (<paramref name="housing"/>: the head's and its painted
    /// glass's points; those under the ring as placed, or within <paramref name="tuck"/> past it, are the end's, so the arm and the body
    /// further off do not count), while every corner of the end's painted glass it covered (<paramref name="corners"/>) stays under it, so no
    /// dark wedge of the end shows past it; the top and bottom come first (a rounded ring cannot cover a sharp highest corner without
    /// standing above it, so a hair of that corner may show). The least of the shortfalls is sought (a grid of scales and shifts, then finer); of
    /// the fits within a hair of the best, the largest and the least moved. Scale 1 and no shift where the ring already fits.
    /// Top and Bottom: how far the ring stood past the end before (positive: above its top, below its bottom); Left: the worst
    /// shortfall after (0 or less: none), all in picture units (the glass's heights).
    /// </summary>
    public static (List<Vector2> Hull, List<Vector2> Cover, float Scale, float Wide, Vector2 Shift, float Top, float Bottom, float Left) FitTurned(
        IReadOnlyList<Vector2> hull, IReadOnlyList<Vector2> cover, IReadOnlyList<Vector2> housing, IReadOnlyList<Vector2> corners, Vector2 up,
        int share, float over, float margin, float ringW, float tuck, IReadOnlyList<Func<Vector2, float>>? heights = null)
    {
        List<Vector2> h0 = [.. hull], c0 = [.. cover];
        if (housing.Count == 0 || c0.Count < 3 || up.LengthSquared() < 1e-12f) return (h0, c0, 1f, 1f, Vector2.Zero, 0f, 0f, 0f);
        up = up.Normalized();
        var side = new Vector2(up.Y, -up.X);
        var u = up;
        // how high a point stands on screen, for each view the glass is judged from (the eye's: picture units are not screen rows, a
        // foreshortened glass's highest point on its plane need not be its highest on screen); without them, along up on the plane
        var hs = heights is { Count: > 0 } ? heights : [q => q.Dot(u)];
        var first = TurnedRing(h0, c0, up, share, over, margin, ringW);
        bool Near((float Lo, float Hi, List<Vector2> Core, float Reach) r, Vector2 q) => GlassRound.Distance(r.Core, q) - r.Reach < tuck;
        var end = housing.Where(q => Near(first, q)).ToList();
        if (end.Count == 0) return (h0, c0, 1f, 1f, Vector2.Zero, 0f, 0f, 0f);
        var top = hs.Select(f => end.Max(f)).ToArray();
        var bottom = hs.Select(f => end.Min(f)).ToArray();
        var cov = corners.Where(q => Near(first, q)).ToList();
        if (cov.Count == 0) cov = end;
        // the ring's outer edge sampled, its highest and lowest on screen past the end's (the worst view)
        (float Top, float Bottom) Past(List<Vector2> core, float reach, Vector2 t)
        {
            var edge = GlassRound.Around(core, reach, 6);
            float pt = float.MinValue, pb = float.MinValue;
            for (int i = 0; i < hs.Count; i++)
            {
                float hi = float.MinValue, lo = float.MaxValue;
                foreach (var e in edge) { float y = hs[i](e + t); hi = MathF.Max(hi, y); lo = MathF.Min(lo, y); }
                pt = MathF.Max(pt, hi - top[i]); pb = MathF.Max(pb, bottom[i] - lo);
            }
            return (pt, pb);
        }
        var (topOver, bottomOver) = Past(first.Core, first.Reach, Vector2.Zero);
        if (topOver <= 0 && bottomOver <= 0) return (h0, c0, 1f, 1f, Vector2.Zero, topOver, bottomOver, MathF.Max(topOver, bottomOver));
        var mid = c0.Aggregate(Vector2.Zero, (x, y) => x + y) / c0.Count;
        // scaled by k about the middle and widened across by w (the sides wrap the end where the height cannot grow), as the ring draws it
        Vector2 Map(Vector2 q, float k, float w) { var d = q - mid; return mid + (side * (d.Dot(side) * w) + up * d.Dot(up)) * k; }
        var rings = new Dictionary<(int, int), (float Lo, float Hi, List<Vector2> Core, float Reach)>();
        (float Lo, float Hi, List<Vector2> Core, float Reach) Ring(float k, float w)
        {
            var key = ((int)MathF.Round(k * 10000), (int)MathF.Round(w * 10000));
            if (!rings.TryGetValue(key, out var r))
                rings[key] = r = TurnedRing([.. h0.Select(q => Map(q, k, w))], [.. c0.Select(q => Map(q, k, w))], up, share, over, margin, ringW);
            return r;
        }
        // the worst shortfall: a covered corner left past the ring, or (weighed fifty times: the top and bottom come first) the ring
        // past the end's top or bottom on screen
        float Short(float k, float w, float dx, float dy)
        {
            var r = Ring(k, w);
            var t = side * dx + up * dy;
            var (pt, pb) = Past(r.Core, r.Reach, t);
            float f = 50f * MathF.Max(MathF.Max(pt, pb), 0f);
            foreach (var q in cov) f = MathF.Max(f, GlassRound.Distance(r.Core, q - t) - r.Reach);
            return f;
        }
        // a grid of scales, widths and shifts, then a finer one round the pick; of the fits within a hair of the best, the least changed
        (float K, float W, float Dx, float Dy) Search(float k0, float k1, float ks, float w0, float w1, float ws, float x0, float x1, float y0, float y1, float ds, float hair)
        {
            var all = new List<(float F, float K, float W, float Dx, float Dy)>();
            for (float k = k0; k <= k1 + 1e-5f; k += ks)
                for (float w = w0; w <= w1 + 1e-5f; w += ws)
                    for (float dx = x0; dx <= x1 + 1e-5f; dx += ds)
                        for (float dy = y0; dy <= y1 + 1e-5f; dy += ds)
                            all.Add((Short(k, w, dx, dy), k, w, dx, dy));
            float fBest = MathF.Max(all.Min(q => q.F), 0f);
            var pick = all.Where(q => q.F <= fBest + hair).OrderBy(q => MathF.Abs(1 - q.K) + (q.W - 1) + MathF.Sqrt(q.Dx * q.Dx + q.Dy * q.Dy)).First();
            return (pick.K, pick.W, pick.Dx, pick.Dy);
        }
        var best = Search(0.82f, 1.02f, 0.02f, 1f, 1.16f, 0.04f, -0.12f, 0.12f, -0.12f, 0.12f, 0.02f, 0.003f);
        best = Search(best.K - 0.02f, MathF.Min(best.K + 0.02f, 1.02f), 0.004f, MathF.Max(1f, best.W - 0.04f), MathF.Min(1.16f, best.W + 0.04f), 0.02f,
            best.Dx - 0.02f, best.Dx + 0.02f, best.Dy - 0.02f, best.Dy + 0.02f, 0.004f, 0.0015f);
        var shift = side * best.Dx + up * best.Dy;
        List<Vector2> Move(List<Vector2> pts) => [.. pts.Select(q => Map(q, best.K, best.W) + shift)];
        return (Move(h0), Move(c0), best.K, best.W, new Vector2(best.Dx, best.Dy), topOver, bottomOver, Short(best.K, best.W, best.Dx, best.Dy));
    }

    /// <summary>The face's sample points in its own units: its rounded outline and a grid across it (where the housing may lie behind it).</summary>
    public static IEnumerable<Vector2> Samples(Shape s)
    {
        foreach (var q in GlassRound.Around(s.Core, s.R, 8)) yield return q;
        for (int i = 0; i <= 8; i++)
            for (int j = 0; j <= 4; j++)
            {
                var q = new Vector2((i / 8f - 0.5f) * s.Width, j / 4f - 0.5f);
                if (s.Inside(q)) yield return q;
            }
    }
}
