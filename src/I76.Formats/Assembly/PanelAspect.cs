using System.Numerics;
using I76.Formats.Geo;

namespace I76.Formats.Assembly;

/// <summary>
/// The shape a cockpit face gives its texture's texels. Every car's specials panel (the detail-5 SPC6 part, ZBKS_.MAP)
/// is a 266 x 181 mm quad, near the weapons panel's proportions, whose UVs take the specials art's 181 x 76 texels of its
/// 256 x 128 map: a texel is 1.63 times as tall as it is wide on the model, where the weapons and damage panels' are
/// square within 0.05. The 2D dash draws the same art 1:1.
/// </summary>
public static class PanelAspect
{
    /// <summary>
    /// A face's texel height over its width on the model (the part placed), for a texture of <paramref name="texW"/> x
    /// <paramref name="texH"/>: 1 where the face shows the art as painted. Null for a face without three distinct UVs.
    /// </summary>
    public static float? TexelAspect(ModelPart part, GeoFace face, int texW, int texH)
    {
        if (face.Vertices.Length < 3 || texW <= 0 || texH <= 0) return null;
        var (a, b, c) = (face.Vertices[0], face.Vertices[1], face.Vertices[2]);
        Vector3 pa = part.ToModel.Apply(part.Mesh.Vertices[a.Index]), e1 = part.ToModel.Apply(part.Mesh.Vertices[b.Index]) - pa,
            e2 = part.ToModel.Apply(part.Mesh.Vertices[c.Index]) - pa;
        float du1 = b.U - a.U, dv1 = b.V - a.V, du2 = c.U - a.U, dv2 = c.V - a.V, det = du1 * dv2 - du2 * dv1;
        if (MathF.Abs(det) < 1e-9f) return null;
        // the model's distance per unit of u and of v across the face, then per texel
        float perU = ((e1 * dv2 - e2 * dv1) / det).Length() / texW, perV = ((e2 * du1 - e1 * du2) / det).Length() / texH;
        return perU > 0 ? perV / perU : null;
    }

    /// <summary>
    /// The texel aspect of the first drawn face textured with <paramref name="stem"/> (ZBKS_ matches ZBKS_.MAP, not ZBKS_320.MAP) and
    /// the lowest v its faces take (the art's last row), or null.
    /// </summary>
    public static (float Aspect, float MinV)? Of(AssembledModel model, string stem, int texW, int texH, Func<ModelPart, bool>? include = null)
    {
        foreach (var p in model.Parts)
        {
            if (include is not null && !include(p)) continue;
            foreach (var f in p.Mesh.Faces)
                if (f.Texture.Equals(stem + ".MAP", StringComparison.OrdinalIgnoreCase) && TexelAspect(p, f, texW, texH) is { } r)
                    return (r, p.Mesh.Faces.Where(g => g.Texture.Equals(f.Texture, StringComparison.OrdinalIgnoreCase)).SelectMany(g => g.Vertices).Min(v => v.V));
        }
        return null;
    }

    /// <summary>
    /// The UV change that shows a face's art with square texels, the top edge (v = 1, the art's first row) and the width kept:
    /// v' = 1 - (1 - v) x aspect, so the face's height takes aspect times the rows and the art fills its top part. The face
    /// is opaque (its keyed texels drawn black, as the original's): below the art's last row it is not drawn (uv_floor), so the dash shows. As (scale u, scale v, offset u, offset v).
    /// </summary>
    public static Vector4 SquareFit(float aspect) => new(1, aspect, 0, 1 - aspect);
}
