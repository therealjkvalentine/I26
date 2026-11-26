using Godot;

namespace I76.Game.Sim;

/// <summary>
/// Shader parameter names set every frame, made once. A string passed where the engine takes a StringName makes a new one each call, and a
/// StringName is finalizable and tracked by the engine's bindings: each survives its first garbage collection, so a few dozen a frame were
/// a steady stream of survivors that made every young collection longer.
/// </summary>
public static class ShaderNames
{
    public static readonly StringName SceneLight = "scene_light", SunView = "sun_view", Modern = "modern", Fade = "fade", Daylight = "daylight", Age = "age";
    /// <summary>A tracer's instance parameters (ModernFx.SetTracer, every round every frame).</summary>
    public static readonly StringName TailFall = "tail_fall", Tail = "tail", Head = "head", Tint = "tint", Width = "width", Energy = "energy";
}
