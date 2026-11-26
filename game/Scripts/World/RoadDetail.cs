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

    public static void Apply(bool setting) =>
        RenderingServer.GlobalShaderParameterSet("i76_road_detail", Value(setting, System.Environment.GetEnvironmentVariable("I76_ROAD_DETAIL")));
}
