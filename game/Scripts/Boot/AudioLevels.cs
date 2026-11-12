namespace I76.Game.Boot;

/// <summary>The SFX and Voice sliders as decibels: the original's volume law, 400 x log2(volume x level / 1000) millibels
/// (0x42182f..0x421854: 4 dB per halving, 0 dB at level 10), so level 8 is -1.29 dB, 5 is -4 dB, 3 is -6.95 dB and 2 is
/// -9.29 dB. Level 0 and 1 are off (the bus is muted; the original treats 1 as 0).</summary>
public static class AudioLevels
{
    public static float EffectDb(int level) => level < 1 ? -80f : 4f * MathF.Log2(level / 10f);
}
