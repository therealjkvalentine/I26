using System.Numerics;
using System.Text.Json;
using I76.Formats.Assembly;
using I76.Formats.Mission;
using I76.Formats.Vfs;

namespace I76.Cli;

/// <summary>
/// i76 tex-seams OUT.json [--only GLOB]: where the install's textures meet a neighbour or themselves, for the HD pack's
/// seam audit and tools/upscale-textures.py --neighbours. Models (TextureSeams over every .vcf at detail 1 with its damage
/// entries 0..3, the cockpit at detail 5, every .sdf), the road strips (Roads: quad j's far edge against quad j+1's near
/// edge, a piece's last quad against the next piece's first), the horizon ring's panels side by side (.hzd, round the
/// ring), and what repeats: the ground families (each mission's largest member), the sky (its clouds repeat), faces whose
/// UVs tile. Texture names are the stems the export and the pack use (lower case); UVs as the GEO stores them (v = 0 the
/// first stored row).
/// </summary>
public static class TexSeamsCommand
{
    public static int Run(GameInstall install, List<string> argv)
    {
        var vfs = new AssetVfs(install);
        var res = new TextureResolver(vfs);
        string outPath = argv.Count > 1 ? argv[1] : throw new ArgumentException("tex-seams needs OUT.json");
        int oi = argv.IndexOf("--only");
        string only = oi > 0 ? argv[oi + 1] : "*";
        static string Stem(string member) => Path.GetFileNameWithoutExtension(member.Replace('\\', '/').Split('/', ':').Last()).ToLowerInvariant();
        bool Want(string n) => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(only, n, ignoreCase: true);
        var seams = new List<object>();
        var seen = new HashSet<string>();
        var tiles = new Dictionary<string, (bool U, bool V, HashSet<string> Why)>();
        void Tile(string tex, bool u, bool v, string why)
        {
            if (!u && !v) return;
            var s = Stem(tex);
            if (!tiles.TryGetValue(s, out var t)) t = (false, false, []);
            t.Why.Add(why);
            tiles[s] = (t.U || u, t.V || v, t.Why);
        }
        static float[] Uv(Vector2 a, Vector2 b) => [MathF.Round(a.X, 5), MathF.Round(a.Y, 5), MathF.Round(b.X, 5), MathF.Round(b.Y, 5)];
        void Add(string cls, string model, string partA, string partB, string ta, Vector2 a0, Vector2 a1, string tb, Vector2 b0, Vector2 b1, float len, float angle, Vector2? ac = null, Vector2? bc = null)
        {
            string sa = Stem(ta), sb = Stem(tb);
            var key = $"{sa}|{string.Join(",", Uv(a0, a1))}|{sb}|{string.Join(",", Uv(b0, b1))}";
            var rev = $"{sb}|{string.Join(",", Uv(b0, b1))}|{sa}|{string.Join(",", Uv(a0, a1))}";
            if (seen.Contains(rev) || !seen.Add(key)) return;
            seams.Add(new { cls, model, parts = new[] { partA, partB }, a = sa, auv = Uv(a0, a1), b = sb, buv = Uv(b0, b1), ac = ac is { } x && float.IsFinite(x.X + x.Y) ? new[] { MathF.Round(x.X, 5), MathF.Round(x.Y, 5) } : null, bc = bc is { } y && float.IsFinite(y.X + y.Y) ? new[] { MathF.Round(y.X, 5), MathF.Round(y.Y, 5) } : null, len = MathF.Round(len, 4), angle = MathF.Round(angle, 1) });
        }
        void Model(string cls, string def, AssembledModel m, int frame)
        {
            var faces = TextureSeams.Faces(m, res, frame);
            foreach (var f in faces) { var (u, v) = TextureSeams.Tiles(f); Tile(f.Texture, u, v, cls + ":" + def); }
            foreach (var s in TextureSeams.Find(faces))
                Add(cls, def, s.PartA, s.PartB, s.A.Texture, s.A.Uv0, s.A.Uv1, s.B.Texture, s.B.Uv0, s.B.Uv1, s.Length, s.Angle, s.A.Centre, s.B.Centre);
        }
        var defs = vfs.Names.Where(n => n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".sdf", StringComparison.OrdinalIgnoreCase))
            .Where(Want).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        int done = 0;
        foreach (var def in defs)
        {
            try
            {
                if (def.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase))
                {
                    if (new ModelAssembler(vfs).Assemble(def) is { } car)
                        for (int frame = 0; frame <= DamageTextures.WreckFrame; frame++) Model("car", def, car, frame);
                    if (new ModelAssembler(vfs) { Detail = '5' }.Assemble(def) is { } pit && pit.Parts.Count > 0) Model("cockpit", def, pit, 0);
                }
                else if (new ModelAssembler(vfs).Assemble(def) is { } sdf) Model("scenery", def, sdf, 0);
            }
            catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException) { Console.Error.WriteLine($"{def}: {e.Message}"); }
            if (++done % 200 == 0) Console.Error.WriteLine($"{done}/{defs.Count} models, {seams.Count} seams");
        }
        // the road strips: one whole texture per quad, u across (0 the right edge, 1 the left), v along; quad j's v = 1 meets quad j+1's v = 0
        var names = Roads.DefaultTextures;
        for (int kind = 0; kind < 3; kind++)
            foreach (bool far in new[] { false, true })
                foreach (var (j, k) in Enumerable.Range(0, 10).Select(j => (j, (j + 1) % 10)).Append((3, 0)))
                {
                    if (res.FindByStem(names[Roads.TextureIndex((uint)kind, j, far)]) is not { } a || res.FindByStem(names[Roads.TextureIndex((uint)kind, k, far)]) is not { } b) continue;
                    Add("road", $"kind {kind}{(far ? " far" : "")}", $"quad {j}", $"quad {k}", a, new(0, 1), new(1, 1), b, new(0, 0), new(1, 0), 0, 0, new(0.5f, 0.5f), new(0.5f, 0.5f));
                }
        // missions: the ground family (its largest member repeats), the sky (repeats), the horizon ring's panels
        foreach (var ext in GameInstall.MissionExtensions)
            foreach (var path in install.ListMissions(ext))
            {
                MissionFile msn;
                WorldSettings? w;
                List<string> refs;
                try { msn = new MissionFile(path); w = WorldSettings.Read(msn); refs = msn.WdefRefs(); }
                catch (Exception) { continue; }
                var tp = refs.FirstOrDefault(r => r.EndsWith(".map", StringComparison.OrdinalIgnoreCase) && !r.StartsWith("zmap", StringComparison.OrdinalIgnoreCase)
                    && !r.StartsWith("nk_", StringComparison.OrdinalIgnoreCase) && !r.Equals(w?.Sky, StringComparison.OrdinalIgnoreCase));
                if (tp is { Length: >= 8 })
                {
                    var best = Enumerable.Range(1, 9).Select(i => tp[..4] + i + tp[5..]).Select(res.FindByStem).OfType<string>()
                        .Select(mm => (mm, t: res.Load(mm))).Where(x => x.t is not null).OrderByDescending(x => x.t!.Width * x.t.Height).FirstOrDefault();
                    if (best.t is not null) Tile(best.mm, true, true, "ground:" + Path.GetFileName(path));
                }
                if (w?.Sky is { Length: > 0 } sky && res.FindByStem(sky) is { } sm) Tile(sm, true, true, "sky:" + Path.GetFileName(path));
                if (w is { Horizon.Length: > 0 } && HorizonList.Parse(vfs.TryRead(w.Horizon)) is { } hz)
                    for (int i = 0; i < HorizonList.Panels; i++)
                    {
                        if (res.FindByStem(hz[i]) is not { } a || res.FindByStem(hz[(i + 1) % HorizonList.Panels]) is not { } b) continue;
                        if (Stem(a) == Stem(b)) Tile(a, true, false, "horizon:" + Path.GetFileName(w.Horizon));
                        else Add("horizon", Path.GetFileName(w.Horizon), $"panel {i}", $"panel {(i + 1) % HorizonList.Panels}", a, new(1, 0), new(1, 1), b, new(0, 0), new(0, 1), 0, 0, new(0.5f, 0.5f), new(0.5f, 0.5f));
                    }
            }
        var doc = new
        {
            note = "i76 tex-seams: where textures meet (seams: side a's UVs at the edge's two ends against side b's, v = 0 the first stored row) and what repeats (tiles: u, v)",
            seams,
            tiles = tiles.OrderBy(k => k.Key, StringComparer.Ordinal).ToDictionary(k => k.Key, k => new { u = k.Value.U, v = k.Value.V, why = k.Value.Why.Order().Take(6).ToArray(), uses = k.Value.Why.Count }),
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(doc));
        Console.WriteLine($"{defs.Count} models, {seams.Count} seams, {tiles.Count} repeating textures -> {outPath}");
        return 0;
    }
}
