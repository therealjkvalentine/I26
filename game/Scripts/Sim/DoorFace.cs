using Godot;

namespace I76.Game.Sim;

/// <summary>
/// The modern look's door mirror face ([port] side_mirror_face=proud, the default): the glass and its thin dark rim as a plate of
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
    public static bool Proud(string? word) => !string.Equals(word?.Trim(), "inset", StringComparison.OrdinalIgnoreCase);

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
