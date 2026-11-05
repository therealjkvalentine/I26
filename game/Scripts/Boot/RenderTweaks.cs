using Godot;

namespace I76.Game.Boot;

/// <summary>
/// Environment switches over the modern look's rendering, for A/B frame
/// profiles (tools/frame-profile.sh); none is set in play:
/// I76_SSAO=0|1 (1: on whatever Ambient Occlusion says), I76_SHADOWS=0, I76_SHADOW_DIST=metres, I76_SHADOW_SPLITS=1|2|4,
/// I76_SHADOW_ATLAS=texels, I76_MSAA=0|2|4|8, I76_TERRAIN_TIERS=0 (the terrain shader's one tier at every distance).
/// </summary>
public static class RenderTweaks
{
    private static string? Env(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    public static void ApplyModern(Godot.Environment env, DirectionalLight3D sun, Viewport vp)
    {
        if (Env("I76_SSAO") == "0") env.SsaoEnabled = false;
        else if (Env("I76_SSAO") == "1") env.SsaoEnabled = true;
        RenderingServer.GlobalShaderParameterSet("i76_terrain_tiers", Env("I76_TERRAIN_TIERS") == "0" ? 0f : 1f);
        if (Env("I76_SHADOWS") == "0") sun.ShadowEnabled = false;
        if (float.TryParse(Env("I76_SHADOW_DIST"), System.Globalization.CultureInfo.InvariantCulture, out float d)) sun.DirectionalShadowMaxDistance = d;
        if (Env("I76_SHADOW_SPLITS") is { } s)
            sun.DirectionalShadowMode = s switch
            {
                "1" => DirectionalLight3D.ShadowMode.Orthogonal,
                "2" => DirectionalLight3D.ShadowMode.Parallel2Splits,
                _ => DirectionalLight3D.ShadowMode.Parallel4Splits,
            };
        if (int.TryParse(Env("I76_SHADOW_ATLAS"), out int atlas)) RenderingServer.DirectionalShadowAtlasSetSize(atlas, true);
        if (Env("I76_MSAA") is { } m)
            vp.Msaa3D = m switch { "0" => Viewport.Msaa.Disabled, "2" => Viewport.Msaa.Msaa2X, "8" => Viewport.Msaa.Msaa8X, _ => Viewport.Msaa.Msaa4X };
    }
}
