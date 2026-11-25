namespace I76.Game.Sim;

/// <summary>
/// The modern look's rear lamps' red light on the ground behind the car, at night: a spotlight at each lamp, looking back and
/// down (CarNode). The tail filament throws a faint glow a couple of metres; the brake's, several times as bright, a plain red
/// pool. Small beside the headlamps' beam: the pool lies a metre or two from the lamp, where the headlamps' light is spread
/// over 50 m of road. A night's light only: none in full sun, turned down with the preset's sun intensity at dusk.
/// </summary>
public static class RearLampLight
{
    /// <summary>The light's energy from the tail filament and from the brake's, at night.</summary>
    public const float TailEnergy = 0.03f, BrakeEnergy = 0.4f;
    /// <summary>What is left in full sun: nothing.</summary>
    public const float DayFloor = 0f;

    /// <summary>
    /// The light's energy at the tail and brake filaments' levels (0 to 1, LampRamp's): the tail's gives way to the brake's as
    /// it rises, as a dual-filament bulb's does, turned down by <paramref name="daylight"/> (Daylight) to nothing in full sun.
    /// </summary>
    public static float Energy(float tail, float brake, float daylight) =>
        (TailEnergy * tail * (1f - brake) + BrakeEnergy * brake) * Daylight.Scale(daylight, DayFloor);

    /// <summary>The pool's spotlight (Godot's SpotAngle is the cone's half-angle): its axis 38 degrees down, its edges 6 and 70 degrees down, 6 m.</summary>
    public const float PoolPitch = -38f, PoolAngle = 32f, PoolRange = 6f;

    /// <summary>
    /// The lamp's light further back, along the road: a second, low spotlight at each lamp, its axis Pitch down, Angle either side,
    /// out to Range, lighting the road evenly along it (Attenuation), turned Yaw outward. The pool ends 6 m behind the lamp, and the mirrors see no
    /// ground that near (MirrorGround): the rear mirror's picture, level and 45 degrees across a 4:1 bitmap as the original's,
    /// reaches only 6 degrees under the horizon, its nearest road 10 m or more behind the eye. The wash is what they show: the
    /// road 2 to 40 m back tinted red, faint with the tail lamps and brighter braking. It lights the ground alone (WashLayers): a car behind
    /// (or the own car's tail) in its cone would take it face on, a red glare beside the road's grazing share.
    /// </summary>
    public const float WashPitch = -3f, WashAngle = 14f, WashRange = 40f, WashAttenuation = -1f, WashYaw = 0f;
    /// <summary>
    /// The wash's energy for the pool's (Energy): the same tail-to-brake ratio, 0.6 of it. Its fall-off over distance is negative, against
    /// the grazing angle: a light h over the road lights it at distance d by h / d^(1 + attenuation), so at -1 the road is lit evenly out to
    /// the range (at 0, six times the energy, the road 10 m back took a third of what 3 m took, and a truck's mirror, whose nearest road
    /// is 30 m back over its bed, showed none of it).
    /// </summary>
    public const float WashShare = 0.6f;
    /// <summary>What the wash lights: the ground's layer (the terrain, the roads on it, the ground plants), not the cars or the scenery.</summary>
    public const uint WashLayers = I76.Game.World.TerrainNode.TerrainLayer;

    /// <summary>The wash's geometry and share as set, or I76_REAR_WASH's "share,pitch,angle,range,attenuation,yaw" (a tuning sheet; share 0: none).</summary>
    public static readonly (float Share, float Pitch, float Angle, float Range, float Attenuation, float Yaw) Wash = ParseWash(System.Environment.GetEnvironmentVariable("I76_REAR_WASH"));

    public static (float Share, float Pitch, float Angle, float Range, float Attenuation, float Yaw) ParseWash(string? hook)
    {
        var d = (WashShare, WashPitch, WashAngle, WashRange, WashAttenuation, WashYaw);
        if (hook?.Split(',') is not { Length: 6 } s) return d;
        var v = new float[6];
        for (int i = 0; i < 6; i++)
            if (!float.TryParse(s[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return d;
        return (Math.Clamp(v[0], 0f, 20f), Math.Clamp(v[1], -60f, 0f), Math.Clamp(v[2], 1f, 60f), Math.Clamp(v[3], 1f, 60f), Math.Clamp(v[4], -2f, 3f), Math.Clamp(v[5], 0f, 30f));
    }

    /// <summary>
    /// How far behind a light <paramref name="height"/> m over flat ground its cone (axis <paramref name="pitch"/> degrees, negative
    /// down; half-angle <paramref name="angle"/>) and <paramref name="range"/> light the ground straight back: nearest and furthest
    /// metres, or null when none of it is.
    /// </summary>
    public static (float Near, float Far)? GroundReach(float height, float pitch, float angle, float range)
    {
        float lo = -pitch + angle, hi = -pitch - angle;   // degrees down of the lower and upper edges
        if (lo <= 0f) return null;
        float near = height / MathF.Tan(MathF.Min(lo, 89.9f) * MathF.PI / 180f);
        float rangeFar = MathF.Sqrt(MathF.Max(range * range - height * height, 0f));
        float far = hi <= 0f ? rangeFar : MathF.Min(rangeFar, height / MathF.Tan(hi * MathF.PI / 180f));
        return far > near ? (near, far) : null;
    }

    /// <summary>
    /// The nearest ground a level mirror camera sees straight back: from <paramref name="height"/> m over the ground, its picture
    /// <paramref name="fovAcross"/> degrees across <paramref name="width"/> x <paramref name="heightPx"/> pixels, metres behind it.
    /// </summary>
    public static float MirrorGround(float height, float fovAcross, int width, int heightPx)
    {
        float tanDown = MathF.Tan(fovAcross * MathF.PI / 360f) * heightPx / width;
        return height / tanDown;
    }
}
