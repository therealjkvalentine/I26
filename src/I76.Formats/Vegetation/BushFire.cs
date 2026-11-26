namespace I76.Formats.Vegetation;

/// <summary>
/// A burning plant's look in the modern look's brush impacts (World/BrushImpacts.cs; a port addition: the original's plants
/// never burn): a small flame sized to the plant, licking up through its crown, a thin smoke, a few embers rising off a big
/// one, and a faint warm light. Pure numbers, so the sizes and counts by plant are tested.
/// </summary>
public readonly record struct BushFireLook(
    int Flames, float FlameWidth, float FlameSpeedMin, float FlameSpeedMax, float FlameLift, float FlameBaseHeight,
    float FlameSpreadX, float FlameSpreadY, int Smoke, float SmokeSize, float SmokeHeight, int Embers,
    float LightRange, float LightEnergy)
{
    /// <summary>The flame cards' life (s).</summary>
    public const float FlameLife = 0.5f;
    /// <summary>A flame card is this many times taller than wide (the tongue's card).</summary>
    public const float FlameTall = 1.7f;
    /// <summary>The smoke puffs' life (s) and how much they grow over it.</summary>
    public const float SmokeLife = 3.2f, SmokeGrowth = 3f;
    /// <summary>The embers' life (s).</summary>
    public const float EmberLife = 1.6f;

    /// <summary>How high above the root the flame's tips reach on average (m): where its cards start, plus their rise, plus half a card.</summary>
    public float FlameTop =>
        FlameBaseHeight + 0.5f * (FlameSpeedMin + FlameSpeedMax) * FlameLife + 0.5f * FlameLift * FlameLife * FlameLife + 0.5f * FlameWidth * FlameTall;
}

public static class BushFire
{
    /// <summary>The plant's size, 0 for a small tuft to 1 for the biggest shrub (crown reach times height against a big mesquite's).</summary>
    public static float Size(float radius, float height) => Math.Clamp(radius * height / (1.4f * 2.0f), 0f, 1f);

    /// <summary>The look of a plant of that crown reach and height (m) burning.</summary>
    public static BushFireLook For(float radius, float height)
    {
        float r = Math.Clamp(radius, 0.2f, 1.5f), h = Math.Clamp(height, 0.25f, 2.5f);
        float u = Size(r, h);
        // the rise scales with the plant's height, so a tuft's flame stands about its own height and a big shrub's a little over it
        float k = Math.Clamp(h / 1.2f, 0.45f, 1.6f);
        return new BushFireLook(
            Flames: (int)MathF.Round(8 + 12 * u),
            FlameWidth: Math.Clamp(0.12f + 0.24f * r, 0.16f, 0.46f),
            FlameSpeedMin: 0.45f * k, FlameSpeedMax: 0.9f * k, FlameLift: 1.2f * k,
            FlameBaseHeight: 0.3f * h, FlameSpreadX: 0.4f * r, FlameSpreadY: 0.2f * h,
            Smoke: (int)MathF.Round(6 + 8 * u),
            SmokeSize: 0.45f + 0.5f * r, SmokeHeight: 0.6f * h,
            Embers: u < 0.15f ? 0 : (int)MathF.Round(1 + 4 * u),
            LightRange: 2.5f + 2f * k, LightEnergy: 0.5f + 0.6f * u);
    }
}
