using Godot;

namespace I76.Game.World;

/// <summary>
/// Graphic Detail's Road Detail ([port] road_detail, Shell.RoadDetailRow): the modern terrain shader's paved-road surface
/// (terrain_modern.gdshaderinc, road_detail()) through the global i76_road_detail, so a change shows at once on every chunk. Off (0) the
/// shader reads none of it. The 1997 look's terrain shader has no such term. I76_ROAD_DETAIL (1 or 0) overrides the setting for a capture.
/// </summary>
public static class RoadDetail
{
    /// <summary>The global's value for the setting and the environment's override (null or anything else: the setting).</summary>
    public static float Value(bool setting, string? env) => env switch
    {
        "1" => 1f,
        "0" => 0f,
        _ => setting ? 1f : 0f,
    };

    /// <summary>
    /// The parts of Road Detail beyond the surface (A, always on with the row), as bits of the global i76_road_style: n the night response
    /// (the markings' retroreflection toward the lamps, glints on the stones under them), b the strong close-up asphalt, c the soft
    /// wheel-track and oil bands and the sealant lines along the lanes (needs the road frame, baked when c is on), d a damp, glossier
    /// night (a prototype only). On is n + c, Strong n + b + c. I76_ROAD_STYLE (letters, any order; "a" for none) overrides for a capture.
    /// </summary>
    public const int Night = 1, Strong = 2, Lanes = 4, Damp = 8;

    /// <summary>The style for the row (on, strong) unless the environment names one.</summary>
    public static int Style(string? env, bool on = true, bool strong = false)
    {
        if (env is null) return !on ? 0 : Night | Lanes | (strong ? Strong : 0);
        int s = 0;
        foreach (char c in env.ToLowerInvariant())
            s |= c switch { 'n' => Night, 'b' => Strong, 'c' => Lanes, 'd' => Damp, _ => 0 };
        return s;
    }


    /// <summary>
    /// Whether a mission built now bakes the chunks' road frame: Road Detail on with the lanes (MissionBuilder). From the saved settings
    /// and the environment, not <see cref="Apply"/>'s: a mission is built before its settings are applied at the spawn.
    /// </summary>
    public static bool WantsFrame
    {
        get
        {
            var s = I76.Game.Shell.ShellSettings.Load();
            bool on = Value(s.RoadDetail, System.Environment.GetEnvironmentVariable("I76_ROAD_DETAIL")) > 0f;
            return on && (Style(System.Environment.GetEnvironmentVariable("I76_ROAD_STYLE"), on, s.RoadDetailStrong) & Lanes) != 0;
        }
    }

    public static void Apply(bool setting, bool strong = false)
    {
        float v = Value(setting, System.Environment.GetEnvironmentVariable("I76_ROAD_DETAIL"));
        RenderingServer.GlobalShaderParameterSet("i76_road_detail", v);
        RenderingServer.GlobalShaderParameterSet("i76_road_style", Style(System.Environment.GetEnvironmentVariable("I76_ROAD_STYLE"), v > 0f, strong));
    }
}
