namespace I76.Game.Sim;

/// <summary>
/// Graphic Detail's Smoke Linger ([port] smoke_haze, SmokeLingerRow), the modern look's: Long lets the smoke of the damaged cars and of
/// the burning wrecks (cars and scenery; DamageSmoke) hang in the air after a puff's own life as a spreading haze. A puff that has
/// lived its life (as Normal draws it) keeps growing, ever more slowly, as smoke diffuses (its area grows at a steady rate:
/// <see cref="Grow"/>), and its density thins as its area grows, the smoke in it conserved (density times area is its mass), so it
/// becomes a faint haze. It drifts with the wind, rises less as it cools, and goes only once it is below <see cref="Threshold"/>,
/// fading to nothing on the way (<see cref="Visible"/>): no hard end to its life, no pop. Neighbouring haze puffs of a size merge
/// (<see cref="Absorb"/>: the smaller hands its smoke to the larger over <see cref="AbsorbSeconds"/>, the larger moving and widening to
/// cover both), so a column of hundreds of puffs ends as a few wide, faint ones and the overdraw stays bounded; over the budget
/// (<see cref="Pressure"/>: haze quads' screen coverage or their count) the threshold rises, so the faintest, oldest go first. Normal
/// (the default) is today's: each puff ends with its life. The original's damage puff lives 20 frames; this is a deviation, and
/// rendering only: nothing here reaches the simulation or the wire. No engine: the tests check the rules.
/// </summary>
public static class SmokeHaze
{
    /// <summary>The setting (Graphic Detail's Smoke Linger Long); the modern look's only (the 1997 look's puffs are the original's).</summary>
    public static bool Long { get; set; }

    /// <summary>I76_SMOKE_HAZE=0/1 overrides the file for a run (a before | after).</summary>
    public static readonly bool? Hook = System.Environment.GetEnvironmentVariable("I76_SMOKE_HAZE") switch { "1" => true, "0" => false, _ => null };

    /// <summary>The setting now: the hook, else the file's.</summary>
    public static bool On => Hook ?? Long;

    /// <summary>A puff's density under which it is gone (the smoke layer's per-puff opacity is about two thirds of it at the centre).</summary>
    public const float Threshold = 0.022f;

    /// <summary>Seconds a lone puff of any density takes, after its own life, to thin to <see cref="Threshold"/> (merged ones, holding more smoke, last longer): [port] smoke_fade (SmokeStart).</summary>
    public static float HazeSeconds => SmokeStart.Fade;

    /// <summary>Long's puffs start denser than Normal's (wrecks, damage smoke, trails, the explosions' haze): their density times this ([port] smoke_start_density, SmokeStart).</summary>
    public static float DensBoost => SmokeStart.Density;

    /// <summary>Long's puffs start darker: their darkness (0 steam white .. 1 oily black) moved this far toward black.</summary>
    public const float DarkShift = 0.25f;

    /// <summary>Long's smoke layer caps a column's opacity here (Normal's 0.65), so the denser start shows in a thick column: rising with the start density (SmokeStart.Cap).</summary>
    public static float LongCap => SmokeStart.Cap(DensBoost);

    /// <summary>A fresh dark puff's extra blackness for the layer's shader (SmokeStart.Shade): Long's [port] smoke_start_dark, Normal's none.</summary>
    public static float FreshDark => On ? SmokeStart.Dark : 0f;

    /// <summary>A young puff's size (DamageSmoke's): Normal's growth from <paramref name="start"/> to <paramref name="end"/>, Long's narrower at the source ([port] smoke_start_width).</summary>
    public static float PuffSize(float start, float end, float life) => SmokeStart.Size(start, end, life, On ? SmokeStart.Width : 1f);

    /// <summary>The emitters' scatter at the source (positions and sideways drift): Long's narrowed with the start width.</summary>
    public static float Scatter => On ? SmokeStart.Width : 1f;

    /// <summary>A puff's starting density: Normal's as it is, Long's <see cref="DensBoost"/> times it (at most 2: past 1 the puff's soft edge fills in, the shader clamping its opacity at 1).</summary>
    public static float StartDens(float dens) => On ? MathF.Min(dens * DensBoost, 2f) : dens;

    /// <summary>A puff's starting darkness: Normal's as it is, Long's moved <see cref="DarkShift"/> of the way to black.</summary>
    public static float StartDark(float dark) => On ? dark + (1f - dark) * DarkShift : dark;

    /// <summary>The smoke layer's cap on a column's opacity: <paramref name="normal"/> (the I76_SMOKE_CAP hook's or 0.65), Long's higher unless the hook set it.</summary>
    public static float LayerCap(float normal, bool hooked) => On && !hooked ? MathF.Max(normal, LongCap) : normal;

    /// <summary>The time a merged puff takes to hand its smoke over.</summary>
    public const float AbsorbSeconds = 1.5f;

    /// <summary>Haze puffs of a size merge when nearer than this share of the larger one's size.</summary>
    public const float MergeReach = 0.75f;

    /// <summary>The rise's time constant in the haze (s): it slows to nothing as it cools.</summary>
    public const float CoolSeconds = 6f;

    /// <summary>The budget: haze quads' summed screen coverage (screens, before the depth test) and their count.</summary>
    public const float CoverBudget = 2f;
    public const int CountBudget = 2500;

    /// <summary>
    /// A puff's growth rate in the haze (its area, metres squared a second) from its size <paramref name="size"/> and density
    /// <paramref name="dens"/> at the end of its life: a lone puff thins to the threshold in <see cref="HazeSeconds"/>.
    /// </summary>
    public static float Growth(float size, float dens) => size * size * MathF.Max(dens / Threshold - 1f, 0f) / HazeSeconds;

    /// <summary>The size after <paramref name="dt"/> seconds: the area grows at a steady rate, so the size grows ever more slowly.</summary>
    public static float Grow(float size, float growth, float dt) => MathF.Sqrt(size * size + growth * dt);

    /// <summary>A puff's density from its smoke (density times area) and its size: conserved, it thins as the inverse of the area.</summary>
    public static float Density(float mass, float size) => mass / MathF.Max(size * size, 1e-4f);

    /// <summary>
    /// The drawn density of a puff of density <paramref name="dens"/>: as it is well above the threshold, easing to nothing at it
    /// (<paramref name="threshold"/>, raised by the budget's pressure), so the puff has faded out when it goes.
    /// </summary>
    public static float Visible(float dens, float threshold = Threshold)
    {
        float t = Math.Clamp((dens - threshold) / threshold, 0f, 1f);
        return dens * t * t * (3f - 2f * t);
    }

    /// <summary>Whether a puff is gone: below the threshold, where <see cref="Visible"/> is nothing.</summary>
    public static bool Retired(float dens, float threshold = Threshold) => dens <= threshold;

    /// <summary>
    /// Smoke <paramref name="dm"/> of a puff at <paramref name="from"/> of size <paramref name="fromSize"/> taken into the puff at
    /// <paramref name="at"/> of size <paramref name="size"/> and smoke <paramref name="mass"/>: the smoke adds, the place is the smokes'
    /// mean, the size covers both (their spreads plus their distances from the mean, as a second moment).
    /// </summary>
    public static void Absorb(ref System.Numerics.Vector3 at, ref float size, ref float mass, System.Numerics.Vector3 from, float fromSize, float dm)
    {
        if (dm <= 0f) return;
        float m = mass + dm;
        var mean = (at * mass + from * dm) / m;
        float s2 = (mass * (size * size + Spread * (at - mean).LengthSquared()) + dm * (fromSize * fromSize + Spread * (from - mean).LengthSquared())) / m;
        at = mean; size = MathF.Max(size, MathF.Sqrt(s2)); mass = m;
    }

    /// <summary>A puff's size as a spread: a soft disc's size squared against its variance (between a disc's 8 and a gaussian's few).</summary>
    private const float Spread = 4f;

    /// <summary>The share of the screen a quad of <paramref name="size"/> at view depth <paramref name="depth"/> covers (at most the screen), the vertical half-angle's tangent <paramref name="tanHalf"/>.</summary>
    public static float Cover(float size, float depth, float tanHalf, float aspect)
    {
        if (depth <= 0.05f) return 1f;
        float side = size / (2f * depth * tanHalf);
        return MathF.Min(side * side / MathF.Max(aspect, 0.1f), 1f);
    }

    /// <summary>The budget's pressure (1 within it): the larger of the haze's coverage and count against their budgets.</summary>
    public static float Pressure(float cover, int count) => MathF.Max(1f, MathF.Max(cover / CoverBudget, count / (float)CountBudget));

    /// <summary>The pressure eased toward <paramref name="target"/> over about two seconds, so a raised threshold fades the faintest out, never cuts them.</summary>
    public static float Ease(float now, float target, float dt) => now + (target - now) * MathF.Min(1f, dt / 2f);

    /// <summary>The shader's age for a haze puff (1 at the end of its life, then slowly on: the noise keeps turning, the erosion holds).</summary>
    public static float ShaderAge(float sinceLife, float life) => 1f + 0.3f * sinceLife / MathF.Max(life, 0.1f);

    /// <summary>An adopted puff's showing, <paramref name="t"/> seconds after its wait, over <paramref name="fadeIn"/> (0: at once).</summary>
    public static float In(float t, float fadeIn)
    {
        if (t < 0f) return 0f;
        if (fadeIn <= 0f) return 1f;
        float k = Math.Clamp(t / fadeIn, 0f, 1f);
        return k * k * (3f - 2f * k);
    }

    /// <summary>A haze puff by the camera thins as the camera comes inside it (a quad as large as the view, close up, is all overdraw and no shape).</summary>
    public static float Near(float depth, float size)
    {
        float t = Math.Clamp((depth / MathF.Max(size, 0.1f) - 0.35f) / 0.65f, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>A haze puff thins toward the far end of its draw distance instead of vanishing at it.</summary>
    public static float Far(float depth, float far)
    {
        float t = Math.Clamp((far - depth) / (0.2f * far), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>The smoke layer's soft edge against the scene for a puff of <paramref name="size"/>: wider for a wide haze, so it meets the ground softly.</summary>
    public static float Soft(float size, float normal) => On ? MathF.Max(normal, 0.3f * size) : normal;

    /// <summary>A size class for merging (puffs of a size merge; a wide one does not swallow a young one whole).</summary>
    public static int Level(float size) => (int)MathF.Floor(MathF.Log2(MathF.Max(size, 0.25f)));
}
