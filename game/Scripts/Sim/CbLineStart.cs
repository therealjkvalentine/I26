namespace I76.Game.Sim;

/// <summary>
/// When a CB line's sound starts (SimDriver's radio): at once, or, while a load holds the game's sound (Main.GameSoundHeld: its buses
/// at -80 dB behind the loading card), at the hold's end; then from the point the radio's clock has reached in the line, so the audio
/// and the radio (which hands over to the next line by that clock) stay together. Engine-free, for the tests.
/// </summary>
public static class CbLineStart
{
    /// <summary>A line started now waits for the hold's end: started at once it would play on silent and be heard part-way through.</summary>
    public static bool Waits(bool gameSoundHeld) => gameSoundHeld;

    /// <summary>Seconds into the line its sound starts at: the radio's clock <paramref name="nowMs"/> less the line's start (never before 0).</summary>
    public static float Offset(double nowMs, double startedMs) => (float)System.Math.Max(0, (nowMs - startedMs) / 1000.0);
}
