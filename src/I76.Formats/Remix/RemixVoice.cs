using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using I76.Formats.Audio;
using I76.Formats.Vfs;

namespace I76.Formats.Remix;

/// <summary>One spoken line of the table (src/I76.Formats/Remix/Voice/lines.json): our text, where it may be said, its recipe.</summary>
public sealed class VoiceLine
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>taurus, jade or groove (the player).</summary>
    [JsonPropertyName("voice")] public string Voice { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    /// <summary>good (the owner heard it and rated it good), unheard (rebuilt or reworded since, from heard pieces), or okay.</summary>
    [JsonPropertyName("rating")] public string Rating { get; set; } = "";
    /// <summary>Its voice's pieces are Nitro Pack clips.</summary>
    [JsonPropertyName("nitro")] public bool Nitro { get; set; }
    /// <summary>The radio moments it may be said at (RemixVoice.Moments).</summary>
    [JsonPropertyName("moments")] public List<string> Moments { get; set; } = [];
    /// <summary>What must be true of the job at that moment for it to be said (every one of them).</summary>
    [JsonPropertyName("needs")] public List<string> Needs { get; set; } = [];
    /// <summary>Why it is never said (it would say something no generated job makes true), or null.</summary>
    [JsonPropertyName("unused")] public string? Unused { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("gain")] public double Gain { get; set; } = 1;
    [JsonPropertyName("seed")] public long Seed { get; set; }
    /// <summary>Silent from this sample on (a line as it was first heard, trimmed there), or null.</summary>
    [JsonPropertyName("silence_from")] public int? SilenceFrom { get; set; }
    [JsonPropertyName("pieces")] public List<SplicePiece> Pieces { get; set; } = [];

    /// <summary>The sound name a job's script plays it by (its CB name: under the radio's 15 characters).</summary>
    [JsonIgnore] public string Sound => RemixVoice.SoundOf(Id);
    /// <summary>The speaker as the stock game labels a CB line: the player for Groove's own lines, else the radio.</summary>
    [JsonIgnore] public bool PlayerSpeaks => Voice == "groove";
    [JsonIgnore] public string Speaker => Voice.Length == 0 ? "" : char.ToUpperInvariant(Voice[0]) + Voice[1..];
}

/// <summary>
/// Spoken radio lines for generated jobs (docs/remix-missions.md "Spoken lines"): new instructions recombined from the game's own
/// recorded dialogue, rebuilt at run time from the player's install by numbers-only recipes (cuts of clips by stem, offsets, gains,
/// a checksum of each source clip). The repository holds our text and the numbers, never audio or the original lines' words.
/// <see cref="Plan"/> (the director's, outside the simulation) picks a line for each radio moment of a job whose facts make it
/// true; the game renders and plays it (SpliceRender), lettering its text whether or not the audio could be built. The
/// simulation sees only the line's length, from the table's numbers (<see cref="Seconds"/>), never from the rendered audio.
/// </summary>
public static class RemixVoice
{
    /// <summary>The radio moments of a job a line may be said at.</summary>
    public static readonly string[] Moments =
    [
        "go",          // the first instruction: where to go
        "hold",        // wait where you are (an ambush's start)
        "police",      // the law is coming (a tail's start)
        "contact",     // the enemy is there (first seen, alerted)
        "arrive",      // the player reports he has got there (Groove)
        "fight",       // the fight step opens
        "kill",        // one of them down (a convoy's lead)
        "count",       // N down, M to go
        "reinforce",   // more of them arrive
        "lost",        // a tail lost: on to the hideout
        "next",        // the next place after a step (a safe house, an escort's goal)
        "targetsdown", // every target down
        "return",      // go back, or away
        "getaway",     // a convoy close to its way out
        "wiped",       // one more structure lost fails the defence
        "hurry",       // the clock is nearly out
        "done",        // the job won
    ];

    public sealed class Table
    {
        [JsonPropertyName("rate")] public int Rate { get; set; } = SpliceRender.Rate;
        [JsonPropertyName("lead")] public int Lead { get; set; }
        [JsonPropertyName("radio")] public SpliceRadio Radio { get; set; } = new();
        /// <summary>The CRC-32 of each source clip's 8-bit PCM, "game:stem": another release or language differs, and its lines are skipped.</summary>
        [JsonPropertyName("clips")] public Dictionary<string, uint> Clips { get; set; } = [];
        [JsonPropertyName("lines")] public List<VoiceLine> Lines { get; set; } = [];
    }

    private static Table? _table;
    private static Dictionary<string, VoiceLine>? _bySound;
    private static Dictionary<string, Dictionary<string, string[]>>? _places;

    public static Table Data => _table ??= Load();
    public static IReadOnlyList<VoiceLine> Lines => Data.Lines;

    private static Table Load()
    {
        using var s = typeof(RemixVoice).Assembly.GetManifestResourceStream("remix-voice/lines.json") ?? throw new RemixError("the voice table is not embedded");
        return JsonSerializer.Deserialize<Table>(s) ?? throw new RemixError("an empty voice table");
    }

    /// <summary>The script's sound name for a line id: rxv, the voice's letter and the number ("taurus-108": rxvt108).</summary>
    public static string SoundOf(string id)
    {
        int dash = id.IndexOf('-');
        return dash <= 0 ? "rxv" + id : "rxv" + id[0] + id[(dash + 1)..];
    }

    /// <summary>The line a sound name plays ("rxvt108" or "rxvt108.wav", any case), or null for any other sound.</summary>
    public static VoiceLine? Of(string? name)
    {
        if (name is null || name.Length < 4 || !name.StartsWith("rxv", StringComparison.OrdinalIgnoreCase)) return null;
        _bySound ??= Lines.ToDictionary(l => l.Sound, StringComparer.OrdinalIgnoreCase);
        int dot = name.LastIndexOf('.');
        return _bySound.TryGetValue(dot < 0 ? name : name[..dot], out var l) ? l : null;
    }

    public static VoiceLine? ById(string id) => Lines.FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// How long the radio is busy with a line, seconds: its recipe's length with both squelch bursts and their pads, from the
    /// table's numbers alone (the same on every machine, whether or not the audio could be built here).
    /// </summary>
    public static float Seconds(VoiceLine l)
    {
        var r = Data.Radio;
        int n = l.Total;
        if (SpliceRadio.Cut(r.Head) is { } h) n += h.End - h.Start + r.Pad;
        if (SpliceRadio.Cut(r.Tail) is { } t) n += t.End - t.Start + r.Pad;
        return n / (float)SpliceRender.Rate;
    }

    // ------------------------------------------------------------------ the player's clips

    /// <summary>
    /// The source clips on this machine: the base game's (the running install) and the Nitro Pack's (an install beside it, or
    /// none). A clip whose bytes differ from the table's checksum is treated as missing.
    /// </summary>
    public sealed class Source(AssetVfs? i76, AssetVfs? nitro)
    {
        private readonly Dictionary<string, float[]?> _cache = [];
        private readonly Dictionary<string, string> _why = [];

        public bool HasNitro => nitro is not null;

        /// <summary>A clip's samples, or null (no such install, no such clip, or its checksum differs).</summary>
        public float[]? Clip(string game, string clip)
        {
            var key = game + ":" + clip;
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var x)) return x;
                _cache[key] = x = Read(game, clip, key);
                return x;
            }
        }

        /// <summary>Why a clip is missing ("no Nitro Pack", "checksum differs: another release?"), or null.</summary>
        public string? Why(string game, string clip) { Clip(game, clip); lock (_cache) return _why.GetValueOrDefault(game + ":" + clip); }

        private float[]? Read(string game, string clip, string key)
        {
            var vfs = game == "nitro" ? nitro : game == "i76" ? i76 : null;
            if (vfs is null) { _why[key] = game == "nitro" ? "no Nitro Pack" : "no install"; return null; }
            Pcm? p;
            try { p = SoundFiles.Read(vfs, clip + ".wav"); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { p = null; }
            if (p is not { Channels: 1, Bits: 8 }) { _why[key] = "no such clip"; return null; }
            if (Data.Clips.TryGetValue(key, out uint want) && Installers.Crc32.Compute(p.Data) != want) { _why[key] = "checksum differs (another release?)"; return null; }
            var x = new float[p.Data.Length];
            for (int i = 0; i < x.Length; i++) x[i] = (p.Data[i] - 128) / 128f;
            return x;
        }
    }

    /// <summary>Whether this machine can build a line, and if not why not (a missing or differing clip of its voice).</summary>
    public static string? WhyNot(VoiceLine l, Source src)
    {
        foreach (var p in l.Pieces)
            if (src.Why(p.Game, p.Clip) is { } w) return $"{p.Game}:{p.Clip}: {w}";
        return null;
    }

    /// <summary>A line rendered with the radio, or null when a clip of its voice is missing here (the bursts and Nitro bed are optional).</summary>
    public static double[]? Render(VoiceLine l, Source src)
    {
        if (SpliceRender.Plain(l.Pieces, l.Total, l.Gain, src.Clip, l.SilenceFrom) is not { } plain) return null;
        return SpliceRender.Radio(plain, Data.Radio, l.Seed, src.Clip);
    }

    // ------------------------------------------------------------------ what a place is

    /// <summary>A node table's place (a start, node, cluster or route by name) in the words a line can say: "oil field", "on 380".</summary>
    public static IReadOnlyList<string> Tags(string map, string? place)
    {
        if (place is null) return [];
        _places ??= LoadPlaces();
        return _places.TryGetValue(map.ToLowerInvariant(), out var m) && m.TryGetValue(place, out var t) ? t : [];
    }

    private static Dictionary<string, Dictionary<string, string[]>> LoadPlaces()
    {
        using var s = typeof(RemixVoice).Assembly.GetManifestResourceStream("remix-voice/places.yaml") ?? throw new RemixError("the voice places are not embedded");
        var d = new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<Dictionary<string, Dictionary<string, string[]>>>(new StreamReader(s).ReadToEnd());
        return d ?? [];
    }

    // ------------------------------------------------------------------ the plan

    /// <summary>
    /// A radio moment of a job: what it is, what is true of the job then (place:TAG, plural, convoy, clear, ...; see the docs),
    /// and where the chosen line goes (the description's radio).
    /// </summary>
    public sealed record Slot(string Moment, IReadOnlyCollection<string> Facts, Action<string> Put);

    /// <summary>The lines a moment with these facts may say: the moment is one of the line's, and every need of the line is a fact.</summary>
    public static IEnumerable<VoiceLine> Fitting(string moment, IReadOnlyCollection<string> facts) =>
        Lines.Where(l => l.Unused is null && l.Moments.Contains(moment) && l.Needs.All(facts.Contains));

    /// <summary>
    /// Picks a line for each slot whose facts some line fits (the owner's "good" ones first, an "okay" one only where no good one
    /// fits), never the same line twice in a job, and puts its sound name there; a slot nothing fits keeps what the description had
    /// (a stock clip of that intent, or nothing). Seeded from the job's own seed on a stream of its own (the rest of the roll is
    /// untouched). Returns the choices, slot by slot (null where none fitted).
    /// </summary>
    public static List<(Slot Slot, VoiceLine? Line)> Plan(IEnumerable<Slot> slots, int seed)
    {
        ulong s = unchecked((ulong)seed * 0xD1B54A32D192ED03UL + 0x5EED_70CEUL);
        int Next(int n)
        {
            ulong z = s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z % (ulong)n);
        }
        var used = new HashSet<string>();
        var o = new List<(Slot, VoiceLine?)>();
        foreach (var slot in slots)
        {
            var fit = Fitting(slot.Moment, slot.Facts).Where(l => !used.Contains(l.Id)).ToList();
            // the owner's good lines first, then those rebuilt from heard pieces but not heard whole, then okay ones
            var pool = fit.Where(l => l.Rating == "good").ToList();
            if (pool.Count == 0) pool = fit.Where(l => l.Rating == "unheard").ToList();
            if (pool.Count == 0) pool = fit;
            if (pool.Count == 0) { o.Add((slot, null)); continue; }
            var l = pool[Next(pool.Count)];
            used.Add(l.Id);
            slot.Put(l.Sound);
            o.Add((slot, l));
        }
        return o;
    }

    /// <summary>The words "{N} down" lines say, by group size: the counts the table can say.</summary>
    public static IEnumerable<int> Counts(int groupSize) => groupSize switch { 3 => [1, 2], 5 => [3], _ => [] };

    /// <summary>A compass word for the direction from one point to another (east or west within 35 degrees), or null.</summary>
    public static string? EastWest(double[] from, double[] to)
    {
        double dx = to[0] - from[0], dz = to[1] - from[1];
        if (dx * dx + dz * dz < 300 * 300) return null;
        double b = DetMath.Atan2(dx, dz) * 180 / Math.PI;   // degrees from +Z (north) toward +X (east)
        return Math.Abs(b - 90) < 35 ? "east" : Math.Abs(b + 90) < 35 ? "west" : null;
    }

    public static string N(double v) => v.ToString(CultureInfo.InvariantCulture);
}
