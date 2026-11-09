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
