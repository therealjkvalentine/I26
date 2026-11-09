namespace I76.Formats.Audio;

/// <summary>
/// The port's tone controls for two buses ([port] game_eq, movie_eq): a low shelf and a high shelf, "hz:db, hz:db", the
/// first point the low shelf's corner and its gain well under it, the second the high shelf's corner and its gain well over
/// it (each within ±12 dB; "off" leaves the bus flat). The game's sounds are 8-bit, 11025 Hz (nothing above 5.5 kHz) and the
/// movies' Smacker audio 22050 Hz, its 6.3 to 10 kHz bands 8 to 13 dB under 1 kHz: the movies sound brighter. The defaults
/// trim the movies' top and lean the game's effects a dB toward the movies' balance.
/// </summary>
public static class SoundEq
{
    /// <summary>A shelf: its corner and its gain (dB) far from the corner, on the far side; half that at the corner.</summary>
    public readonly record struct Shelf(float Hz, float Db)
    {
        /// <summary>Godot's shelf gain for <see cref="Db"/>: its shelves give the square of their gain far from the corner and the gain itself at it (measured).</summary>
        public float GodotGain => MathF.Pow(10f, Db / 40f);
    }

    public const float MaxDb = 12f;

    /// <summary>
    /// The game's effects (the SFX bus): over the install's 120 engine, weapon and effect sounds the 2 to 4 kHz bands sit
    /// 2.2 dB under 125 to 500 Hz, the movies' 5.7: a dB up under 150 Hz and a dB down over 2.5 kHz closes about half of it.
    /// </summary>
    public const string GameDefault = "150:1, 2500:-1";
    /// <summary>The movies: their top octave, which the game's sounds cannot have, 6 dB down well over 6 kHz (3 at it).</summary>
    public const string MovieDefault = "250:0, 6000:-6";

    /// <summary>The two shelves for a key's text: absent, empty or unreadable the default's, "off" none (a flat bus).</summary>
    public static (Shelf Low, Shelf High)? Parse(string? text, string defaults)
    {
        if (text?.Trim().ToLowerInvariant() is "off" or "none" or "flat") return null;
        return Points(text) ?? Points(defaults);
    }

    private static (Shelf, Shelf)? Points(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var num = System.Globalization.NumberStyles.Float;
        var pts = new List<Shelf>();
        foreach (var part in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split(':', StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && float.TryParse(kv[0], num, inv, out float hz) && float.TryParse(kv[1], num, inv, out float db)
                && float.IsFinite(hz) && float.IsFinite(db))
                pts.Add(new Shelf(Math.Clamp(hz, 20f, 20000f), Math.Clamp(db, -MaxDb, MaxDb)));
        }
        if (pts.Count != 2) return null;
        return pts[0].Hz <= pts[1].Hz ? (pts[0], pts[1]) : (pts[1], pts[0]);
    }
}
