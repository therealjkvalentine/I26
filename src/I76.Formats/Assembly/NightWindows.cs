using System.Numerics;
using I76.Formats.Geo;

namespace I76.Formats.Assembly;

/// <summary>
/// The modern look's night: scenery the original shows lit at night without a light of its own in its definition (no LOBJ chunk):
/// a night house's lit windows and the pool of light painted on the ground beside them, plain-coloured faces the engine draws at
/// full brightness (FaceLight). A row of <see cref="Table"/>
/// names such a definition and the light each of those faces sheds (a warm omni just outside the face), so a night mission's
/// houses light the ground under their windows as its gas station's canopy does; the next lit building is a row here, in any
/// mission that places it under a night preset. The 1997 look takes none of these: the original has no such light.
/// </summary>
public static class NightWindows
{
    /// <summary>A lit face's light: its energy at night_station_light 100, its range in metres, its colour (linear).</summary>
    public sealed record Row(float Energy, float Range, float R, float G, float B);

    /// <summary>By definition stem: the night house (T12, M04, T06), its windows a tungsten yellow.</summary>
    public static readonly IReadOnlyDictionary<string, Row> Table = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase)
    {
        ["bhjhous1"] = new(5f, 14f, 1f, 0.82f, 0.5f),
    };

    /// <summary>How far outside its face a light stands, metres.</summary>
    public const float Stand = 0.6f;
    /// <summary>How far above a flat lit face (a painted pool of light on the ground) its light hangs, metres.</summary>
    public const float Lift = 1.5f;

    public static Row? For(string definition) => Table.GetValueOrDefault(Path.GetFileNameWithoutExtension(definition));

    /// <summary>
    /// Where the lights of a listed model stand, definition space: one per plain-coloured full-bright face, at the face's middle,
    /// <see cref="Stand"/> outside it along its normal, or <see cref="Lift"/> above a flat one.
    /// </summary>
    public static List<Vector3> Of(AssembledModel m)
    {
        var at = new List<Vector3>();
        foreach (var part in m.Parts)
            foreach (var f in part.Mesh.Faces)
            {
                if (f.Texture.Length > 0 || f.Unk_0x21 != 0 || !FaceLight.Unlit(f) || f.Vertices.Length == 0) continue;
                var mid = Vector3.Zero; int n = 0;
                foreach (var v in f.Vertices) if ((uint)v.Index < part.Mesh.Vertices.Length) { mid += part.Mesh.Vertices[v.Index]; n++; }
                if (n == 0) continue;
                var normal = new Vector3(f.Plane.X, f.Plane.Y, f.Plane.Z);
                if (normal.LengthSquared() < 1e-6f) continue;
                var out_ = Vector3.Normalize(part.ToModel.Rotate(normal));
                // a wall's face: just outside it; a flat one (the pool of light the original paints on the ground by a lit window): above it
                at.Add(part.ToModel.Apply(mid / n) + (MathF.Abs(out_.Y) > 0.7f ? new Vector3(0, Lift, 0) : out_ * Stand));
            }
        return at;
    }
}
