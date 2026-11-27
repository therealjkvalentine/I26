using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using I76.Formats.Fsm;
using I76.Formats.Mission;
using I76.Formats.Mission.Edit;
using I76.Formats.Vfs;

namespace I76.Formats.Remix;

/// <summary>What a build makes: the mission file, its objective text, the script's source (for reading) and the build's stamp.</summary>
public sealed record RemixOutput(string Stem, byte[] Mission, string Text, string Source, string Stamp)
{
    /// <summary>What the port's map (M) of the remix marks: <see cref="RemixMarks"/>' text.</summary>
    public string Marks { get; init; } = "";
    public string MissionFile => Stem + ".msn";
    public string TextFile => Stem + ".npt";

    /// <summary>Writes the files into <paramref name="dir"/> (never an install's folder): STEM.msn, STEM.npt, STEM.yaml, STEM.marks, STEM.remix (last: the stamp says the rest is there).</summary>
    public IReadOnlyList<string> WriteTo(string dir)
    {
        Directory.CreateDirectory(dir);
        var o = new List<string>();
        void Put(string name, byte[] b) { var p = Path.Combine(dir, name); File.WriteAllBytes(p, b); o.Add(p); }
        Put(MissionFile, Mission);
        Put(TextFile, Encoding.Latin1.GetBytes(Text));
        Put(Stem + ".yaml", Encoding.UTF8.GetBytes(Source));
        Put(Stem + ".marks", Encoding.UTF8.GetBytes(Marks));
        Put(Stem + ".remix", Encoding.UTF8.GetBytes(Stamp));
        return o;
    }
}

/// <summary>
/// The remix generator (docs/remix-missions.md): a stock mission's map with a new script. It reads the base mission from the
/// player's install, edits a copy of it with the map writers (the player's start, new parked cars, new nav paths appended to the
/// script's table, the hour, no briefing or outro movie, its own objective text), keeps the base script's library routines and
/// their threads (leaving the map, taunts, hit chatter) and replaces every mission thread with the objective template's,
/// compiled from the script source language (MissionScriptCompiler). Nothing is written into the install.
/// </summary>
public static class RemixBuilder
{
    /// <summary>The generator's version, in the stamp: a build by another version is made again.</summary>
    public const int Version = 3;
    /// <summary>The stock library's routines lead every mission's code: Interstate '76's 15, the Nitro Pack's 28 (docs/mission-scripts.md).</summary>
    public const int LibraryRoutines = 15, NitroLibraryRoutines = 28;
    public const string PathPrefix = "rx_";

    public static RemixOutput Build(GameInstall install, RemixSpec spec)
    {
        if (!MapProject.IsValidStem(spec.Id)) throw new RemixError($"id '{spec.Id}': 1-{MapProject.MaxStem} letters, digits and underscores");
        if (spec.Title.Length is 0 or > MapDocument.ArenaNameSize - 1) throw new RemixError($"title '{spec.Title}': 1-{MapDocument.ArenaNameSize - 1} characters");
        var basePath = install.FindMission(spec.Base) ?? throw new RemixError($"no mission '{spec.Base}' in the install");
        if (GameInstall.UserMissionDir is { } um && Path.GetFullPath(basePath).StartsWith(Path.GetFullPath(um), StringComparison.OrdinalIgnoreCase))
            throw new RemixError($"the base '{spec.Base}' must be a mission of the install, not a generated one");
        if (install.FindMission(spec.Id) is { } clash && !(GameInstall.UserMissionDir is { } um2 && Path.GetFullPath(clash).StartsWith(Path.GetFullPath(um2), StringComparison.OrdinalIgnoreCase)))
            throw new RemixError($"id '{spec.Id}' is a mission of the install");
        var bytes = File.ReadAllBytes(basePath);
        var stock = MissionScriptFile.Decompile(bytes, spec.Base.ToUpperInvariant()).Script;
        var doc = MapDocument.Parse(bytes);

        // ---- the map ----
        // the player's placement as the build finds it (MissionSetup: flagged, else a spawn, else Groove's car, else the first car),
        // flagged so that a scenario's car never lands on another placement of the same car
        var player = doc.Objects.FirstOrDefault(o => o.IsPlayer && o.ObjectType == 1)
            ?? doc.Objects.FirstOrDefault(o => o.ObjectType == 1 && o.Name.Equals("spawn", StringComparison.OrdinalIgnoreCase))
            ?? doc.Objects.FirstOrDefault(o => o.ObjectType == 1 && o.Name.Equals("vppirna1", StringComparison.OrdinalIgnoreCase))
            ?? doc.Objects.FirstOrDefault(o => o.ObjectType == 1)
            ?? throw new RemixError($"{spec.Base} has no player placement");
        player.ObjectFlags |= 0x10;
        var playerId = player.Payload[..8];
        if (spec.Start is { } st)
        {
            var (x, z) = XZ(st.At, "start");
            player.Position = new Vector3(x, player.Position.Y, z);
            player.Yaw = (float)(st.Heading * Math.PI / 180);
        }
        foreach (var r in spec.Remove) doc.Objects.Remove(Find(doc, r, "remove"));
        // structures set down before the start (a wreck at a pickup, a roadblock): an object of the install placed as the base game
        // places it, on the ground under it (the terrain is the base's; nothing of it changes)
        if (spec.Props.Count > 0)
        {
            var msn = new MissionFile(basePath);
            var terPath = I76.Formats.Assembly.MissionWorld.FindTerrain(install, msn) ?? throw new RemixError($"{spec.Base} has no terrain for its props");
            var ter = TerrainTiles.Load(msn, terPath);
            var vfs = new AssetVfs(install);
            for (int i = 0; i < spec.Props.Count; i++)
            {
                var pr = spec.Props[i];
                if (!MapProject.IsValidStem(pr.Object) || !vfs.Exists(pr.Object + ".sdf")) throw new RemixError($"prop {i + 1}: no object '{pr.Object}' in the install");
                if (pr.Type is not (2 or 3 or 4)) throw new RemixError($"prop {i + 1} ({pr.Object}): type {pr.Type}: 2 (building), 3 (small object) or 4 (scenery)");
                var (x, z) = XZ(pr.At, $"prop {i + 1}");
                var o = MapObject.Create(pr.Object, pr.Type, new Vector3(x, GroundAt(ter, x, z), z), (float)(pr.Heading * Math.PI / 180));
                o.NameFlags = FreeFlags(doc, pr.Object);
                doc.Objects.Add(o);
            }
        }
        var actors = new Dictionary<string, string>(StringComparer.Ordinal);   // a template's name → the actor key
        var newActors = new List<ActorDef>();
        foreach (var (name, b) in spec.Bind)
        {
            var o = Find(doc, b, $"bind {name}");
            newActors.Add(new ActorDef { Key = PathPrefix + name, Label = PathPrefix + name, Id = o.Payload[..8], Object = o.Name });
            actors[name] = PathPrefix + name;
        }
        foreach (var (name, c) in spec.Cars)
        {
            if (!MapProject.IsValidStem(c.Vcf)) throw new RemixError($"car {name}: vcf '{c.Vcf}' is not a car file's stem");
            var (x, z) = XZ(c.At, $"car {name}");
            var o = MapObject.Create(c.Vcf, 1, new Vector3(x, 0, z), (float)(c.Heading * Math.PI / 180));
            o.NameFlags = FreeFlags(doc, c.Vcf);
            doc.Objects.Add(o);
            newActors.Add(new ActorDef { Key = PathPrefix + name, Label = PathPrefix + name, Id = o.Payload[..8], Object = c.Vcf });
            actors[name] = PathPrefix + name;
        }
        var placeKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var headings = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, pts) in spec.Places)
        {
            if (pts.Count == 0) throw new RemixError($"place {name}: no points");
            var label = PathPrefix + name;
            if (label.Length > FsmPaths.MaxLabel) throw new RemixError($"place {name}: the label is longer than {FsmPaths.MaxLabel}");
            if (doc.Paths.Any(p => p.Name.Equals(label, StringComparison.OrdinalIgnoreCase))) throw new RemixError($"place {name}: the base has a path {label}");
            var v = pts.Select((p, i) => { var (x, z) = XZ(p, $"place {name} point {i}"); return new Vector3(x, 0, z); }).ToList();
            doc.Paths.Add(new PathEntry(label, v));
            placeKeys[name] = label;
            headings[label] = v.Count > 1 ? Heading(v[0], v[1]) : 0;
        }
        // stock paths are found by their key in the base's source too (behind1, fisher, ...)
        var stockPathKeys = MissionScriptDecompiler.Keys(FsmPaths.Read(doc.FsmPayload).Select(p => p.Name));
        foreach (var (key, i) in stockPathKeys.Select((k, i) => (k, i)))
            if (!placeKeys.ContainsValue(key) && !placeKeys.ContainsKey(key))
            {
                placeKeys[key] = key;
                var pts = doc.Paths[i].Points;
                headings[key] = pts.Count > 1 ? Heading(pts[0], pts[1]) : 0;
            }
        var w = doc.WorldPayload ?? throw new RemixError($"{spec.Base} has no WRLD");
        if (spec.Hour is { } h) { if (h is < 0 or > 23) throw new RemixError($"hour {h}: 0-23"); doc.Hour = h; }
        Name13(w, 0x04, "");                        // no briefing movie: it tells the stock mission's story
        Name13(w, 0x11, "");                        // nor the outro
        Name13(w, 0x45, spec.Id + ".npt");          // its own objective text
        doc.ArenaName = spec.Title;

        // ---- the script ----
        var src = Library(stock, spec, install.IsNitro);
        var libraryThreads = src.Threads.ToList();
        foreach (var a in newActors) src.Actors.Add(a);
        var stockPlayer = stock.Actors.FirstOrDefault(a => a.Id is { } id && id.AsSpan().SequenceEqual(playerId))?.Key
            ?? throw new RemixError($"{spec.Base}'s script names no actor for the player's placement");
        var used = new HashSet<string>(StringComparer.Ordinal) { stockPlayer };
        var ctx = new RemixTemplates.Context(spec, stockPlayer, n => { var k = Actor(n, actors, stock); used.Add(k); return k; }, n => Place(n, placeKeys), n => headings[Place(n, placeKeys)]);
        var fragment = RemixTemplates.Emit(ctx);
        foreach (var piece in fragment.Split("---\n", StringSplitOptions.RemoveEmptyEntries))
        {
            MissionScriptSource part;
            try { part = MissionScriptYaml.Read(piece); }
            catch (ScriptError ex) { throw new RemixError($"the template's source: {ex.Message}\n{piece}"); }
            Merge(src, part);
        }
        // the sides: every car the template turns on the player is the enemy's (team 2), as every stock mission's enemies are, so the
        // target keys (E, T) cycle to it and the radar shows it; a new car is nobody's (team 0) unless told, which the keys skip
        var hostile = RemixRoles.Hostiles(spec).Select(r => r.Car).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, c) in spec.Cars)
            if (ObjectOf(name) is { } o) SetTeam(o, c.Team ?? (hostile.Contains(name) ? RemixRoles.EnemyTeam : 0));
        foreach (var name in hostile.Where(n => !spec.Cars.ContainsKey(n)))
            if (ObjectOf(name) is { } o && o.ObjectType == 1) SetTeam(o, RemixRoles.EnemyTeam);
        MapObject? ObjectOf(string name)
        {
            byte[]? id = actors.TryGetValue(name, out var key) ? newActors.First(a => a.Key == key).Id
                : stock.Actors.FirstOrDefault(a => a.Key == name)?.Id;
            return id is null ? null : doc.Objects.FirstOrDefault(o => o.Payload.AsSpan(0, 8).SequenceEqual(id));
        }
        // the stock cars nothing drives any more: the base's mission threads (dropped) did; they would only stand where it parked them
        switch (spec.IdleCars)
        {
            case "keep": break;
            case "remove":
                var keep = new HashSet<string>(newActors.Select(a => Convert.ToHexString(a.Id!)), StringComparer.Ordinal);
                foreach (var a in stock.Actors.Where(a => a.Id is not null && used.Contains(a.Key))) keep.Add(Convert.ToHexString(a.Id!));
                var gone = doc.Objects.Where(o => o.ObjectType == 1 && o != player && !keep.Contains(Convert.ToHexString(o.Payload, 0, 8)))
                    .Select(o => Convert.ToHexString(o.Payload, 0, 8)).ToHashSet(StringComparer.Ordinal);
                doc.Objects.RemoveAll(o => o.ObjectType == 1 && gone.Contains(Convert.ToHexString(o.Payload, 0, 8)));
                // and the library threads that watched them (a car's kill line, the ally's friendly-fire chatter): their car is gone
                var goneKeys = stock.Actors.Where(a => a.Id is not null && gone.Contains(Convert.ToHexString(a.Id))).Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
                var globalsByName = src.Globals.GroupBy(g => g.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
                src.Threads.RemoveAll(t => libraryThreads.Contains(t) && (t.Named?.Select(x => x.Global) ?? t.Positional ?? [])
                    .Any(g => globalsByName.TryGetValue(g, out var v) && v is { Kind: RefKind.Actor, Key: { } k } && goneKeys.Contains(k)));
                break;
            default: throw new RemixError($"idle_cars '{spec.IdleCars}': remove or keep");
        }
        var edited = doc.ToBytes();
        byte[] compiled;
        try { compiled = MissionScriptFile.Compile(src, edited); }
        catch (ScriptError ex) { throw new RemixError($"compiling: {ex.Message}"); }

        var text = Text(spec);
        var source = $"# generated by the remix builder from {spec.Id}'s description on {spec.Base}: not stock content, never in the repository\n"
            + MissionScriptYaml.Write(src);
        var marks = RemixMarks.Of(spec, new Vector2(player.Position.X, player.Position.Z), name => PlacePoints(name, placeKeys, doc),
            name => actors.TryGetValue(name, out var key) ? newActors.FirstOrDefault(a => a.Key == key) is { } na ? Where(doc, na.Id!) : null
                : stock.Actors.FirstOrDefault(a => a.Key == name) is { Id: { } sid } ? Where(doc, sid) : null);
        return new RemixOutput(spec.Id.ToLowerInvariant(), compiled, text, source, Stamp(spec, bytes)) { Marks = marks.ToText() };
    }

    /// <summary>A place's points (x, z), by its name in the description (a place, or a stock path's key).</summary>
    private static IReadOnlyList<Vector2> PlacePoints(string name, Dictionary<string, string> keys, MapDocument doc)
    {
        if (!keys.TryGetValue(name, out var key)) return [];
        var labels = MissionScriptDecompiler.Keys(doc.Paths.Select(p => p.Name));
        int i = Array.IndexOf(labels, key);
        if (i < 0) i = doc.Paths.FindIndex(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? [] : [.. doc.Paths[i].Points.Select(p => new Vector2(p.X, p.Z))];
    }

    /// <summary>Where a placement stands (x, z), by its 8-byte id; null when it is gone.</summary>
    private static Vector2? Where(MapDocument doc, byte[] id) =>
        doc.Objects.FirstOrDefault(o => o.Payload.AsSpan(0, 8).SequenceEqual(id)) is { } o ? new Vector2(o.Position.X, o.Position.Z) : null;

    /// <summary>The stamp a build of this description on this base file carries: a build is current while it matches.</summary>
    public static string Stamp(RemixSpec spec, byte[] baseFile)
    {
        var yaml = new YamlDotNet.Serialization.SerializerBuilder().Build().Serialize(spec);
        return $"remix {Version} spec {Hash(Encoding.UTF8.GetBytes(yaml))} base {spec.Base.ToLowerInvariant()} {Hash(baseFile)}";
    }

    /// <summary>Builds every description whose files in <paramref name="outDir"/> are missing or stale; returns the stems built and the errors.</summary>
    public static (List<string> Built, List<string> Errors) BuildAll(GameInstall install, IEnumerable<(string Name, string Yaml)> specs, string outDir)
    {
        var built = new List<string>(); var errors = new List<string>();
        foreach (var (name, yaml) in specs)
        {
            try
            {
                var spec = RemixSpec.Parse(yaml);
                var stamp = Path.Combine(outDir, spec.Id.ToLowerInvariant() + ".remix");
                if (install.FindMission(spec.Base) is { } bp && File.Exists(stamp) && File.Exists(Path.Combine(outDir, spec.Id.ToLowerInvariant() + ".msn"))
                    && File.ReadAllText(stamp) == Stamp(spec, File.ReadAllBytes(bp))) continue;
                Build(install, spec).WriteTo(outDir);
                built.Add(spec.Id.ToLowerInvariant());
            }
            catch (Exception ex) when (ex is RemixError or IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
            {
                errors.Add($"{name}: {ex.Message}");
            }
        }
        return (built, errors);
    }

    /// <summary>The generated missions in a folder: stem and listed title, by stem.</summary>
    public static List<(string Stem, string Title)> Listed(string? dir)
    {
        var o = new List<(string, string)>();
        if (dir is null || !Directory.Exists(dir)) return o;
        foreach (var f in Directory.EnumerateFiles(dir, "*.remix").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var msn = Path.ChangeExtension(f, ".msn");
            if (!File.Exists(msn)) continue;
            string stem = Path.GetFileNameWithoutExtension(f);
            string title;
            try { title = WorldSettings.ArenaName(new MissionFile(msn))?.Trim() is { Length: > 0 } t ? t : stem.ToUpperInvariant(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { continue; }
            o.Add((stem, title));
        }
        return o;
    }

    // ---- the script's parts ----

    /// <summary>
    /// The base's script with only its library threads: every routine, table and data cell kept (the library threads name them;
    /// the mission's own routines stay in the code, started by nobody), the threads of the library routines kept but those on a
    /// dropped exit, the path table left to the mission (it gains the remix's places).
    /// </summary>
    internal static MissionScriptSource Library(MissionScriptSource stock, RemixSpec spec, bool nitro)
    {
        int lib = nitro ? NitroLibraryRoutines : LibraryRoutines;
        var dropKeys = new HashSet<string>(stock.Paths.Where(p => spec.DropExits.Contains(p.Label, StringComparer.OrdinalIgnoreCase)).Select(p => p.Key), StringComparer.Ordinal);
        foreach (var d in spec.DropExits)
            if (!stock.Paths.Any(p => p.Label.Equals(d, StringComparison.OrdinalIgnoreCase))) throw new RemixError($"drop_exits: {spec.Base} has no path {d}");
        var globals = stock.Globals.ToDictionary(g => g.Name, g => g.Value, StringComparer.Ordinal);
        bool Library(ThreadDef t) =>
            t.Script.StartsWith("script", StringComparison.Ordinal) && int.TryParse(t.Script.AsSpan(6).ToString().Split('+')[0], out int n) && n < lib;
        bool OnDropped(ThreadDef t) =>
            (t.Named?.Select(x => x.Global) ?? t.Positional ?? []).Any(g => globals.TryGetValue(g, out var v) && v.Kind == RefKind.Path && dropKeys.Contains(v.Key!));
        var src = new MissionScriptSource { Mission = spec.Id.ToUpperInvariant(), Prototypes = stock.Prototypes };
        src.Actors.AddRange(stock.Actors);
        src.Sounds.AddRange(stock.Sounds);
        src.Globals.AddRange(stock.Globals);
        src.Scripts.AddRange(stock.Scripts);
        src.Threads.AddRange(stock.Threads.Where(t => Library(t) && !OnDropped(t)));
        return src;
    }

    private static void Merge(MissionScriptSource into, MissionScriptSource part)
    {
        foreach (var a in part.Actors) into.Actors.Add(a);
        foreach (var s in part.Sounds) into.Sounds.Add(s);
        foreach (var g in part.Globals) into.Globals.Add(g);
        foreach (var s in part.Scripts) into.Scripts.Add(s);
        foreach (var t in part.Threads) into.Threads.Add(t);
    }

    private static string Actor(string name, Dictionary<string, string> mine, MissionScriptSource stock) =>
        mine.TryGetValue(name, out var k) ? k
        : stock.Actors.FirstOrDefault(a => a.Key.Equals(name, StringComparison.Ordinal))?.Key
        ?? throw new RemixError($"no actor '{name}': not in cars or bind, and the base's script names none");

    private static string Place(string name, Dictionary<string, string> keys) =>
        keys.TryGetValue(name, out var k) ? k : throw new RemixError($"no place '{name}': not in places, and the base has no such path");

    // ---- helpers ----

    private static MapObject Find(MapDocument doc, BindSpec b, string what)
    {
        var all = doc.Objects.Where(o => o.Name.Equals(b.Object, StringComparison.OrdinalIgnoreCase)).ToList();
        if (b.Nth < 0 || b.Nth >= all.Count) throw new RemixError($"{what}: the base has {all.Count} placement(s) of '{b.Object}', not a number {b.Nth}");
        return all[b.Nth];
    }

    /// <summary>The ground's height under a point (world units): the terrain's samples every 5 m, a tenth of a sample each, bilinear (the sim's Ground).</summary>
    internal static float GroundAt(TerrainTiles ter, float x, float z)
    {
        float fx = x / MissionFile.WorldScale, fz = z / MissionFile.WorldScale;
        int x0 = (int)MathF.Floor(fx), z0 = (int)MathF.Floor(fz);
        float tx = fx - x0, tz = fz - z0;
        float H(int a, int b) => ter.Height(a, b) ?? 0;
        float a0 = H(x0, z0) + (H(x0 + 1, z0) - H(x0, z0)) * tx, a1 = H(x0, z0 + 1) + (H(x0 + 1, z0 + 1) - H(x0, z0 + 1)) * tx;
        return (a0 + (a1 - a0) * tz) * 0.1f;
    }

    private static void SetTeam(MapObject o, int team)
    {
        if (team is < 0 or > 7) throw new RemixError($"team {team}: 0-7");
        o.ObjectFlags = (o.ObjectFlags & ~0x70000u) | ((uint)team << 16);
    }

    /// <summary>High bits for a new placement of <paramref name="name"/> that no placement of that name has: its id is unique.</summary>
    private static byte FreeFlags(MapDocument doc, string name)
    {
        var used = doc.Objects.Where(o => o.Name.Equals(name, StringComparison.Ordinal)).Select(o => o.NameFlags).ToHashSet();
        for (int f = 0; f < 256; f++) if (!used.Contains((byte)f)) return (byte)f;
        throw new RemixError($"no free id for another '{name}'");
    }

    private static (float X, float Z) XZ(double[] p, string what) =>
        p is [var x, var z] && double.IsFinite(x) && double.IsFinite(z) ? ((float)x, (float)z) : throw new RemixError($"{what}: a point is [x, z]");

    /// <summary>The teleport angle (degrees, as the script's teleport takes it) that faces from a to b.</summary>
    internal static int Heading(Vector3 a, Vector3 b)
    {
        double deg = Math.Atan2(b.X - a.X, b.Z - a.Z) * 180 / Math.PI;
        return (int)Math.Round((deg + 360) % 360) % 360;
    }

    private static void Name13(byte[] w, int at, string s)
    {
        if (s.Length > 12) throw new RemixError($"'{s}': longer than 12 characters");
        Array.Clear(w, at, 13);
        Encoding.Latin1.GetBytes(s, 0, s.Length, w, at);
    }

    /// <summary>
    /// The .npt: the objectives, then the failures, then a death line per objective (shown for a death while that objective is
    /// the open one: the templates' controllers keep its number, RemixTemplates.DeathLine).
    /// </summary>
    internal static string Text(RemixSpec spec)
    {
        var t = spec.Text;
        if (t.Objectives.Count is 0 or > ObjectiveText.MaxObjectives) throw new RemixError($"text: 1-{ObjectiveText.MaxObjectives} objectives");
        int finish = RemixTemplates.FinishObjective(spec);
        if (finish > 0 && t.Objectives.Count < finish) throw new RemixError($"finish: objective line {finish} (the finish step's) is missing");
        var deaths = Deaths(spec);
        if (t.Failures.Count < 2 || t.Failures.Count + deaths.Count > ObjectiveText.MaxFailures)
            throw new RemixError($"text: 2 or more failures (1 death, 2 leaving the map), at most {ObjectiveText.MaxFailures} with a death line per objective");
        foreach (var l in t.Objectives.Concat(t.Failures).Concat(deaths))
            if (l.Length == 0 || l.Any(c => c is '\r' or '\n' || c > 0xff)) throw new RemixError($"text line '{l}': one line of Latin-1");
        return string.Join("\r\n", [.. t.Objectives, "", "(failure)", .. t.Failures, .. deaths]) + "\r\n";
    }

    /// <summary>The death line for each objective: the description's, else one made from the objective's own line.</summary>
    public static List<string> Deaths(RemixSpec spec)
    {
        var t = spec.Text;
        var o = new List<string>();
        for (int i = 0; i < t.Objectives.Count; i++)
            o.Add(i < t.Deaths.Count && t.Deaths[i].Length > 0 ? t.Deaths[i] : "You got yourself killed. Still to do: " + Plain(t.Objectives[i]));
        return o;
    }

    /// <summary>An objective line as said: without its "(hidden)" mark and the job's number.</summary>
    public static string Plain(string line)
    {
        var l = line.StartsWith("(hidden)", StringComparison.Ordinal) ? line[8..] : line;
        if (l.StartsWith("Job ", StringComparison.Ordinal) && l.IndexOf(": ", StringComparison.Ordinal) is var k and > 0) l = l[(k + 2)..];
        return l.Length > 0 ? char.ToLowerInvariant(l[0]) + l[1..] : l;
    }

    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b))[..16].ToLowerInvariant();
}

/// <summary>The remix descriptions that ship with the port (src/I76.Formats/Remix/Scenarios, embedded): name and YAML.</summary>
public static class RemixCatalogue
{
    public static IReadOnlyList<(string Name, string Yaml)> Builtin()
    {
        var asm = typeof(RemixCatalogue).Assembly;
        var o = new List<(string, string)>();
        foreach (var n in asm.GetManifestResourceNames().Where(n => n.StartsWith("remix/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var s = asm.GetManifestResourceStream(n)!;
            o.Add((n["remix/".Length..], new StreamReader(s).ReadToEnd()));
        }
        return o;
    }
}
