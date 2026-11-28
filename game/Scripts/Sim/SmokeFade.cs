namespace I76.Game.Sim;

/// <summary>
/// How a modern puff grows and ends: never while it is still visibly opaque. A puff that ends with its life (Smoke Linger Normal's damage
/// smoke, burning wrecks' smoke and rocket and Ball O'Flame trails) grows the whole of its life, gently and ever more slowly, to
/// <see cref="Grow"/> times its end size (<see cref="Size"/>: one smooth easing curve, no step where the fade begins), and its opacity
/// eases to nothing over the last <see cref="FadeShare"/> of its life (<see cref="Out"/>), so it spreads and thins away rather than
/// vanishing. Its size grows by time, not speed: the lives are long ([port] smoke_linger, 400 by default), so the growth rate in metres a
/// second stays under the earlier curve's (the 1.6 times swell over the last quarter, whose rate peaked where that swell began). Under
/// Long the puff lives on as haze (SmokeHaze), which has its own slow growth and fade, so neither applies. A puff the pool needs back
/// (<see cref="Retire"/>: the pool near full) and one leaving its draw distance (<see cref="Edge"/>) fade out the same way. The original's
/// sprites (the 1997 look) end at their 20th frame as they always did. Rendering only: nothing here reaches the simulation or the wire.
/// </summary>
public static class SmokeFade
{
    /// <summary>The share of a puff's life after which its opacity eases out (to nothing at the end): the last 40 % of it.</summary>
    public static readonly float OutFrom = Tune(0, 0.6f, 0.2f, 0.95f);

    /// <summary>The share of a puff's life over which it fades out.</summary>
    public static float FadeShare => 1f - OutFrom;

    /// <summary>How many times its end size (the size Smoke Linger Long's haze starts from) a puff ending with its life reaches by its end.</summary>
    public static readonly float Grow = Tune(1, 3f, 1f, 4f);

    /// <summary>
    /// The growth curve's easing: size = start + (Grow x end - start) x (1 - (1 - life)^Shape). Above 1 the growth slows with age to nothing
    /// at the end (a power just over 1: nearly even, easing off); the rate at birth is Shape times the mean rate.
    /// </summary>
    public static readonly float Shape = Tune(2, 1.2f, 1f, 3f);

    /// <summary>I76_SMOKE_FADE="from,grow,shape" sets <see cref="OutFrom"/>, <see cref="Grow"/> and <see cref="Shape"/> for a run (a tuning sheet).</summary>
    private static float Tune(int k, float d, float lo, float hi) =>
        System.Environment.GetEnvironmentVariable("I76_SMOKE_FADE")?.Split(',') is { Length: 3 } v
        && float.TryParse(v[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? Math.Clamp(f, lo, hi) : d;

    /// <summary>A puff's opacity factor at <paramref name="life"/> (0 to 1 of its life): 1 until <see cref="OutFrom"/>, then easing to 0 at 1.</summary>
    public static float Out(float life)
    {
        float t = Math.Clamp((life - OutFrom) / (1f - OutFrom), 0f, 1f);
        return 1f - t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The size of a puff that ends with its life, at <paramref name="life"/> (0 to 1): from <paramref name="start"/> to <see cref="Grow"/>
    /// times <paramref name="end"/>, easing (<see cref="Shape"/>).
    /// </summary>
    public static float Size(float start, float end, float life)
    {
        float k = 1f - MathF.Pow(1f - Math.Clamp(life, 0f, 1f), Shape);
        return start + (Grow * end - start) * k;
    }

    /// <summary>Its growth rate in metres a second at <paramref name="life"/> for a life of <paramref name="seconds"/>.</summary>
    public static float Rate(float start, float end, float life, float seconds) =>
        (Grow * end - start) * Shape * MathF.Pow(1f - Math.Clamp(life, 0f, 1f), Shape - 1f) / MathF.Max(seconds, 1e-3f);

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
