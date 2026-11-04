using System.Globalization;
using I76.Formats.Assembly;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;

/// <summary>
/// `--find-strip INSTALL MISSION...`: straight lines in a mission's terrain that cross asphalt, a dirt road and off-road
/// ground, flat and passable, with a run-up, for the bass shaker's in-game check (I76_SPAWN_AT plus I76_TEST_CRUISE and
/// I76_TEST_HEADING drive one). Prints the best few: start x, z, heading (degrees from +Z toward +X) and the kinds along it.
/// </summary>
static class StripFinder
{
    const double Step = 2.5, RunUp = 250, Length = 900;

    public static void Run(string installDir, IEnumerable<string> missions)
    {
        var install = new GameInstall(installDir);
        foreach (var name in missions)
        {
            var path = install.FindMission(name);
            if (path is null) { Console.WriteLine($"{name}: not found"); continue; }
            var vfs = new AssetVfs(install);
            var asm = new ModelAssembler(vfs);
            MissionSetup setup;
            try { setup = MissionSetup.Build(vfs, new MissionWorld(vfs, asm, path), asm, null, script: false); }
            catch (Exception ex) { Console.WriteLine($"{name}: {ex.Message}"); continue; }
            IGround g = setup.World.Ground;
            var rng = new Random(1);
            int flat = 0, mix = 0;
            var found = new List<(double Score, double X, double Z, double Heading, string Kinds)>();
            // asphalt cells, sampled on a 20 m grid over the whole map
            var paved = new List<(double, double)>();
            for (double x = 0; x < 51200; x += 20)
                for (double z = 0; z < 51200; z += 20)
                    if (g.SurfaceId(x, z) == 2) paved.Add((x, z));
            Console.WriteLine($"{name}: {paved.Count} asphalt samples");
            for (int trial = 0; trial < Math.Min(4000, paved.Count * 4) && paved.Count > 0; trial++)
            {
                var (px, pz) = paved[rng.Next(paved.Count)];
                double hd = rng.NextDouble() * 360, r = hd * Math.PI / 180, dx = Math.Sin(r), dz = Math.Cos(r);
                // the asphalt somewhere in the measured stretch: start the line up to 600 m back from it
                double back = RunUp + rng.NextDouble() * (Length - RunUp);
                double sx = px - dx * back, sz = pz - dz * back;
                var kinds = new List<GroundKind>();
                bool ok = true;
                float h0 = g.BareHeight(sx, sz);
                double maxSlope = 0;
                float last = h0;
                for (double d = 0; d <= Length && ok; d += Step)
                {
                    double x = sx + dx * d, z = sz + dz * d;
                    if (x < 50 || z < 50 || x > 51150 || z > 51150 || g.Impassable(x, z)) { ok = false; break; }
                    float h = g.BareHeight(x, z);
                    maxSlope = Math.Max(maxSlope, Math.Abs(h - last) / Step);
                    last = h;
                    kinds.Add(WheelCrossings.At(g, x, h + 0.5, z));
                }
                if (!ok || maxSlope > 0.2) { flat++; continue; }
                // in the measured stretch (past the run-up): the longest run of each kind and how many changes
                var measured = kinds.Skip((int)(RunUp / Step)).ToList();
                double Longest(GroundKind k) { int best = 0, cur = 0; foreach (var q in measured) { cur = q == k ? cur + 1 : 0; best = Math.Max(best, cur); } return best * Step; }
                double pv = Longest(GroundKind.Paved), dr = Longest(GroundKind.DirtRoad), off = Longest(GroundKind.OffRoad);
                int changes = measured.Zip(measured.Skip(1)).Count(p => p.First != p.Second);
                if (pv < 25 || dr < 25 || off < 40 || changes > 10) { mix++; continue; }
                string seq = string.Join(" ", Runs(measured));
                found.Add((Math.Min(pv, Math.Min(dr, off)) - 10 * changes - 200 * maxSlope, sx, sz, hd, seq));
            }
            Console.WriteLine($"  {found.Count} found, {flat} too steep or blocked, {mix} without all three");
            foreach (var f in found.OrderByDescending(f => f.Score).Take(5))
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {f.X:0},{f.Z:0},{f.Heading:0}  score {f.Score:0}  after {RunUp:0} m: {f.Kinds}"));
        }
    }

    /// <summary>The kinds as runs: "O120 P45 D60" (letter and metres).</summary>
    static IEnumerable<string> Runs(List<GroundKind> k)
    {
        int i = 0;
        while (i < k.Count)
        {
            int j = i;
            while (j < k.Count && k[j] == k[i]) j++;
            yield return $"{"-ODPB"[(int)k[i]]}{(j - i) * Step:0}";
            i = j;
        }
    }
}
