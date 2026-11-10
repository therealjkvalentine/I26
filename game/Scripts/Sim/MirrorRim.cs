using Godot;

namespace I76.Game.Sim;

/// <summary>
/// A rim round a side mirror's glass (docs/deviations.md, Side-view mirrors): the moulding inside the housing's edge. The pane's
/// outline (the edges its faces do not share) is the housing's edge; it is drawn inward in the pane's plane by the rim's width, over
/// the glass's outer band, the ring between the two outlines as triangles, so the glass shows inside the lip. The rear mirror's own
/// frame is 6 of its art's 64 rows, about a tenth of its height; the side mirrors' rim is two thirds of that in the glass's own
/// height (RimShare), and its colour the rear frame's. The rim's UV.y is 0 at the housing's edge and 1 at the glass, for the bevel.
/// </summary>
public static class MirrorRim
{
    /// <summary>The rear mirror's frame: 6 rows of its art's 64.</summary>
    public const float RearFrameShare = 6f / 64f;
    /// <summary>The side mirrors' rim as a share of the glass's height: two thirds of the rear frame's.</summary>
    public const float RimShare = RearFrameShare * 2f / 3f;

    /// <summary>The outline of a set of faces: the edges used by one face only, chained into loops (each a list of corners in order).</summary>
    public static List<List<Vector3>> Outline(IReadOnlyList<Vector3[]> faces, float tolerance = 1e-4f)
    {
        var edges = new List<(Vector3 A, Vector3 B)>();
        foreach (var f in faces)
            for (int i = 0; i < f.Length; i++) edges.Add((f[i], f[(i + 1) % f.Length]));
        bool Same(Vector3 a, Vector3 b) => a.DistanceSquaredTo(b) <= tolerance * tolerance;
        var boundary = new List<(Vector3 A, Vector3 B)>();
        for (int i = 0; i < edges.Count; i++)
        {
            bool shared = false;
            for (int j = 0; j < edges.Count && !shared; j++)
                if (i != j && ((Same(edges[i].A, edges[j].A) && Same(edges[i].B, edges[j].B)) || (Same(edges[i].A, edges[j].B) && Same(edges[i].B, edges[j].A)))) shared = true;
            if (!shared) boundary.Add(edges[i]);
        }
        var loops = new List<List<Vector3>>();
        while (boundary.Count > 0)
        {
            var loop = new List<Vector3> { boundary[0].A, boundary[0].B };
            boundary.RemoveAt(0);
            for (bool grew = true; grew && boundary.Count > 0;)
            {
                grew = false;
                for (int i = 0; i < boundary.Count; i++)
                {
                    var (a, b) = boundary[i];
                    if (Same(a, loop[^1])) { loop.Add(b); boundary.RemoveAt(i); grew = true; break; }
                    if (Same(b, loop[^1])) { loop.Add(a); boundary.RemoveAt(i); grew = true; break; }
                }
            }
            if (loop.Count > 2 && Same(loop[0], loop[^1])) loop.RemoveAt(loop.Count - 1);
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }

    /// <summary>
    /// The ring inside a loop: each corner pulled inward by <paramref name="width"/> along the bisector of its two edges' outward
    /// normals in the loop's plane (toward the loop's centre), two triangles an edge, both windings. The vertices and their UVs
    /// (x along the ring by corner, y 0 at the loop, the housing's edge, and 1 at the inner edge, the glass).
    /// </summary>
    public static (Vector3[] Vertices, Vector2[] Uv) Ring(IReadOnlyList<Vector3> loop, float width)
    {
        int n = loop.Count;
        var centre = Vector3.Zero;
        foreach (var p in loop) centre += p;
        centre /= n;
        var normal = Vector3.Zero;
        for (int i = 0; i < n; i++) normal += (loop[i] - centre).Cross(loop[(i + 1) % n] - centre);
        normal = normal.LengthSquared() > 0 ? normal.Normalized() : Vector3.Up;
        var outer = new Vector3[n];   // the inner outline, the glass's edge under the lip
        for (int i = 0; i < n; i++)
        {
            var prev = loop[(i + n - 1) % n]; var next = loop[(i + 1) % n]; var p = loop[i];
            var n1 = Outward(p - prev, normal, p, centre); var n2 = Outward(next - p, normal, p, centre);
            var bis = (n1 + n2).LengthSquared() > 1e-8f ? (n1 + n2).Normalized() : n1;
            float cosHalf = Mathf.Max(bis.Dot(n1), 0.5f);   // a mitre, capped at twice the width on a sharp corner
            outer[i] = p - bis * (width / cosHalf);
        }
        var v = new List<Vector3>(); var uv = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var quad = new[] { (loop[i], new Vector2(i, 0)), (loop[j], new Vector2(i + 1, 0)), (outer[j], new Vector2(i + 1, 1)), (outer[i], new Vector2(i, 1)) };
            foreach (var k in new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }) { v.Add(quad[k].Item1); uv.Add(quad[k].Item2); }
        }
        return (v.ToArray(), uv.ToArray());
    }

    /// <summary>An edge's outward normal in the plane: perpendicular to the edge and the plane's normal, pointing away from the centre.</summary>
    private static Vector3 Outward(Vector3 edge, Vector3 normal, Vector3 at, Vector3 centre)
    {
        var o = edge.Cross(normal);
        if (o.LengthSquared() < 1e-10f) return (at - centre).Normalized();
        o = o.Normalized();
        return o.Dot(at - centre) < 0 ? -o : o;
    }

    /// <summary>The rim's width for a pane: RimShare of the glass's height (its extent along the loop plane's up).</summary>
    public static float Width(IReadOnlyList<Vector3> loop)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var p in loop) { lo = Mathf.Min(lo, p.Y); hi = Mathf.Max(hi, p.Y); }
        return Mathf.Max(hi - lo, 0.01f) * RimShare;
    }

    /// <summary>The rear mirror frame's colour: the mean of the art's opaque frame texels (not the surround, not the glass) in its top frame rows.</summary>
    public static (byte R, byte G, byte B) FrameColour(byte[] rgba, int w, int h)
    {
        var corner = new[] { rgba[0], rgba[1], rgba[2] };
        long r = 0, g = 0, b = 0, n = 0;
        int rows = Mathf.Max(1, (int)(h * RearFrameShare));
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (rgba[i + 3] < 128 || MirrorEdge.Surround(rgba, i, corner)) continue;
                r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; n++;
            }
        return n == 0 ? ((byte)30, (byte)30, (byte)33) : ((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}

