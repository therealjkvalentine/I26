namespace I76.Formats.Vegetation;

/// <summary>
/// A car knocking a plant down (docs/modern-vegetation.md, Brush impacts: Cars): how long the plant takes to go down, the
/// curve it follows, how far a slow nudge bends it, and the sound's pitch and level. Pure: the game's BrushImpacts asks it.
/// </summary>
public static class BrushKnock
{
    /// <summary>The shape of the fall.</summary>
    public enum Shape { Smooth, Snap, Topple }

    /// <summary>The curve in use (<see cref="Shape.Snap"/>: fast from the hit, settling as it lands).</summary>
    public static Shape Fall { get; set; } = Shape.Snap;

    /// <summary>Seconds = <see cref="Base"/> + <see cref="PerSpeed"/> / speed (m/s), between <see cref="Fastest"/> and <see cref="Slowest"/>.</summary>
    public static float Base { get; set; } = 0.05f;
    public static float PerSpeed { get; set; } = 1.2f;
    public const float Fastest = 0.06f, Slowest = 0.7f;

    /// <summary>
    /// How long a plant struck by a car at <paramref name="speed"/> m/s takes to go down: 0.14 s at 30 mph, 0.10 at 60, 0.08
    /// at 100, 0.32 at 10; a creeping car's push takes most of a second.
    /// </summary>
    public static float Seconds(float speed) => Math.Clamp(Base + PerSpeed / MathF.Max(speed, 0.1f), Fastest, Slowest);

    /// <summary>How far down (the bend drawn, 0..1; <see cref="LayFor"/> gives the field its value) a car at <paramref name="speed"/> lays a plant: a nudge at walking pace bends it about halfway, 4 m/s and more flattens it.</summary>
    public static float Depth(float speed) => Math.Clamp(speed / 4f, 0.55f, 1f);

    /// <summary>The share of the fall made at <paramref name="u"/> (0..1 of its time), in the plant's bend as drawn.</summary>
    public static float Curve(float u, Shape shape)
    {
        u = Math.Clamp(u, 0f, 1f);
        return shape switch
        {
            Shape.Snap => 1f - (1f - u) * (1f - u) * (1f - u),
            Shape.Topple => u * u,
            _ => u * u * (3f - 2f * u),
        };
    }

    public static float Curve(float u) => Curve(u, Fall);

    /// <summary>The bend the vegetation shader draws for a laid value (its smoothstep(0, 0.6, lay)).</summary>
    public static float Drawn(float lay)
    {
        float t = Math.Clamp(lay / 0.6f, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>The laid value the shader draws as bend <paramref name="drawn"/> (the inverse of <see cref="Drawn"/> below 0.6).</summary>
    public static float LayFor(float drawn)
    {
        drawn = Math.Clamp(drawn, 0f, 1f);
        return 0.6f * (0.5f - MathF.Sin(MathF.Asin(1f - 2f * drawn) / 3f));
    }

    /// <summary>
    /// The install's sound a car going through a bush plays: the original's knock for a car running a sign down (vnvco3), which the bush
    /// shares with the signs, so it is pitched <see cref="DefaultSemitones"/> up and <see cref="DefaultDb"/> quieter to read as a
    /// brush's crackle and not a sign's knock (the owner's review, section ES). I76_BUSH_KNOCK=name,semitones,dB tries others.
    /// </summary>
    public const string DefaultSample = "vnvco3.wav";
    public const float DefaultSemitones = 7f, DefaultDb = -6f;

    /// <summary>The sound in use (<see cref="DefaultSample"/>).</summary>
    public static string Sample { get; set; } = DefaultSample;
    /// <summary>The pitch shift in semitones, on top of each plant's own pitch (<see cref="DefaultSemitones"/>).</summary>
    public static float Semitones { get; set; } = DefaultSemitones;
    /// <summary>The level offset in dB on the speed curve (<see cref="DefaultDb"/>).</summary>
    public static float OffsetDb { get; set; } = DefaultDb;

    /// <summary>The pitch scale for a shift of <paramref name="semitones"/>.</summary>
    public static float Ratio(float semitones) => MathF.Pow(2f, semitones / 12f);

    /// <summary>
    /// Reads I76_BUSH_KNOCK's value, "name[,semitones[,dB]]" (a name with no ".wav" gets one); false for an empty or unreadable value,
    /// leaving the settings as they were.
    /// </summary>
    public static bool Apply(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        float semi = Semitones, db = OffsetDb;
        if (parts.Length > 1 && !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out semi)) return false;
        if (parts.Length > 2 && !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out db)) return false;
        var name = parts[0].Trim();
        if (name.Length == 0) return false;
        Sample = name.Contains('.') ? name : name + ".wav";
        Semitones = Math.Clamp(semi, -24f, 24f);
        OffsetDb = Math.Clamp(db, -40f, 12f);
        return true;
    }

    /// <summary>
    /// The knock's sound for a plant at (<paramref name="x"/>, <paramref name="z"/>) of <paramref name="height"/> metres struck at
    /// <paramref name="speed"/> m/s: a pitch of its own (0.85-1.15, lower for a big plant, times <see cref="Ratio"/> of <see cref="Semitones"/>) and a level, louder the faster the car
    /// (-14 dB creeping to 0 dB at 100 mph, plus <see cref="OffsetDb"/>), each plant a little apart (±1.5 dB) so a row of them does not sound as one sample.
    /// </summary>
    public static (float Pitch, float Db) Voice(float speed, float height, float x, float z)
    {
        uint h = Hash(x, z);
        float r1 = (h & 0xffff) / 65535f, r2 = (h >> 16) / 65535f;
        float size = Math.Clamp(1.12f - 0.08f * height, 0.85f, 1.1f);
        float pitch = Math.Clamp(size * (0.88f + 0.24f * r1), 0.75f, 1.25f) * Ratio(Semitones);
        float db = -14f + 14f * Math.Clamp(speed / 44.7f, 0f, 1f) + (r2 - 0.5f) * 3f + OffsetDb;
        return (pitch, db);
    }

    private static uint Hash(float x, float z)
    {
        uint h = (uint)(int)MathF.Floor(x * 8f) * 0x9E3779B1u ^ (uint)(int)MathF.Floor(z * 8f) * 0x85EBCA77u;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return h;
    }
}

/// <summary>
/// The knock sounds' rate limit: a car makes at most one every <see cref="Gap"/> seconds, and all the cars together at most
/// <see cref="PerSecond"/> a second, so a car ploughing a hedge of brush sounds as a run of crunches, not a machine gun.
/// </summary>
public sealed class KnockSounds
{
    public const float Gap = 0.08f;
    public const int PerSecond = 12;
    private readonly Dictionary<ulong, double> _last = [];
    private readonly Queue<double> _recent = new();

    /// <summary>Whether car <paramref name="car"/> may sound a knock at time <paramref name="now"/> (seconds); a yes is counted.</summary>
    public bool Try(ulong car, double now)
    {
        if (_last.TryGetValue(car, out double t) && now - t < Gap) return false;
        while (_recent.Count > 0 && now - _recent.Peek() >= 1.0) _recent.Dequeue();
        if (_recent.Count >= PerSecond) return false;
        _last[car] = now;
        _recent.Enqueue(now);
        if (_last.Count > 64) _last.Clear();
        return true;
    }

    public void Clear() { _last.Clear(); _recent.Clear(); }
}
