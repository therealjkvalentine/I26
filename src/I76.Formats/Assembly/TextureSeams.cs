using System.Numerics;

namespace I76.Formats.Assembly;

/// <summary>
/// Where two textures meet on a model, or a texture meets itself: every geometric edge two faces share (by position,
/// in model space) across which the picture changes, a different texture or the same texture at other UVs (two UV
/// islands of one texture meeting at a panel edge), with each side's UVs along the edge; and every texture a face
/// tiles (its UVs span more than one repeat, or cross a whole number). The HD pack's upscaler works one texture at a
/// time, so these are the places its edge handling can leave a seam (tools/upscale-textures.py --neighbours).
/// </summary>
public static class TextureSeams
{
    /// <summary>A textured polygon in model space: its corners and their UVs (as the GEO stores them), and its texture member.</summary>
    public sealed record Face(string Part, Vector3[] P, Vector2[] Uv, string Texture);

    /// <summary>One side of a seam: the texture, the UVs at the edge's two ends (in the edge's order) and the face's UV centroid (which side of the edge the face lies).</summary>
    public readonly record struct Side(string Texture, Vector2 Uv0, Vector2 Uv1, Vector2 Centre);

    /// <summary>An edge two faces share where the picture is not continuous: sides A and B, the edge's length, and the angle (degrees) between the two faces' normals.</summary>
    public sealed record Seam(string PartA, string PartB, Side A, Side B, float Length, float Angle);

    /// <summary>
    /// The seams among <paramref name="faces"/>: corners within <paramref name="weld"/> of each other are the same point;
    /// UVs within <paramref name="uvEps"/> the same UV. Faces that share an edge back to back (a double-sided panel,
    /// normals more than 170° apart) are not a seam. Each seam is listed once.
    /// </summary>
    public static List<Seam> Find(IReadOnlyList<Face> faces, float weld = 1e-4f, float uvEps = 1e-4f)
    {
        var keys = new Dictionary<(long, long, long), int>();
        int Key(Vector3 p)
        {
            var k = ((long)MathF.Round(p.X / weld), (long)MathF.Round(p.Y / weld), (long)MathF.Round(p.Z / weld));
            if (!keys.TryGetValue(k, out var i)) keys[k] = i = keys.Count;
            return i;
        }
        var normals = faces.Select(Normal).ToArray();
        // per edge (lower key first): every face's use of it, with the UVs at the lower and the higher key
        var uses = new Dictionary<(int, int), List<(int Face, Vector2 Lo, Vector2 Hi, Vector3 PLo, Vector3 PHi)>>();
        for (int fi = 0; fi < faces.Count; fi++)
        {
            var f = faces[fi];
            if (f.P.Length < 3 || f.Uv.Length != f.P.Length || f.Texture.Length == 0) continue;
            for (int i = 0; i < f.P.Length; i++)
            {
                int j = (i + 1) % f.P.Length, ka = Key(f.P[i]), kb = Key(f.P[j]);
                if (ka == kb) continue;
                var e = ka < kb ? (ka, kb) : (kb, ka);
                var u = ka < kb ? (fi, f.Uv[i], f.Uv[j], f.P[i], f.P[j]) : (fi, f.Uv[j], f.Uv[i], f.P[j], f.P[i]);
                if (!uses.TryGetValue(e, out var l)) uses[e] = l = [];
                l.Add(u);
            }
        }
        var o = new List<Seam>();
        var seen = new HashSet<(string, string, (int, int, int, int), (int, int, int, int))>();
        static (int, int, int, int) Q(Vector2 a, Vector2 b) => ((int)MathF.Round(a.X * 4096), (int)MathF.Round(a.Y * 4096), (int)MathF.Round(b.X * 4096), (int)MathF.Round(b.Y * 4096));
        foreach (var l in uses.Values)
            for (int x = 0; x < l.Count; x++)
                for (int y = x + 1; y < l.Count; y++)
                {
                    var (a, b) = (l[x], l[y]);
                    if (a.Face == b.Face) continue;
                    var (fa, fb) = (faces[a.Face], faces[b.Face]);
                    bool same = fa.Texture.Equals(fb.Texture, StringComparison.OrdinalIgnoreCase)
                        && Vector2.Distance(a.Lo, b.Lo) <= uvEps && Vector2.Distance(a.Hi, b.Hi) <= uvEps;
                    if (same) continue;
                    float angle = MathF.Acos(Math.Clamp(Vector3.Dot(normals[a.Face], normals[b.Face]), -1f, 1f)) * 180f / MathF.PI;
                    if (angle > 170f) continue;
                    // one listing per pair of sides, whichever face came first
                    var ka = (fa.Texture.ToLowerInvariant(), fb.Texture.ToLowerInvariant(), Q(a.Lo, a.Hi), Q(b.Lo, b.Hi));
                    var kb = (ka.Item2, ka.Item1, ka.Item4, ka.Item3);
                    if (seen.Contains(ka) || seen.Contains(kb)) continue;
                    seen.Add(ka);
                    o.Add(new Seam(fa.Part, fb.Part, new Side(fa.Texture, a.Lo, a.Hi, Centre(fa)), new Side(fb.Texture, b.Lo, b.Hi, Centre(fb)), Vector3.Distance(a.PLo, a.PHi), angle));
                }
        return o;
    }

    /// <summary>The axes (u, v) along which a face repeats its texture: its UVs span more than one repeat or cross a whole number.</summary>
    public static (bool U, bool V) Tiles(Face f, float eps = 1e-3f)
    {
        static bool Axis(IEnumerable<float> s, float eps)
        {
            float lo = s.Min(), hi = s.Max();
            return hi - lo > 1 + eps || MathF.Floor(lo + eps) < MathF.Floor(hi - eps);
        }
        return f.Uv.Length == 0 ? (false, false) : (Axis(f.Uv.Select(u => u.X), eps), Axis(f.Uv.Select(u => u.Y), eps));
    }

    private static Vector2 Centre(Face f) => f.Uv.Aggregate(Vector2.Zero, (a, b) => a + b) / f.Uv.Length;

    private static Vector3 Normal(Face f)
    {
        var n = Vector3.Zero;   // Newell's normal: any polygon, either winding
        for (int i = 0; i < f.P.Length; i++)
        {
            var (p, q) = (f.P[i], f.P[(i + 1) % f.P.Length]);
            n += new Vector3((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y));
        }
        return n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
    }

    /// <summary>A model's textured faces in model space, each slot resolved to its member at damage entry <paramref name="frame"/> (null where nothing resolves).</summary>
    public static List<Face> Faces(AssembledModel m, TextureResolver res, int frame = 0)
    {
        var o = new List<Face>();
        foreach (var part in m.Parts)
        {
            var placed = part.Mesh.Vertices.Select(part.ToModel.Apply).ToArray();
            foreach (var f in part.Mesh.Faces)
            {
                if (f.Texture.Length == 0 || f.Unk_0x20 == 0 || f.Vertices.Length < 3 || f.Vertices.Any(v => (uint)v.Index >= placed.Length)) continue;
                if (res.ResolveFace(f.Texture, m.ObjectCode, m.TmtNames, vtfMaps: m.VtfMaps, frame: frame) is not { } t) continue;
                o.Add(new Face(part.NodeName, [.. f.Vertices.Select(v => placed[v.Index])], [.. f.Vertices.Select(v => new Vector2(v.U, v.V))], t));
            }
        }
        return o;
    }
}
