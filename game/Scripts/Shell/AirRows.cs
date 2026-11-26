namespace I76.Game.Shell;

/// <summary>
/// Graphic Detail's Haze, Glow and Reflections (the port's, Advanced, the modern look only; each Off by default, so the picture is
/// today's until a player picks one): the words, the file's keys and the rules, apart from the engine (EnvExtras applies them).
/// </summary>
public static class AirRows
{
    /// <summary>
    /// Haze ([port] haze): Off; Light* and Southwest* add aerial perspective from <see cref="HazeStart"/> over the map and the ring
    /// alike, toward the same horizon colour the ring's own haze uses (haze.gdshaderinc).
    /// </summary>
    public static readonly string[] HazeNames = ["Off", "Light" + ShellSettings.PortMark, "Southwest" + ShellSettings.PortMark];
    /// <summary>Glow ([port] glow): Off, or Lamps* (bloom above a threshold only lamps, flashes and fire reach).</summary>
    public static readonly string[] GlowNames = ["Off", "Lamps" + ShellSettings.PortMark];
    /// <summary>Reflections ([port] reflections): screen-space reflections, drawn only with Ambient Occlusion On (they share its normal pass).</summary>
    public static readonly string[] ReflectionNames = ["Off", "On" + ShellSettings.PortMark];
    /// <summary>The Reflections row's word while Ambient Occlusion is Off: the row is not active then.</summary>
    public const string NeedsAo = "Needs AO";

    /// <summary>Metres of clear air before the added haze begins.</summary>
    public const float HazeStart = 600f;

    /// <summary>
    /// A Haze level's added air: the share it takes at the far end (<c>Amount</c>) and the metres over which it gets two thirds of the
    /// way there (<c>Reach</c>), as 1 - exp(-(d - start) / reach), on top of the ring's own haze. Off is none.
    /// </summary>
    public static (float Amount, float Reach) Haze(int level) => Math.Clamp(level, 0, 2) switch
    {
        1 => (0.18f, 4000f),
        2 => (0.28f, 3000f),
        _ => (0f, 1f),
    };

    /// <summary>The added haze's share at <paramref name="metres"/> from the camera (the pass's own sum, for the tests and the docs).</summary>
    public static float HazeAt(int level, float metres)
    {
        var (amount, reach) = Haze(level);
        return amount * (1f - MathF.Exp(-MathF.Max(metres - HazeStart, 0f) / reach));
    }

    public static int ClampHaze(int v) => Math.Clamp(v, 0, HazeNames.Length - 1);
    public static int ClampGlow(int v) => Math.Clamp(v, 0, GlowNames.Length - 1);

    /// <summary>A click on Haze or Glow: the next value, past the last back to Off.</summary>
    public static void StepHaze(ShellSettings s) => s.Haze = (ClampHaze(s.Haze) + 1) % HazeNames.Length;
    public static void StepGlow(ShellSettings s) => s.Glow = (ClampGlow(s.Glow) + 1) % GlowNames.Length;

    /// <summary>Reflections draw: the row On, Ambient Occlusion On (its normal pre-pass is what SSR reads; without it SSR would cost the 5 ms itself).</summary>
    public static bool ReflectionsActive(ShellSettings s) => s.Reflections && s.AmbientOcclusion;

    /// <summary>The Reflections row's word: its value, or <see cref="NeedsAo"/> with Ambient Occlusion Off.</summary>
    public static string ReflectionWord(ShellSettings s) => s.AmbientOcclusion ? ReflectionNames[s.Reflections ? 1 : 0] : NeedsAo;

    /// <summary>A click on Reflections: the next value, nothing while Ambient Occlusion is Off.</summary>
    public static void StepReflections(ShellSettings s) { if (s.AmbientOcclusion) s.Reflections = !s.Reflections; }
}
