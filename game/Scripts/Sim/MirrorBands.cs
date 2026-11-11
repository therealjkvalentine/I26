namespace I76.Game.Sim;

/// <summary>
/// The side-view mirrors' shares of the one mirror render (docs/deviations.md, Side-view mirrors). The pass is drawn once, wider
/// than the rear-view mirror's 45 degrees (the original's), and each glass shows a band of its columns. The bands follow the
/// projection, not a linear split of the picture: a perspective picture's column is tan of its angle, so the rear mirror's 45 of a
/// 90-degree render is the middle tan(22.5°)/tan(45°) = 41.4 % of the width, not half (42 of 90 would be tan(21°)/tan(45°),
/// 38.4 %). Columns are the mirror's own, the picture flipped left-right (0 at the driver's left), so the left mirror's band lies
/// to the left of the rear's and the three glasses read as one view behind.
/// </summary>
public static class MirrorBands
{
    /// <summary>
    /// Glass and Quads: whether the door mirrors' painted glass (MirrorGlass: the Piranha's two, the CC cars' left one) shows the view
    /// and whether a car without any gets the two added glasses. Off: neither (the original's); On: the glass where the car has it, the
    /// added glasses where it has none; Auto (the default): the glass where the car has it, nothing added.
    /// </summary>
    public static (bool Glass, bool Quads) Shown(SideMirrorMode mode, bool hasGlass) => mode switch
    {
        SideMirrorMode.Off => (false, false),
        SideMirrorMode.On => (hasGlass, !hasGlass),
        _ => (hasGlass, false),
    };

    /// <summary>The render's width across by default, degrees: the rear mirror's 45 and about 22 a side, a real car's.</summary>
    public const float DefaultFov = 90f;
    /// <summary>Where a side mirror sits by default: degrees from straight ahead, and the margin kept to the window's edge.</summary>
    public const float DefaultYaw = 45f, EdgeMargin = 10f, MinYaw = 15f;
    /// <summary>How far below the rear mirror's row the side mirrors hang by default, in the rear mirror's heights: clear of it on a wide window.</summary>
    public const float DefaultDrop = 1.25f;
    /// <summary>How far into the rear mirror's view a side mirror reaches by default, degrees: a real side mirror shows the rear's edge again.</summary>
    public const float DefaultOverlap = 6f, MaxOverlap = 20f;

    /// <summary>The render's field across for a wanted one: within the original's set-up's 45..135, and never under the rear mirror's own 45.</summary>
    public static float Fov(float wanted) => MathF.Max(MirrorCameraSettings.ClampFov(wanted), MirrorCameraSettings.FovDegrees);

    /// <summary>A mirror's share of the render's columns for its angle: tan of its half angle over tan of the render's.</summary>
    public static float Share(float fov, float degrees) => MathF.Tan(Rad(degrees / 2)) / MathF.Tan(Rad(Fov(fov) / 2));

    /// <summary>The rear mirror's share of the width: its own 45 degrees' columns.</summary>
    public static float RearShare(float fov) => Share(fov, MirrorCameraSettings.FovDegrees);

    /// <summary>
    /// The render's width in pixels that keeps the rear mirror's own pixels per degree (<paramref name="rearWidth"/> across 45 degrees),
    /// to a multiple of 8: the rear band stays as many pixels wide whatever the whole is, and its height stays the picture's.
    /// </summary>
    public static int Width(float fov, int rearWidth) => (int)MathF.Round(rearWidth / RearShare(fov) / 8f) * 8;

    /// <summary>The rear mirror's band of columns: the middle RearShare.</summary>
    public static (float From, float To) Rear(float fov)
    {
        float r = RearShare(fov) / 2;
        return (0.5f - r, 0.5f + r);
    }

    /// <summary>
    /// A side mirror's band: from <paramref name="overlap"/> degrees inside the rear band's edge (what the rear mirror shows there
    /// again, as a real side mirror does) outward by <paramref name="share"/> (0.1 to 1) of the columns left on that side (1: to the
    /// render's edge), the left mirror's to the left of the rear's.
    /// </summary>
    public static (float From, float To) Side(float fov, float share, float overlap, bool left)
    {
        var (from, to) = Rear(fov);
        float inner = Share(fov, MirrorCameraSettings.FovDegrees - 2 * Math.Clamp(overlap, 0f, MaxOverlap)) / 2;   // the half-share of the rear's view less the overlap each side
        float s = Math.Clamp(share, 0.1f, 1f) * from;   // what is left on a side is the rear band's own margin
        return left ? (from - s, 0.5f - inner) : (0.5f + inner, to + s);
    }

    /// <summary>
    /// A door mirror's glass band: from <paramref name="overlap"/> degrees inside the rear band's edge outward, as many columns as the
    /// glass is wide for the picture's height (<paramref name="aspect"/> its width over its height as the driver sees it,
    /// <paramref name="renderAspect"/> the render's), so the view is not stretched in the glass; within the render.
    /// </summary>
    public static (float From, float To) Glass(float fov, float overlap, float aspect, float renderAspect, bool left)
    {
        float inner = Share(fov, MirrorCameraSettings.FovDegrees - 2 * Math.Clamp(overlap, 0f, MaxOverlap)) / 2;
        float w = Math.Clamp(aspect / MathF.Max(renderAspect, 1e-3f), 0.01f, 0.5f - inner);
        return left ? (0.5f - inner - w, 0.5f - inner) : (0.5f + inner, 0.5f + inner + w);
    }

    /// <summary>The angles off the render's axis a band's edges see, radians (the cylindrical remap draws a glass's columns evenly in angle): a column's angle is atan of its offset from the middle times tan of the half field.</summary>
    public static (float From, float To) Angles((float From, float To) band, float fov)
    {
        float t = MathF.Tan(Rad(Fov(fov) / 2));
        return (MathF.Atan((band.From * 2 - 1) * t), MathF.Atan((band.To * 2 - 1) * t));
    }

    /// <summary>Where a side mirror sits: the wanted yaw from straight ahead, kept inside the window's view (the half field across less the margin), degrees.</summary>
    public static float Yaw(float wanted, float halfFovAcross) => MathF.Min(wanted, MathF.Max(halfFovAcross - EdgeMargin, MinYaw));

    /// <summary>The window's half field across, degrees, for a camera that keeps its height: atan of tan(half the vertical field) times the aspect.</summary>
    public static float HalfFovAcross(float verticalFov, float aspect) => Deg(MathF.Atan(MathF.Tan(Rad(verticalFov / 2)) * aspect));

    private static float Rad(float d) => d * MathF.PI / 180f;
    private static float Deg(float r) => r * 180f / MathF.PI;
}

/// <summary>[port] side_mirrors: off (the original's), on, auto (the default): MirrorBands.Shown.</summary>
public enum SideMirrorMode { Off, On, Auto }

/// <summary>[port] side_mirror_render: shared, each side mirror a band of the rear mirror's one render; own (the default), a camera of its own at the door mirror looking back along the car's side (MirrorBands.OwnCamera).</summary>
public enum SideMirrorRender { Shared, Own }

/// <summary>
/// The side mirrors' own cameras (side_mirror_render=own, the default): one at each door mirror's
/// height and length, OutboardMetres outboard of the glass (the nearer the eye is to the flank's line, the more of the flank lies
/// inside the picture's inner edge: at 20 cm and more none of it does), looking back along the car's side turned YawDegrees outward and PitchDegrees down, FovDegrees
/// across a 512x256 picture, the two drawn in turn on the rear mirror's ticks (each every fourth frame): the picture's inner edge looks FovDegrees / 2 - YawDegrees
/// inboard of straight back (InboardDegrees), so the car's flank is a sliver at that edge and the rest is the adjoining lane well
/// back, as a real door mirror shows; the flank is the car's own body, drawn for these cameras alone (CarNode.OwnBodyLayer).
/// I76_OWN_MIRROR=fov,yaw,outboard,pitch sets the four for a review sheet (docs/dev-hooks.md).
/// </summary>
public static class OwnCamera
{
    public const float FovDegrees = 40f, OutboardMetres = 0.05f, YawDegrees = 15f, PitchDegrees = 3f;
    public const int Width = 512, Height = 256;
    /// <summary>How far inboard of straight back the picture's inner edge looks: where the flank shows.</summary>
    public const float InboardDegrees = FovDegrees / 2 - YawDegrees;

    /// <summary>The camera's forward in the car's frame (x right, y up, z back as the engine's frame has it): back, turned outward and down.</summary>
    public static System.Numerics.Vector3 Forward(bool left, float yawDegrees = YawDegrees, float pitchDegrees = PitchDegrees)
    {
        float d = (left ? -yawDegrees : yawDegrees) * MathF.PI / 180f, p = pitchDegrees * MathF.PI / 180f;
        return new(MathF.Sin(d) * MathF.Cos(p), -MathF.Sin(p), MathF.Cos(d) * MathF.Cos(p));
    }

    /// <summary>The camera's forward in the car's frame for a yaw outward and a pitch down (Forward), as a Godot vector (the car's back is +Z).</summary>
    public static Godot.Vector3 ForwardGodot(bool left, float yawDegrees, float pitchDegrees)
    {
        var f = Forward(left, yawDegrees, pitchDegrees);
        return new Godot.Vector3(f.X, f.Y, f.Z);
    }

    /// <summary>fov, yaw, outboard, pitch: the constants, or I76_OWN_MIRROR's four where it gives them (a review sheet's variants).</summary>
    public static (float Fov, float Yaw, float Outboard, float Pitch) Aim(string? hook)
    {
        var a = (Fov: FovDegrees, Yaw: YawDegrees, Outboard: OutboardMetres, Pitch: PitchDegrees);
        if (hook?.Split(',') is not { Length: 4 } s) return a;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var n = System.Globalization.NumberStyles.Float;
        return float.TryParse(s[0], n, c, out float f) && float.TryParse(s[1], n, c, out float y) && float.TryParse(s[2], n, c, out float o) && float.TryParse(s[3], n, c, out float p)
            ? (Math.Clamp(f, 10f, 90f), Math.Clamp(y, 0f, 45f), Math.Clamp(o, 0f, 1f), Math.Clamp(p, -20f, 20f)) : a;
    }
}

/// <summary>
/// Each side mirror's aim from the driver's eye, as a real door mirror is set (side_mirror_render=own, the modern look). The picture
/// looks back along the car's side with the car's own flank filling FlankShare of its width at the inner edge (the flank's line is the
/// body's widest side seen from the camera, back to its rear end), PitchDegrees down; the door mirror's glass is then turned, with its
/// housing and about its own middle, to the plane that reflects the eye's ray at the glass's middle into that direction (its normal
/// halves the angle between the eye's ray and the picture's axis). The original's art sets the Piranha's glasses square to the car,
/// turned 4 degrees outward and tilted 14.5 down: from the driver's seat the right one is seen 79 degrees off its face, a sliver, and
/// would reflect the view 85 degrees to the side; turned to the plane, the Piranha's left glass turns 29 degrees (seen 34 off its face
/// instead of 60) and its right 36 (45 instead of 79: it is twice as far away and more to the side), the CC cars' left 16 (32 instead
/// of 48). Game axes throughout: x right, y up, z forward.
/// </summary>
public static class MirrorAim
{
    /// <summary>The share of the picture's width the car's own flank fills at the inner edge (an eighth: a sliver, as a driver sets it).</summary>
    public const float FlankShare = 0.125f;

    /// <summary>
    /// The picture's yaw outward from straight back for a camera at <paramref name="cam"/> beside a body <paramref name="halfWidth"/>
    /// to a side that ends at <paramref name="rearZ"/>: the inner edge FlankShare of the field inboard of the flank's line;
    /// <paramref name="inward"/> degrees more inboard on top.
    /// </summary>
    public static float YawDegrees(bool left, System.Numerics.Vector3 cam, float halfWidth, float rearZ, float fov, float inward = 0)
    {
        float s = left ? -1 : 1;
        float flank = MathF.Atan2(s * cam.X - halfWidth, MathF.Max(cam.Z - rearZ, 0.5f)) * 180f / MathF.PI;   // the flank's line, inboard of straight back
        return Math.Clamp(fov / 2 - (flank + FlankShare * fov) - inward, 0f, 45f);
    }

    /// <summary>The picture's axis: back, turned yaw outward and pitch down.</summary>
    public static System.Numerics.Vector3 Axis(bool left, float yawDegrees, float pitchDegrees)
    {
        var f = OwnCamera.Forward(left, yawDegrees, pitchDegrees);
        return new(f.X, f.Y, -f.Z);
    }

    /// <summary>The normal of the plane at <paramref name="centre"/> that reflects the eye's ray into <paramref name="axis"/> (facing away from the eye).</summary>
    public static System.Numerics.Vector3 Normal(System.Numerics.Vector3 eye, System.Numerics.Vector3 centre, System.Numerics.Vector3 axis) =>
        System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Normalize(centre - eye) - axis);

    /// <summary>What a plane of normal <paramref name="normal"/> at <paramref name="centre"/> reflects the eye's ray into.</summary>
    public static System.Numerics.Vector3 Reflect(System.Numerics.Vector3 eye, System.Numerics.Vector3 centre, System.Numerics.Vector3 normal)
    {
        var d = System.Numerics.Vector3.Normalize(centre - eye);
        return d - 2 * System.Numerics.Vector3.Dot(d, normal) * normal;
    }

    /// <summary>
    /// The turn of a door mirror's head about <paramref name="pivot"/> that brings its glass (middle <paramref name="centre"/>, normal
    /// <paramref name="artNormal"/> either way) to the plane reflecting the eye's ray into <paramref name="axis"/>: the shortest
    /// rotation between the two normals, solved twice so the plane is the one at the turned glass's middle.
    /// </summary>
    public static System.Numerics.Quaternion Turn(System.Numerics.Vector3 eye, System.Numerics.Vector3 centre, System.Numerics.Vector3 artNormal,
        System.Numerics.Vector3 pivot, System.Numerics.Vector3 axis)
    {
        var q = System.Numerics.Quaternion.Identity;
        for (int i = 0; i < 2; i++)
        {
            var at = pivot + System.Numerics.Vector3.Transform(centre - pivot, q);
            var want = Normal(eye, at, axis);
            var art = System.Numerics.Vector3.Dot(artNormal, want) < 0 ? -artNormal : artNormal;
            q = Between(System.Numerics.Vector3.Normalize(art), want);
        }
        return q;
    }

    /// <summary>The shortest rotation taking unit <paramref name="a"/> to unit <paramref name="b"/>.</summary>
    public static System.Numerics.Quaternion Between(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
    {
        var c = System.Numerics.Vector3.Cross(a, b);
        float w = 1 + System.Numerics.Vector3.Dot(a, b);
        return w < 1e-6f ? System.Numerics.Quaternion.Identity : System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(c.X, c.Y, c.Z, w));
    }

    /// <summary>A rotation in game axes as the same rotation in Godot's (z mirrored): the axis's x and y components change sign.</summary>
    public static Godot.Quaternion ToGodot(System.Numerics.Quaternion q) => new(-q.X, -q.Y, q.Z, q.W);

    /// <summary>
    /// I76_MIRROR_AIM: "art" leaves the glasses as the original's art sets them and the pictures at the fixed OwnCamera.YawDegrees (the
    /// aim before this rule, for a review sheet); "L,R" turns each glass that many degrees further to the driver (its picture twice
    /// that further inboard). Unset: the rule, nothing further.
    /// </summary>
    public static (bool Art, float InwardL, float InwardR) Hook(string? hook)
    {
        if (hook == "art") return (true, 0, 0);
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return hook?.Split(',') is { Length: 2 } s && float.TryParse(s[0], System.Globalization.NumberStyles.Float, c, out float l) && float.TryParse(s[1], System.Globalization.NumberStyles.Float, c, out float r)
            ? (false, Math.Clamp(l, -15f, 15f), Math.Clamp(r, -15f, 15f)) : (false, 0, 0);
    }
}

