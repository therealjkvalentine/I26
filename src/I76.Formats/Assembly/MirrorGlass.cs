using System.Numerics;
using I76.Formats.Geo;

namespace I76.Formats.Assembly;

/// <summary>
/// The door mirrors' glass in a car's interior (detail 5): the faces of its MIRL / MIRR parts the original paints as glass, a flat
/// sky blue (untextured, blue well over red: the Piranha's left glass, four faces at (73, 204, 255); the CC cars' left one at
/// (130, 196, 220)) or a mirror picture of the part's own (a texture named MIR..., the Piranha's right glass, PP51MIRR). The rest of
/// those parts is the housing, in the body's colour. Of the stock cars only the Piranha's (both doors) and the CC cars' (the left
/// door) interiors have such parts; the original draws them as they are painted, with no view in them.
/// </summary>
public static class MirrorGlass
{
    /// <summary>One door mirror's glass: its side, its part, its faces' corners in the model's frame (game axes) and their mean normal.</summary>
    public sealed record Pane(bool Left, string Part, IReadOnlyList<Vector3[]> Faces, Vector3 Normal);

    /// <summary>Whether a part is a door mirror (…MIRL, …MIRR), and which side; the rear-view mirror (MIRI) is not one.</summary>
    public static bool? Side(string node) =>
        node.EndsWith("MIRL", StringComparison.OrdinalIgnoreCase) ? true : node.EndsWith("MIRR", StringComparison.OrdinalIgnoreCase) ? false : null;

    /// <summary>Whether a door mirror's face is its glass: a MIR texture of its own (not the rear mirror's ZMIRI art), or untextured and sky blue.</summary>
    public static bool IsGlass(GeoFace f) =>
        f.Texture.Length > 0
            ? f.Texture.Contains("MIR", StringComparison.OrdinalIgnoreCase) && !f.Texture.StartsWith("ZMIRI", StringComparison.OrdinalIgnoreCase)
            : f.B > f.R + 20 && f.B >= f.G;

    /// <summary>The door mirrors' glass panes of an interior, left first; none for a car without door mirror parts.</summary>
    public static List<Pane> Find(AssembledModel interior)
    {
        var panes = new List<Pane>();
        foreach (var p in interior.Parts)
        {
            if (Side(p.NodeName) is not { } left) continue;
            var faces = new List<Vector3[]>();
            var normal = Vector3.Zero;
            foreach (var f in p.Mesh.Faces.Where(IsGlass))
            {
                if (f.Vertices.Length < 3) continue;
                var pts = f.Vertices.Select(v => p.ToModel.Apply(p.Mesh.Vertices[v.Index])).ToArray();
                faces.Add(pts);
                normal += Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
            }
            if (faces.Count > 0) panes.Add(new Pane(left, p.NodeName, faces, normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : Vector3.UnitZ));
        }
        return [.. panes.OrderBy(q => q.Left ? 0 : 1)];
    }

    /// <summary>
    /// A pane's corners' picture coordinates as the driver sees the glass from <paramref name="eye"/> (game axes: x right, y up, z
    /// forward): u from the glass's left edge (0) to its right (1), v from its foot (0) to its top (1), each corner by its offset
    /// from the glass's middle along the eye's right and up across the line of sight; and the glass's width over its height as seen.
    /// </summary>
    public static (Vector2[][] Uv, float Aspect) Picture(Pane pane, Vector3 eye)
    {
        var all = pane.Faces.SelectMany(f => f).ToArray();
        var mid = all.Aggregate(Vector3.Zero, (a, b) => a + b) / all.Length;
        var d = Vector3.Normalize(mid - eye);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, d));
        var up = Vector3.Normalize(Vector3.Cross(d, right));
        float u0 = all.Min(q => Vector3.Dot(q - mid, right)), u1 = all.Max(q => Vector3.Dot(q - mid, right));
        float v0 = all.Min(q => Vector3.Dot(q - mid, up)), v1 = all.Max(q => Vector3.Dot(q - mid, up));
        float w = MathF.Max(u1 - u0, 1e-6f), h = MathF.Max(v1 - v0, 1e-6f);
        var uv = pane.Faces.Select(f => f.Select(q => new Vector2((Vector3.Dot(q - mid, right) - u0) / w, (Vector3.Dot(q - mid, up) - v0) / h)).ToArray()).ToArray();
        return (uv, w / h);
    }
}
