using System;
using Godot;

namespace I76.Game.Sim;

/// <summary>
/// Mirror Rate's Smooth values (the port's own, the modern look's): each mirror camera draws every Nth frame, the three in turn, and
/// between its draws the glass shows its last picture re-projected for how the camera has turned (and, with its depth, moved) since,
/// as a VR headset's timewarp does. The render is a little wider and taller than the glass shows (Overscan), so a turn between draws
/// brings in picture that was drawn; past that the picture's edge is stretched.
/// </summary>
public static class MirrorWarp
{
    /// <summary>The draw period of a Mirror Rate value: 2, 3 or 4 for the Smooth ones, 0 for the others (no re-projection).</summary>
    public static int Period(MirrorRate r) => r switch { MirrorRate.Smooth2 => 2, MirrorRate.Smooth3 => 3, MirrorRate.Smooth4 => 4, _ => 0 };

    /// <summary>
    /// The frame of the period a mirror draws on (0 the rear, 1 the left side, 2 the right): one camera a frame where the period allows
    /// (1/3: rear, left, right; 1/4: the same and a frame with none), the two side cameras together opposite the rear at 1/2.
    /// </summary>
    public static int Offset(int period, int mirror) => period <= 2 ? (mirror == 0 ? 0 : 1) : mirror % period;

    /// <summary>Whether a mirror draws on this frame of the count.</summary>
    public static bool Draws(int period, long frame, int mirror) => period <= 1 || frame % period == Offset(period, mirror);

    /// <summary>
    /// The render's half tangents over the glass's, across and up: room for the turn of the frames between draws (about 2.3 degrees
    /// a frame each side across at the 45-degree rear mirror, half that up; the edge stretched past it).
    /// </summary>
    public static (float X, float Y) Overscan(int period) => period <= 1 ? (1f, 1f) : (1f + 0.05f * (period - 1), 1f + 0.025f * (period - 1));

    /// <summary>A render size grown by the overscan, at least the glass's own.</summary>
    public static (int W, int H) Size(int w, int h, (float X, float Y) over) => (Math.Max(w, (int)MathF.Round(w * over.X)), Math.Max(h, (int)MathF.Round(h * over.Y)));

    /// <summary>
    /// The picture coordinates' map from what a camera at <paramref name="now"/> would see (u right, v down, 0..1 over the glass's
    /// field, half tangents <paramref name="shown"/>) to the texture drawn at <paramref name="then"/> (half tangents
    /// <paramref name="drawn"/>): a homography on (u, v, 1), the result divided by its z. Rotations only (camera bases, orthonormal,
    /// Godot's x right, y up, the view down -z); the move between is <see cref="Shift"/>'s.
    /// </summary>
    public static Basis Homography(Basis then, Basis now, (float X, float Y) shown, (float X, float Y) drawn)
    {
        var a = new Basis(new Vector3(2 * shown.X, 0, 0), new Vector3(0, -2 * shown.Y, 0), new Vector3(-shown.X, shown.Y, -1));
        var b = new Basis(new Vector3(0.5f / drawn.X, 0, 0), new Vector3(0, -0.5f / drawn.Y, 0), new Vector3(-0.5f, -0.5f, -1));
        return b * Turn(then, now) * a;
    }

    /// <summary>
    /// Whether a glass re-projects with its camera's depth (MirrorDepth): only while the depth effect is in the camera's compositor and
    /// its texture has been made. Otherwise the turn alone: an unmade texture reads as the engine's placeholder, a checkerboard of
    /// 0 and 1 metres, which put flat blocks of a single texel over the glass.
    /// </summary>
    public static bool DepthUsable(bool attached, bool made) => attached && made;

    /// <summary>The turn from the camera's frame now to its frame then: a direction seen now, in the camera's frame as it was.</summary>
    public static Basis Turn(Basis then, Basis now) => then.Transposed() * now;

    /// <summary>The camera's place now in its frame then (metres; x right, y up, -z ahead as it was).</summary>
    public static Vector3 Shift(Transform3D then, Vector3 nowOrigin) => then.Basis.Transposed() * (nowOrigin - then.Origin);

    /// <summary>The homography applied to a picture point (the shader's sum), for the tests.</summary>
    public static Vector2 Apply(Basis h, Vector2 uv)
    {
        var p = h * new Vector3(uv.X, uv.Y, 1);
        return new Vector2(p.X / p.Z, p.Y / p.Z);
    }

    /// <summary>
    /// A point at <paramref name="metres"/> along the view seen at <paramref name="uv"/> now, where it lies in the picture drawn then,
    /// the camera having turned and moved (the shader's depth step, solved exactly here for the tests).
    /// </summary>
    public static Vector2 Project(Transform3D then, Transform3D now, (float X, float Y) shown, (float X, float Y) drawn, Vector2 uv, float metres)
    {
        var d = new Vector3((2 * uv.X - 1) * shown.X, (1 - 2 * uv.Y) * shown.Y, -1);
        var world = now * (d * metres);
        var p = then.AffineInverse() * world;
        return new Vector2(0.5f + 0.5f * (p.X / -p.Z) / drawn.X, 0.5f - 0.5f * (p.Y / -p.Z) / drawn.Y);
    }

    /// <summary>
    /// The shader's depth step (mirror_view.gdshaderinc warp_uv), here for the tests: from the turn's guess, three times, the distance
    /// the render holds where the guess lands taken as the point's, the point along the view's ray now at that distance as seen then.
    /// </summary>
    public static Vector2 Iterate(Basis h, Basis turn, Vector3 shift, (float X, float Y) shown, (float X, float Y) drawn, Vector2 uv, Func<Vector2, float> metresAt, int steps = 3)
    {
        var r = Apply(h, uv);
        var d = turn * new Vector3((2 * uv.X - 1) * shown.X, (1 - 2 * uv.Y) * shown.Y, -1);
        for (int i = 0; i < steps; i++)
        {
            float z = metresAt(r);
            float s = (z + shift.Z) / MathF.Max(-d.Z, 1e-4f);
            var p = d * s + shift;
            r = new Vector2(0.5f + 0.5f * (p.X / MathF.Max(-p.Z, 1e-3f)) / drawn.X, 0.5f - 0.5f * (p.Y / MathF.Max(-p.Z, 1e-3f)) / drawn.Y);
        }
        return r;
    }
}

/// <summary>
/// [port] mirror_haze, Graphic Detail's Mirror Haze (the modern look's): the mirrors' glass a touch milky, as no mirror is perfect or
/// perfectly clean, so the glass does not look exactly like the landscape through the windscreen: a flat veil over the whole glass
/// (the blacks lifted a little, the contrast a little less, a faint warm-grey tint) and the slightest softening; no gradient or
/// pattern. Off, Low (the default, asked for on: barely seen), Med, High. The 1997 look's mirror is the original's, never hazed.
/// </summary>
public static class MirrorHazeRow
{
    public static readonly string[] Names = ["Off", "Low*", "Med*", "High*"];
    public const int Default = 1;
    public static int Clamp(int v) => Math.Clamp(v, 0, Names.Length - 1);
    /// <summary>The key's word; null for low, the default, which is not written.</summary>
    public static string? Word(int v) => Clamp(v) switch { 0 => "off", 2 => "med", 3 => "high", _ => null };
    /// <summary>Reads the key; the first release's words read forward (slight was today's Low, more today's High).</summary>
    public static int Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "off" => 0,
        "med" or "medium" => 2,
        "high" or "more" => 3,
        _ => Default,   // low, slight, absent, unknown
    };
    public static void Step(I76.Game.Shell.ShellSettings s) => s.MirrorHaze = (Clamp(s.MirrorHaze) + 1) % Names.Length;

    /// <summary>The veil's share (of the tint mixed flat over the picture), the softening's radius in the render's texels, the veil's colour (sRGB, as the shader's source_color takes it).</summary>
    public static (float Veil, float Blur, Vector3 Tint) Of(int level) => Clamp(level) switch
    {
        1 => (0.012f, 0.35f, Tint),    // Low: the first release's Slight
        2 => (0.023f, 0.475f, Tint),   // Med: halfway
        3 => (0.035f, 0.6f, Tint),     // High: the first release's More
        _ => (0f, 0f, Vector3.One),
    };

    /// <summary>A pale warm grey, the colour a little dust or an aged silvering lends the glass.</summary>
    public static readonly Vector3 Tint = new(0.62f, 0.60f, 0.56f);
}
