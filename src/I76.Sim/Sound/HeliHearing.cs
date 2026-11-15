using System.Globalization;

namespace I76.Sim.Sound;

/// <summary>
/// How far a helicopter's rotor loop carries ([port] helicopter_hearing=far, the modern look's default; docs/deviations.md).
/// The original gives it a car engine's DirectSound3D voice (<see cref="Ds3d"/>: full within 10 m, -20 dB at 100 m, held at
/// -32 dB from 400 m, parked 600 m from the player's car), so a helicopter is barely heard at the range its fights are flown
/// at and is gone past 600 m. Here three bands instead: overhead (inside <see cref="NearMetres"/>) at <see cref="NearDb"/> over
/// the original's near level; medium, falling at <see cref="Rolloff"/> times the inverse-distance law plus
/// <see cref="ExcessDbPerKm"/> (the air and the ground taking the highs and some level); far, from <see cref="FadeMetres"/>
/// faded to nothing at <see cref="RangeMetres"/>, where the voice is parked as the original parks it at 600 m. A high shelf
/// over <see cref="ShelfHz"/> takes the blade slap and the whine away with distance, so a far one is mostly its thump.
/// Presentation only: the levels are the voice's, before the Effects slider.
/// </summary>
public sealed record HeliHearing(
    float NearMetres = 50f, float NearDb = -0.5f, float Rolloff = 1f, float ExcessDbPerKm = 3f,
    float FadeMetres = 1800f, float RangeMetres = 2000f,
    float ShelfStartMetres = 150f, float ShelfFarDb = -18f, float ShelfHz = 500f)
{
    /// <summary>The helicopters' engine loop (engsnd.dat rows 20 and 41, and the missions' own helicopters by their body).</summary>
    public const string RotorLoop = "aheli.wav";

    /// <summary>
    /// Whether a vehicle's engine loop is a rotor's: a helicopter (object class 9) playing <see cref="RotorLoop"/>. The balloon
    /// (vballoon.wav, a burner) and the UFO (vufo.wav), class 9 too, keep the original's hearing, as every car, the tank and the trucks do.
    /// </summary>
    public static bool IsRotorLoop(bool helicopter, string? engineSound) =>
        helicopter && string.Equals(engineSound, RotorLoop, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The fork's bands, against the original's near level (0 dB inside 10 m): -0.5 dB to 50 m, -10 at 150 m, -20 at 400, -29 at 1000,
    /// -37 at 1800, silent at 2000: 3.5 dB (half as loud again) over the first cut's, which a playtest found too quiet. The rotor loop
    /// is 10 dB hotter than a car's engine loop (aheli -7 dBFS RMS, einp1 -17), so overhead it stands about 11 dB over the player's
    /// own engine, at 150 m about level with it, at 400 m 7 dB under, at 1.8 km 25 dB under.
    /// </summary>
    public static readonly HeliHearing Default = new();

    /// <summary>
    /// The level at <paramref name="distance"/> metres from the listener, in dB against the original's near level (its
    /// <see cref="Ds3d.AttenuationDb"/> of 0); <see cref="float.NegativeInfinity"/> at or past <see cref="RangeMetres"/>.
    /// </summary>
    public float Db(float distance)
    {
        float d = float.IsFinite(distance) ? System.Math.Max(distance, 0f) : float.PositiveInfinity;
        if (!(d < RangeMetres)) return float.NegativeInfinity;
        float near = System.Math.Max(NearMetres, 1f);
        float db = NearDb;
        if (d > near) db -= 20f * Rolloff * MathF.Log10(d / near) + ExcessDbPerKm * (d - near) / 1000f;
        // the last stretch fades the amplitude out straight to nothing at the range
        if (d > FadeMetres && RangeMetres > FadeMetres) db += 20f * MathF.Log10(System.Math.Max((RangeMetres - d) / (RangeMetres - FadeMetres), 1e-6f));
        return db;
    }

    /// <summary>The voice's gain at <paramref name="distance"/> (1 at the original's near level).</summary>
    public float Gain(float distance) => float.IsNegativeInfinity(Db(distance)) ? 0f : MathF.Pow(10f, Db(distance) / 20f);

    /// <summary>
    /// The high shelf's cut over <see cref="ShelfHz"/> at <paramref name="distance"/>, in dB (0 or less): none inside
    /// <see cref="ShelfStartMetres"/>, then straight in the log of the distance to <see cref="ShelfFarDb"/> at <see cref="FadeMetres"/>, held.
    /// </summary>
    public float ShelfDb(float distance)
    {
        if (!(distance > ShelfStartMetres) || FadeMetres <= ShelfStartMetres) return 0f;
        float u = System.Math.Clamp(MathF.Log(distance / ShelfStartMetres) / MathF.Log(FadeMetres / ShelfStartMetres), 0f, 1f);
        return ShelfFarDb * u;
    }

    /// <summary>Whether a looping voice this far from the player's car plays (the original's 600 m cull, moved to the range).</summary>
    public bool Plays(float distanceFromPlayer) => distanceFromPlayer < RangeMetres;

    /// <summary>
    /// The env hook I76_HELI_HEARING: "near_m,near_db,rolloff,excess_db_per_km,fade_m,range_m[,shelf_start_m,shelf_far_db,shelf_hz]",
    /// any field left empty keeping the default ("50,6" a louder overhead, ",,,,2800,3000" a longer range). Null when unset or unreadable.
    /// </summary>
    public static HeliHearing? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var f = text.Split(',');
        var d = Default;
        float[] v = [d.NearMetres, d.NearDb, d.Rolloff, d.ExcessDbPerKm, d.FadeMetres, d.RangeMetres, d.ShelfStartMetres, d.ShelfFarDb, d.ShelfHz];
        if (f.Length > v.Length) return null;
        for (int i = 0; i < f.Length; i++)
        {
            if (f[i].Trim().Length == 0) continue;
            if (!float.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i])) return null;
        }
        if (v[0] <= 0 || v[2] < 0 || v[3] < 0 || v[5] <= 0 || v[4] > v[5] || v[8] <= 0 || v[7] > 0) return null;
        return new HeliHearing(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8]);
    }

    /// <summary>
    /// The value for Godot's attenuation filter (AudioStreamPlayer3D.attenuation_filter_db) that gives the shelf cut <paramref name="shelfDb"/>
    /// for a voice at <paramref name="volumeDb"/> with no distance model of Godot's: Godot scales the filter's dB by one less the voice's
    /// linear volume and passes the result as the shelf's A, whose high-frequency gain is AÂ², so half the cut goes in. 0 for none.
    /// </summary>
    public static float GodotFilterDb(float shelfDb, float volumeDb)
    {
        if (shelfDb >= 0) return 0f;
        float lin = MathF.Pow(10f, System.Math.Min(volumeDb, 0f) / 20f);
        float k = 1f - System.Math.Min(1f, lin);
        return k < 0.01f ? 0f : System.Math.Max(shelfDb / 2f / k, -80f);
    }
}
