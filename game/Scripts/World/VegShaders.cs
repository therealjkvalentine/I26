using Godot;

namespace I76.Game.World;

/// <summary>
/// The plants' shader (vegetation.gdshaderinc): alpha-tested, or with alpha-to-coverage at Anti-Aliasing 4x and 8x, where its
/// smoother leaf edges are bought with the frame time those choices already spend (0.28 ms at 3440x1440 on a GTX 1080 Ti in
/// the full-detail T01 cockpit, the plants then drawn in the transparent pass: over the budget for a default, docs/perf/plan.md).
/// Every plant material is made here and follows the choice when it changes.
/// </summary>
public static class VegShaders
{
    private static readonly List<WeakReference<ShaderMaterial>> Made = [];
    private static bool _coverage;

    /// <summary>Whether a choice of the Anti-Aliasing row (RenderScale.AntiAliasing: Off, 2x, 4x, 8x) draws the plants with alpha-to-coverage.</summary>
    public static bool CoverageFor(int antialias) => antialias >= 2;

    private static Shader Current => GD.Load<Shader>(_coverage ? "res://Shaders/vegetation_a2c.gdshader" : "res://Shaders/vegetation.gdshader");

    public static ShaderMaterial Make()
    {
        var m = new ShaderMaterial { Shader = Current };
        lock (Made) Made.Add(new(m));
        return m;
    }

    /// <summary>The choice in use (Main's ApplyModernQuality): every live plant material's shader swapped when it changes.</summary>
    public static void SetCoverage(bool on)
    {
        if (on == _coverage) return;
        _coverage = on;
        var s = Current;
        lock (Made)
        {
            Made.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var w in Made) if (w.TryGetTarget(out var m) && GodotObject.IsInstanceValid(m)) m.Shader = s;
        }
    }
}
