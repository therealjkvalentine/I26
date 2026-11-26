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
    /// The prototypes on top of On, for the owner to pick from (I76_ROAD_STYLE, letters, any order; none: today's On), as bits of the global
    /// i76_road_style: n the night response (retroreflective markings and glints on the stones under the lamps), b stronger close-up asphalt,
    /// c wear lanes, an oil streak and sealant lines along the lanes (needs the road frame, baked only then), d a damp, glossier night.
    /// </summary>
    public const int Night = 1, Strong = 2, Lanes = 4, Damp = 8;

    public static int Style(string? env)
    {
        int s = 0;
        foreach (char c in (env ?? "").ToLowerInvariant())
            s |= c switch { 'n' => Night, 'b' => Strong, 'c' => Lanes, 'd' => Damp, _ => 0 };
        return s;
    }

    /// <summary>Whether the chunks' road frame is to be baked (the wear-lane prototype, c).</summary>
    public static bool WantsFrame => (Style(System.Environment.GetEnvironmentVariable("I76_ROAD_STYLE")) & Lanes) != 0;

    public static void Apply(bool setting)
    {
        RenderingServer.GlobalShaderParameterSet("i76_road_detail", Value(setting, System.Environment.GetEnvironmentVariable("I76_ROAD_DETAIL")));
        RenderingServer.GlobalShaderParameterSet("i76_road_style", Style(System.Environment.GetEnvironmentVariable("I76_ROAD_STYLE")));
    }
}
