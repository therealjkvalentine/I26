namespace I76.Sim.Views;

/// <summary>
/// The port's Hood Finish (Graphic Detail): how much the modern look's car paint and cockpit hood shine. The original's hood is a
/// flat texture with no reflection; the modern look's clear-coated paint mirrors the sky across it. Satin, the default, thins the
/// coat; Gloss is the full coat (as before the setting); Matte takes it off.
/// </summary>
public static class PaintFinish
{
    public const int Matte = 0, Satin = 1, Gloss = 2;
    /// <summary>The row's values: Matte is the original's look (its hood and paint reflect nothing); the shine is the port's, marked as the pads mark it.</summary>
    public static readonly string[] Names = ["Matte", "Satin*", "Gloss*"];
    private static readonly string[] Words = ["matte", "satin", "gloss"];
    public const int Default = Satin;

    /// <summary>The cfg word ([port] hood_finish); absent or unknown, the old hood_matte key's Matte, else the default.</summary>
    public static int Parse(string? word, bool legacyMatte = false) => (word ?? "").Trim().ToLowerInvariant() switch
    {
        "matte" => Matte,
        "satin" => Satin,
        "gloss" or "glossy" => Gloss,
        _ => legacyMatte ? Matte : Default,
    };

    public static string Word(int finish) => Words[System.Math.Clamp(finish, 0, Words.Length - 1)];
}
