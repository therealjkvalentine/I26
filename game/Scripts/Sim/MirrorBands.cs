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
/// across a 512x256 picture every second frame as the rear mirror's: the picture's inner edge looks FovDegrees / 2 - YawDegrees
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

