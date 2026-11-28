namespace I76.Game.Sim;

/// <summary>
/// How a modern puff ends: never while it is still visibly opaque. A puff that ends with its life (Smoke Linger Normal's damage smoke and
/// burning wrecks, Normal's rocket and Ball O'Flame trails) swells a little more and its opacity eases to nothing over the last part of its
/// life (<see cref="Out"/>, <see cref="Swell"/>), so it fades and spreads away (1.6 times its end size by its end, the last quarter of its life fading) rather than vanishing at about half its opacity, as the shader's
/// erosion alone left it. Under Long the puff lives on as haze (SmokeHaze), which has its own fade, so neither applies. A puff the pool needs
/// back (<see cref="Retire"/>: the pool near full) and one leaving its draw distance (<see cref="Edge"/>) fade out the same way. The original's
/// sprites (the 1997 look) end at their 20th frame as they always did. Rendering only: nothing here reaches the simulation or the wire.
/// </summary>
public static class SmokeFade
{
    /// <summary>The share of a puff's life after which its opacity eases out (to nothing at the end).</summary>
    public static readonly float OutFrom = Tune(0, 0.75f, 0.2f, 0.95f);

    /// <summary>How much larger than its end size a puff is when it has faded out (it goes on spreading as it thins).</summary>
    public static readonly float SwellBy = Tune(1, 0.6f, 0f, 1.5f);

    /// <summary>I76_SMOKE_FADE="from,swell" sets <see cref="OutFrom"/> and <see cref="SwellBy"/> for a run (a tuning sheet).</summary>
    private static float Tune(int k, float d, float lo, float hi) =>
        System.Environment.GetEnvironmentVariable("I76_SMOKE_FADE")?.Split(',') is { Length: 2 } v
        && float.TryParse(v[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? Math.Clamp(f, lo, hi) : d;

    /// <summary>A puff's opacity factor at <paramref name="life"/> (0 to 1 of its life): 1 until <see cref="OutFrom"/>, then easing to 0 at 1.</summary>
    public static float Out(float life)
    {
        float t = Math.Clamp((life - OutFrom) / (1f - OutFrom), 0f, 1f);
        return 1f - t * t * (3f - 2f * t);
    }

    /// <summary>A puff's size factor at <paramref name="life"/>: 1 until <see cref="OutFrom"/>, then growing to 1 + <see cref="SwellBy"/> at the end.</summary>
    public static float Swell(float life)
    {
        float t = Math.Clamp((life - OutFrom) / (1f - OutFrom), 0f, 1f);
        return 1f + SwellBy * t * (2f - t);
    }

    /// <summary>A puff toward the far end of its draw distance <paramref name="far"/> at view depth <paramref name="depth"/> thins out over the last fifth instead of vanishing at it.</summary>
    public static float Edge(float depth, float far) => SmokeHaze.Far(depth, far);

    /// <summary>Seconds a puff takes to fade out when the pool takes it back.</summary>
    public const float RetireSeconds = 0.6f;

    /// <summary>The share of a pool in use above which the oldest puffs are faded out to make room before the ring reaches them.</summary>
    public const float RetireAbove = 0.85f;

    /// <summary>How many slots ahead of the ring's next one start fading when the pool is over <see cref="RetireAbove"/> (of a pool of <paramref name="pool"/>).</summary>
    public static int RetireAhead(int pool) => Math.Max(8, pool / 32);

    /// <summary>Whether a pool of <paramref name="pool"/> with <paramref name="live"/> puffs should start retiring its oldest.</summary>
    public static bool Crowded(int live, int pool) => live > RetireAbove * pool;

    /// <summary>A retiring puff's opacity factor with <paramref name="left"/> seconds of its fade to go (no fade: 1).</summary>
    public static float Retire(float left)
    {
        if (left <= 0f) return 1f;
        float t = Math.Clamp(left / RetireSeconds, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
