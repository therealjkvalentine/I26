using Godot;
using Vec3 = System.Numerics.Vector3;

namespace I76.Game.Sim;

/// <summary>
/// Graphic Detail's Smoke Lighting ([port] smoke_lighting, SmokeLightingRow), the modern look's: the smoke and dust lit by the mission's
/// sun, its colour from its height (the air it comes through: white high up, warm low down; the moon's blue at night) and the sky's
/// ambient (the sky texture's mean colour), instead of one white level for the mission (scene_light). The smokes read it from the globals
/// i76_smoke_sun (rgb, w 1 when on) and i76_smoke_amb, the smoke layer from <see cref="Sun"/> and <see cref="Ambient"/>; off, w is 0 and
/// every smoke draws as before. The original's smoke is unlit sprites; the 1997 look never reads it.
/// </summary>
public static class SmokeLight
{
    /// <summary>On, in the modern look: the smokes take <see cref="Sun"/> and <see cref="Ambient"/>.</summary>
    public static bool On { get; private set; }
    /// <summary>The sun's light on smoke (linear, its level included); the ambient's.</summary>
    public static Vec3 Sun { get; private set; } = Vec3.One;
    public static Vec3 Ambient { get; private set; } = Vec3.One;

    private static bool _setting, _look1997;
    private static Vec3 _sun = Vec3.One, _amb = Vec3.One;

    /// <summary>The row and the look (ApplyGraphics, a look switch).</summary>
    public static void Apply(bool setting, bool look1997)
    {
        _setting = setting; _look1997 = look1997;
        Push();
    }

    /// <summary>The look, at a switch (the setting kept).</summary>
    public static void SetLook1997(bool look1997) { _look1997 = look1997; Push(); }

    /// <summary>A mission's light: its preset and the sky texture's mean colour (sRGB).</summary>
    public static void SetMission(I76.Formats.Mission.LightPreset light, Color skyAverage)
    {
        var sky = skyAverage.SrgbToLinear();
        (_sun, _amb) = Colours(light.IsDay, light.Intensity, Elevation(light), new Vec3(sky.R, sky.G, sky.B));
        Push();
    }

    private static void Push()
    {
        On = _setting && !_look1997;
        Sun = _sun; Ambient = _amb;
        RenderingServer.GlobalShaderParameterSet("i76_smoke_sun", new Vector4(_sun.X, _sun.Y, _sun.Z, On ? 1f : 0f));
        RenderingServer.GlobalShaderParameterSet("i76_smoke_amb", new Vector3(_amb.X, _amb.Y, _amb.Z));
        LightChanged?.Invoke();
    }

    /// <summary>The setting or the mission's light changed (ModernFx's dust tint follows).</summary>
    public static event Action? LightChanged;

    /// <summary>The sun's height over the horizon in degrees (the preset's light vector is (sin a, cos a, 0), a from the zenith).</summary>
    public static float Elevation(I76.Formats.Mission.LightPreset light) => 90f - light.ZenithAngle * 180f / MathF.PI;

    /// <summary>The mission's one level for its smoke today (DamageView.SceneLight): the scale both lights keep.</summary>
    public static float SceneLight(float intensity) => Math.Clamp(intensity * 1.5f, 0.25f, 1f);

    /// <summary>
    /// The sun's and the ambient's light on smoke for a mission's light: by day the sun white at noon and warmer the lower it is (each
    /// channel through the air mass of its height, Kasten and Young's, normalised to noon and to the luminance), the ambient the sky's
    /// colour, half grey, weaker with a low sun; by night the moon's blue and a dim sky. Both at the mission's level (<see cref="SceneLight"/>),
    /// so a noon's smoke is as bright as with the setting off.
    /// </summary>
    public static (Vec3 Sun, Vec3 Ambient) Colours(bool day, float intensity, float elevationDeg, Vec3 skyLinear)
    {
        float level = SceneLight(intensity);
        var sky = Vec3.Lerp(Vec3.One, Unit(skyLinear), 0.5f);
        if (!day) return (Unit(new Vec3(0.6f, 0.7f, 1f)) * level * 0.65f, sky * level * 0.45f);
        float e = Math.Clamp(elevationDeg, 1f, 90f);
        float mass = 1f / (MathF.Sin(e * MathF.PI / 180f) + 0.50572f * MathF.Pow(e + 6.07995f, -1.6364f));
        var k = new Vec3(0.08f, 0.2f, 0.45f);   // per unit air mass: red least, blue most (scattering and haze)
        var tint = new Vec3(MathF.Exp(-k.X * (mass - 1f)), MathF.Exp(-k.Y * (mass - 1f)), MathF.Exp(-k.Z * (mass - 1f)));
        float t = Math.Clamp((e - 5f) / 35f, 0f, 1f);
        return (Unit(tint) * level, sky * level * (0.4f + 0.6f * t * t * (3f - 2f * t)));
    }

    /// <summary>A colour at luminance 1 (Rec. 709); grey for none.</summary>
    public static Vec3 Unit(Vec3 c)
    {
        float l = Luminance(c);
        return l > 1e-4f ? c / l : Vec3.One;
    }

    public static float Luminance(Vec3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
}
