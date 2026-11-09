using Godot;

namespace I76.Game.Ui;

/// <summary>
/// The 3D dash's gunsight ring drawn as vector geometry (<see cref="ReticleArt"/>): where the hood's ring mesh (RTC1, or RTC6 in the binoculars)
/// sits, each point of the ring's design carried through the mesh's own texture mapping into the cockpit's space and projected by the eye's
/// camera, so it moves with the dash and is as large on the screen as the ring's quad is. The modern look only; the 1997 look keeps the mesh.
/// </summary>
public partial class CockpitReticle : Control
{
    public required Func<I76.Game.Sim.Cockpit?> Pit { get; init; }
    public required Func<bool> InCockpit { get; init; }

    private readonly Dictionary<MeshInstance3D, (Vector3 Origin, Vector3 U, Vector3 V)?> _frames = [];

    public override void _Ready()
    {
        AnchorRight = 1;
        AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>
    /// The ring quad's plane as texture space maps it: the mesh-local position of uv (0, 0) and the steps for one unit of u and of v
    /// (from three of its vertices that span the uv plane), or null when the mesh has no such triangle.
    /// </summary>
    public static (Vector3 Origin, Vector3 U, Vector3 V)? PlaneOf(Mesh mesh)
    {
        if (mesh.GetSurfaceCount() == 0) return null;
        var arrays = mesh.SurfaceGetArrays(0);
        var vs = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        if (vs.Length < 3 || uv.Length != vs.Length) return null;
        int j = 0;
        for (int i = 1; i < vs.Length; i++) if ((uv[i] - uv[0]).LengthSquared() > (uv[j] - uv[0]).LengthSquared()) j = i;
        int k = -1; float best = 1e-8f;
        var a = uv[j] - uv[0];
        for (int i = 0; i < vs.Length; i++)
        {
            float cross = MathF.Abs(a.X * (uv[i].Y - uv[0].Y) - a.Y * (uv[i].X - uv[0].X));
            if (cross > best) { best = cross; k = i; }
        }
        if (k < 0) return null;
        return PlaneFrom(vs[0], uv[0], vs[j], uv[j], vs[k], uv[k]);
    }

    /// <summary>The affine map from texture space to a plane through three vertices with their uvs (null if the uvs are collinear).</summary>
    public static (Vector3 Origin, Vector3 U, Vector3 V)? PlaneFrom(Vector3 p0, Vector2 t0, Vector3 p1, Vector2 t1, Vector3 p2, Vector2 t2)
    {
        var a = t1 - t0; var b = t2 - t0;
        float det = a.X * b.Y - a.Y * b.X;
        if (MathF.Abs(det) < 1e-9f) return null;
        // p = p0 + (u - u0) U + (v - v0) V; solve U and V from the two edges
        var da = p1 - p0; var db = p2 - p0;
        var u = (da * b.Y - db * a.Y) / det;
        var v = (db * a.X - da * b.X) / det;
        return (p0 - u * t0.X - v * t0.Y, u, v);
    }

    public override void _Draw()
    {
        if (!ReticleArt.Drawn || Pit() is not { Faithful: false, Binoculars: var bino } pit || !InCockpit()) return;
        var node = pit.Node3D;
        var ring = bino ? node.Rtc6 : node.Rtc1;
        if (ring is null || !ring.IsVisibleInTree() || node.CockpitCamera is not { Current: true } cam || ring.Mesh is not { } mesh) return;
        if (!_frames.TryGetValue(ring, out var plane)) _frames[ring] = plane = PlaneOf(mesh);
        if (plane is not { } pl) return;
        var gt = ring.GlobalTransform;
        Vector2? Map(Vector2 d)
        {
            var uv = (d + Vector2.One) * 0.5f;   // the design's half-tile units to the texture's 0..1
            var world = gt * (pl.Origin + pl.U * uv.X + pl.V * uv.Y);
            return cam.IsPositionBehind(world) ? null : cam.UnprojectPosition(world);
        }
        ReticleArt.Draw(this, bino ? ReticleArt.Fine : ReticleArt.Coarse, Map);
    }
}
