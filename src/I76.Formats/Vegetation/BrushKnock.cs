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
    /// shares with the signs, so it is pitched <see cref="DefaultSemitones"/> up, <see cref="DefaultDb"/> quieter and started
    /// <see cref="DefaultStartPercent"/> % in, past its first hit, with a <see cref="DefaultFadeInMs"/> ms fade-in, to read as a
    /// brush's crackle and not a sign's knock (the owner's reviews, sections ES, FL, GD's variants and then "a little more loud and
    /// abrupt ... start only 15% into the sound": <see cref="Variants"/> H). shell.cfg [port] bush_knock picks another of
    /// <see cref="Variants"/>; I76_BUSH_KNOCK=name,semitones,dB,start,length,fade tries any sound on top of it.
    /// </summary>
    public const string DefaultSample = "vnvco3.wav";
    public const float DefaultSemitones = 12f, DefaultDb = -9f, DefaultStartPercent = 15f, DefaultLengthMs = 0f, DefaultFadeInMs = 2f;

    /// <summary>The sound in use (<see cref="DefaultSample"/>).</summary>
    public static string Sample { get; set; } = DefaultSample;
    /// <summary>The pitch shift in semitones, on top of each plant's own pitch (<see cref="DefaultSemitones"/>).</summary>
    public static float Semitones { get; set; } = DefaultSemitones;
    /// <summary>The level offset in dB on the speed curve (<see cref="DefaultDb"/>).</summary>
    public static float OffsetDb { get; set; } = DefaultDb;
    /// <summary>Where in the sound it starts, in percent of its length (0 to <see cref="MaxStartPercent"/>; <see cref="DefaultStartPercent"/>): past the attack, so the knock lands with less impact.</summary>
    public static float StartPercent { get; set; } = DefaultStartPercent;
    /// <summary>How much of the sound plays from the start, in ms of the sound itself (before the pitch); 0 plays it to its end (<see cref="DefaultLengthMs"/>).</summary>
    public static float LengthMs { get; set; } = DefaultLengthMs;
    /// <summary>No knock at all: shell.cfg [port] bush_knock=off (the original has no plants a car goes through, so no sound for one).</summary>
    public static bool Silent { get; set; }

    /// <summary>A sound started past its beginning fades in over this long (<see cref="DefaultFadeInMs"/>), from its nearest zero crossing (<see cref="SnapMs"/>).</summary>
    public static float FadeInMs { get; set; } = DefaultFadeInMs;

    /// <summary>
    /// A sound cut short fades out over <see cref="FadeOutMs"/>; a late start moves to the nearest zero crossing within
    /// <see cref="SnapMs"/> of it (<see cref="NearestZero"/>), so the fade-in starts from silence rather than the middle of a swing.
    /// </summary>
    public const float FadeOutMs = 20f, MaxStartPercent = 90f, SnapMs = 4f;

    /// <summary>One of the knocks offered by [port] bush_knock (the reviews' letters): the sound, its shift, level, start, length and fade-in.</summary>
    public sealed record Variant(char Letter, string Sample, float Semitones, float Db, float StartPercent, float LengthMs, float FadeInMs = 8f);

    /// <summary>
    /// The knocks the owner can pick from, as in the reviews (i26-scratch/r24-bush/out/bush-variants.mp4 and -2.mp4): A the knock
    /// before them (+9 st, -8 dB), B three semitones higher and 3 dB quieter, C as B past the attack (30 %, 8 ms fade-in), D as C at
    /// A's pitch, three of the original's other sounds with a brushing, rustling noise rather than a knock (E the shell's page turn,
    /// its swish; F the shell's pencil scratch; G a 300 ms slice of the map's paper rustle; at C's loudness), and H, the default: as
    /// C but from 15 % with a 2 ms fade-in and 2 dB louder, "a little more loud and abrupt".
    /// </summary>
    public static readonly Variant[] Variants =
    [
        new('a', "vnvco3.wav", 9, -8, 0, 0),
        new('b', "vnvco3.wav", 12, -11, 0, 0),
        new('c', "vnvco3.wav", 12, -11, 30, 0),
        new('d', "vnvco3.wav", 9, -11, 30, 0),
        new('e', "gpage.wav", 0, -13, 55, 0),
        new('f', "gpencil.wav", 0, -10, 0, 0),
        new('g', "cmap2.wav", 0, -6, 21, 300),
        new('h', DefaultSample, DefaultSemitones, DefaultDb, DefaultStartPercent, DefaultLengthMs, DefaultFadeInMs),
    ];

    /// <summary>The variant used when shell.cfg names none (<see cref="Variants"/> H).</summary>
    public const char DefaultVariant = 'h';

    /// <summary>The variant a [port] bush_knock value names ("a" to "h", any case), or null for "off", an empty or unknown value.</summary>
    public static Variant? VariantOf(string? text) =>
        text?.Trim() is { Length: 1 } t ? Array.Find(Variants, v => v.Letter == char.ToLowerInvariant(t[0])) : null;

    /// <summary>Puts <paramref name="v"/> in use; null silences the knock.</summary>
    public static void Use(Variant? v)
    {
        Silent = v is null;
        if (v is null) return;
        (Sample, Semitones, OffsetDb, StartPercent, LengthMs, FadeInMs) = (v.Sample, v.Semitones, v.Db, v.StartPercent, v.LengthMs, v.FadeInMs);
    }

    /// <summary>The pitch scale for a shift of <paramref name="semitones"/>.</summary>
    public static float Ratio(float semitones) => MathF.Pow(2f, semitones / 12f);

    /// <summary>
    /// Reads I76_BUSH_KNOCK's value, "name[,semitones[,dB[,start %[,length ms[,fade-in ms]]]]]" (a name with no ".wav" gets one; a
    /// value left out keeps the one in use); false for an empty or unreadable value, leaving the settings as they were.
    /// </summary>
    public static bool Apply(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var f = System.Globalization.NumberStyles.Float;
        float semi = Semitones, db = OffsetDb, start = StartPercent, length = LengthMs, fade = FadeInMs;
        if (parts.Length > 1 && !float.TryParse(parts[1], f, inv, out semi)) return false;
        if (parts.Length > 2 && !float.TryParse(parts[2], f, inv, out db)) return false;
        if (parts.Length > 3 && !float.TryParse(parts[3], f, inv, out start)) return false;
        if (parts.Length > 4 && !float.TryParse(parts[4], f, inv, out length)) return false;
        if (parts.Length > 5 && !float.TryParse(parts[5], f, inv, out fade)) return false;
        var name = parts[0].Trim();
        if (name.Length == 0) return false;
        Sample = name.Contains('.') ? name : name + ".wav";
        Semitones = Math.Clamp(semi, -24f, 24f);
        OffsetDb = Math.Clamp(db, -40f, 12f);
        StartPercent = Math.Clamp(start, 0f, MaxStartPercent);
        LengthMs = Math.Max(length, 0f);
        FadeInMs = Math.Clamp(fade, 0f, 100f);
        Silent = false;
        return true;
    }

    /// <summary>Whether the sound in use is played from other than its beginning or cut short.</summary>
    public static bool Trims => Span(1000, 1000, StartPercent, LengthMs) != (0, 1000);

    /// <summary>
    /// The frames of a sound of <paramref name="frames"/> frames at <paramref name="rate"/> Hz that play: from <paramref name="startPercent"/>
    /// % of it (0 to <see cref="MaxStartPercent"/>, rounded to a frame) for <paramref name="lengthMs"/> ms of it (0: to its end), at
    /// least one frame. End is exclusive.
    /// </summary>
    public static (int Start, int End) Span(int frames, int rate, float startPercent, float lengthMs)
    {
        if (frames <= 0) return (0, 0);
        int start = Math.Clamp((int)MathF.Round(frames * Math.Clamp(startPercent, 0f, MaxStartPercent) / 100f), 0, frames - 1);
        int end = lengthMs > 0 ? Math.Clamp(start + (int)MathF.Round(lengthMs * rate / 1000f), start + 1, frames) : frames;
        return (start, end);
    }

    /// <summary>
    /// The gain of frame <paramref name="i"/> (0 = the span's first) of a span <see cref="Span"/> gave for a sound of
    /// <paramref name="frames"/> frames at <paramref name="rate"/> Hz: rising linearly from 0 over <paramref name="fadeInMs"/>
    /// (<see cref="FadeInMs"/> unless given; at least a frame, none for 0) where the span starts late, falling to 0 over
    /// <see cref="FadeOutMs"/> where it ends early, 1 otherwise (a whole sound is untouched).
    /// </summary>
    public static float Gain(int i, (int Start, int End) span, int frames, int rate, float? fadeInMs = null)
    {
        int n = span.End - span.Start;
        float g = 1f, fade = fadeInMs ?? FadeInMs;
        if (span.Start > 0 && fade > 0)
        {
            int fin = Math.Min(n, Math.Max(1, (int)MathF.Round(fade * rate / 1000f)));
            if (i < fin) g = Math.Min(g, i / (float)fin);
        }
        if (span.End < frames)
        {
            int fout = Math.Min(n, Math.Max(1, (int)MathF.Round(FadeOutMs * rate / 1000f)));
            int left = n - i;
            if (left <= fout) g = Math.Min(g, (left - 1) / (float)fout);
        }
        return g;
    }

    /// <summary>
    /// The part of <paramref name="pcm"/> that plays (<see cref="Span"/> of <see cref="StartPercent"/> and <see cref="LengthMs"/>
    /// unless given, a late start moved to <see cref="NearestZero"/>), faded by <see cref="Gain"/>: 8-bit (unsigned, centred on 128)
    /// or 16-bit, any channels; another format, or a sound played whole, comes back as it is.
    /// </summary>
    public static I76.Formats.Audio.Pcm Trim(I76.Formats.Audio.Pcm pcm, float? startPercent = null, float? lengthMs = null, float? fadeInMs = null)
    {
        int bytes = pcm.Bits / 8, frame = bytes * pcm.Channels;
        if ((pcm.Bits != 8 && pcm.Bits != 16) || frame <= 0) return pcm;
        int frames = pcm.Data.Length / frame;
        var span = Span(frames, pcm.Rate, startPercent ?? StartPercent, lengthMs ?? LengthMs);
        if (span.Start > 0 && NearestZero(pcm, span.Start) is var z && z != span.Start)
            span = (z, span.End < frames ? Math.Clamp(z + span.End - span.Start, z + 1, frames) : frames);
        if (span == (0, frames)) return pcm;
        var data = new byte[(span.End - span.Start) * frame];
        for (int i = 0; i < span.End - span.Start; i++)
        {
            float g = Gain(i, span, frames, pcm.Rate, fadeInMs);
            for (int c = 0; c < pcm.Channels; c++)
            {
                int src = (span.Start + i) * frame + c * bytes, dst = i * frame + c * bytes;
                if (bytes == 1) data[dst] = (byte)Math.Clamp((int)MathF.Round(128 + (pcm.Data[src] - 128) * g), 0, 255);
                else
                {
                    short v = (short)(pcm.Data[src] | pcm.Data[src + 1] << 8);
                    short o = (short)Math.Clamp((int)MathF.Round(v * g), short.MinValue, short.MaxValue);
                    data[dst] = (byte)o; data[dst + 1] = (byte)(o >> 8);
                }
            }
        }
        return pcm with { Data = data };
    }

    /// <summary>Frame <paramref name="i"/> of <paramref name="pcm"/> (8- or 16-bit) as -1..1, its channels averaged.</summary>
    public static float Value(I76.Formats.Audio.Pcm pcm, int i)
    {
        int bytes = pcm.Bits / 8, frame = bytes * pcm.Channels;
        float sum = 0;
        for (int c = 0; c < pcm.Channels; c++)
        {
            int p = i * frame + c * bytes;
            sum += bytes == 1 ? (pcm.Data[p] - 128) / 128f : (short)(pcm.Data[p] | pcm.Data[p + 1] << 8) / 32768f;
        }
        return sum / pcm.Channels;
    }

    /// <summary>
    /// The zero crossing nearest frame <paramref name="start"/> within <see cref="SnapMs"/> of it (of the two frames either side of
    /// a change of sign, or a frame at 0, the one nearer 0; the earlier of two as near), or <paramref name="start"/> itself when the
    /// sound does not cross there (a slow swing: the fade-in then starts from the middle of it).
    /// </summary>
    public static int NearestZero(I76.Formats.Audio.Pcm pcm, int start)
    {
        int frames = pcm.Data.Length / Math.Max(1, pcm.Bits / 8 * pcm.Channels);
        int w = (int)MathF.Round(SnapMs * pcm.Rate / 1000f), best = -1;
        for (int i = Math.Max(1, start - w); i <= Math.Min(frames - 1, start + w); i++)
        {
            float a = Value(pcm, i - 1), b = Value(pcm, i);
            if (Math.Sign(a) == Math.Sign(b) && b != 0) continue;
            int j = MathF.Abs(b) <= MathF.Abs(a) ? i : i - 1;
            if (best < 0 || Math.Abs(j - start) < Math.Abs(best - start)) best = j;
        }
        return best < 0 ? start : best;
    }

    /// <summary>For a sound that cannot be trimmed (a pack's compressed one): where to start it, seconds into its <paramref name="seconds"/>.</summary>
    public static float StartSeconds(double seconds) => (float)(seconds * Math.Clamp(StartPercent, 0f, MaxStartPercent) / 100.0);

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
