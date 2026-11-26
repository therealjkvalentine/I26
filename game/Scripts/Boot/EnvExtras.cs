using Godot;
using I76.Game.Shell;

namespace I76.Game.Boot;

/// <summary>
/// Graphic Detail's Haze, Glow and Reflections on the modern look's environment (AirRows has the rows; each Off by default, and Off
/// sets nothing the frame draws: no pass, no glow, no SSR). The 1997 look has an environment of its own and never shows them.
/// <para>
/// Glow: Godot's glow on the HDR frame before the tonemap, at a threshold set in the calibration's scene light (the light after the
/// exposure, where the card curve shows 0.8 as it is and clips toward 2, LightingCalibration.Shoulder): above <see cref="DayThreshold"/>
/// by day, which sunlit sand and paint do not reach, and <see cref="NightThreshold"/> at night, which the dash and the moonlit ground
/// do not reach either; lamps, muzzle flashes, fire and explosions, drawn additively over 1, do. Screen blend, no bloom, the middle
/// levels only: a soft halo, never a wash.
/// </para>
/// <para>Reflections: Godot's SSR, which reads the normal and roughness pre-pass Ambient Occlusion already draws (alone it would cost
/// that pass, docs/perf/costs.md); Godot leaves surfaces rougher than 0.7 out, which is the ground, the roads and matte paint.</para>
/// Hooks for the comparison sheets: I76_HAZE=0|1|2, I76_GLOW=0|1, I76_SSR=0|1 over the rows; I76_GLOW_THRESHOLD=v (scene light),
/// I76_GLOW_VIEW=1 (the glow alone, Godot's Replace blend), I76_HAZE_AMOUNT / I76_HAZE_REACH.
/// </summary>
public static class EnvExtras
{
    public const float DayThreshold = 1.6f, NightThreshold = 1.1f;

    private static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
    private static float? EnvF(string name) => float.TryParse(Env(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : null;
    private static int? EnvI(string name) => int.TryParse(Env(name), out int i) ? i : null;

    /// <summary>The rows' values with the hooks over them.</summary>
    public static int HazeLevel(int row) => AirRows.ClampHaze(EnvI("I76_HAZE") ?? row);
    public static int GlowLevel(int row) => AirRows.ClampGlow(EnvI("I76_GLOW") ?? row);
    public static bool Ssr(bool row) => EnvI("I76_SSR") is { } v ? v != 0 : row;

    /// <summary>
    /// Glow and SSR on the modern environment. <paramref name="sceneOverGodot"/> is the scene light's exposure over the tonemap's
    /// (the card curve's headroom under Card, 1 under Filmic), so the threshold given in scene light becomes Godot's, which compares
    /// the frame times the tonemap's exposure.
    /// </summary>
    public static void Apply(Godot.Environment env, int glow, bool reflections, float sceneOverGodot)
    {
        bool g = GlowLevel(glow) > 0 && !RenderBackend.Compatibility;
        env.GlowEnabled = g;
        if (g)
        {
            float day = I76.Game.Sim.Daylight.Current;
            float v = EnvF("I76_GLOW_THRESHOLD") ?? Mathf.Lerp(NightThreshold, DayThreshold, Mathf.Clamp(day * 2f, 0f, 1f));
            env.GlowHdrThreshold = v / Mathf.Max(sceneOverGodot, 1e-3f);
            env.GlowHdrScale = 1.5f / Mathf.Max(sceneOverGodot, 1e-3f);
            env.GlowHdrLuminanceCap = 12f;
            env.GlowBloom = 0f;
            env.GlowIntensity = 0.7f;
            env.GlowStrength = 1f;
            env.GlowNormalized = false;
            env.GlowBlendMode = Env("I76_GLOW_VIEW") == "1" ? Godot.Environment.GlowBlendModeEnum.Replace : Godot.Environment.GlowBlendModeEnum.Screen;
            float[] levels = [0f, 0.6f, 0.8f, 0.6f, 0.3f, 0f, 0f];
            for (int i = 0; i < levels.Length; i++) env.SetGlowLevel(i, levels[i]);
        }
        // SSR only over the normal pass Ambient Occlusion draws (the I76_SSAO hook included: it has been applied by now)
        env.SsrEnabled = Ssr(reflections) && env.SsaoEnabled && !RenderBackend.Compatibility;
    }
}

/// <summary>
/// Graphic Detail's Haze: the full-screen pass (aerial_haze.gdshader), made the first time Haze is on, drawn while it is on and the
/// horizon ring shows (the modern look at Max or 2.3 km; at Far the sky fog has the distance). The ring's haze colour, gain, sun and
/// light are copied into it each frame it draws, so the two agree whatever the calibration, the night or the hour do to the ring.
/// </summary>
public partial class AerialHaze : MeshInstance3D
{
    private ShaderMaterial _mat = null!;
    /// <summary>The ring whose haze parameters the pass takes.</summary>
    public World.HorizonTerrain? Ring { get; set; }
    /// <summary>The look and the Visibility Range allow it (the modern look with the ring).</summary>
    public bool Allowed { get; set; }
    public int Level { get; set; }

    public static AerialHaze Create()
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/aerial_haze.gdshader"), RenderPriority = (int)Material.RenderPriorityMin };
        return new AerialHaze
        {
            Name = "AerialHaze", _mat = mat, Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = mat, Visible = false,
            CastShadow = ShadowCastingSetting.Off, CustomAabb = new Aabb(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f)),
            Layers = 1 | World.TerrainNode.SkyFogLayer,
        };
    }

    private static readonly string[] Copied = ["horizon_color", "haze_gain", "sprite_dir", "light_strength"];

    public override void _Process(double delta)
    {
        int level = EnvExtras.HazeLevel(Level);
        bool on = level > 0 && Allowed && Ring is { } r && IsInstanceValid(r) && r.IsInsideTree() && r.Visible && r.Material is not null;
        if (Visible != on) Visible = on;
        if (!on) return;
        var ring = Ring!.Material;
        foreach (var p in Copied) _mat.SetShaderParameter(p, ring.GetShaderParameter(p));
        var (amount, reach) = AirRows.Haze(level);
        _mat.SetShaderParameter("start", AirRows.HazeStart);
        _mat.SetShaderParameter("amount", EnvExtrasHooks.Amount ?? amount);
        _mat.SetShaderParameter("reach", EnvExtrasHooks.Reach ?? reach);
    }
}

internal static class EnvExtrasHooks
{
    private static float? F(string n) => float.TryParse(System.Environment.GetEnvironmentVariable(n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : null;
    public static readonly float? Amount = F("I76_HAZE_AMOUNT"), Reach = F("I76_HAZE_REACH");
}
