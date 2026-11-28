using System.Globalization;
using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Cli;

/// <summary>
/// i76 remix ladder [--table] [--check]: every stock car file (v*.vcf) of the install with its parts' trip levels (PartTiers), its hit
/// points and guns' damage a second against the melee Piranha's, its level and strength, and whether a job may field it
/// (RemixRoster.Eligible); --table prints the C# rows of RemixRoster.Cars (stems and numbers only); --check compares the built-in table
/// with the install and exits 1 on a difference.
/// </summary>
public static class RemixRosterCommand
{
    public sealed record Row(string Stem, RemixRoster.Gear Gear, int Armour, double Hp, double Dps, double Strength, string? Why, List<string> Keys)
    {
        public RemixRoster.Car Car => new(Stem, Gear.Engine, Gear.Suspension, Gear.Brakes, Gear.Wheels,
            Keys.Where(k => PartTiers.TypeOf(k) == 1).OrderByDescending(PartTiers.Level).ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault() ?? "", Math.Round(Strength, 2), Why is null);
    }

    /// <summary>Every stock car file of the install, measured.</summary>
    public static List<Row> Measure(AssetVfs vfs)
    {
        var me = I76.Sim.Vehicles.VehicleFactory.Create(vfs, "vppirna1.vcf");
        var (php, pdps) = I76.Sim.Missions.RemixCheck.Stats(me);
        var rows = new List<Row>();
        foreach (var name in vfs.Names.Where(n => n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(n).StartsWith('v')).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var stem = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
            List<string> keys;
            int armour;
            try { (keys, armour) = RemixRoster.Read(vfs, name); }
            catch (Exception) { continue; }
            double hp = 0, dps = 0;
            try { (hp, dps) = I76.Sim.Missions.RemixCheck.Stats(I76.Sim.Vehicles.VehicleFactory.Create(vfs, name)); }
            catch (Exception) { }
            double st = php <= 0 || pdps <= 0 ? 0 : Math.Sqrt(hp / php * (dps / pdps));
            rows.Add(new Row(stem, RemixRoster.GearOf(keys), armour, hp / php, pdps > 0 ? dps / pdps : 0, st, RemixRoster.Eligible(stem, keys, dps), keys));
        }
        return rows;
    }

    public static int Ladder(GameInstall install, List<string> argv)
    {
        var inv = CultureInfo.InvariantCulture;
        var rows = Measure(new AssetVfs(install));
        if (argv.Contains("--table"))
        {
            foreach (var r in rows.OrderBy(r => r.Why is null ? 0 : 1).ThenBy(r => r.Car.Level).ThenBy(r => r.Stem, StringComparer.Ordinal))
            {
                var c = r.Car;
                Console.WriteLine(string.Create(inv, $"        new(\"{c.Stem}\", {c.Engine}, {c.Suspension}, {c.Brakes}, {c.Wheels}, \"{c.Best}\", {c.Strength:0.00}{(c.Enemy ? "" : ", false")}),{(r.Why is null ? "" : "   // " + r.Why)}"));
            }
            return 0;
        }
        if (argv.Contains("--check"))
        {
            int bad = 0;
            foreach (var r in rows)
            {
                var want = r.Car;
                if (RemixRoster.Of(r.Stem) is not { } have) { Console.WriteLine($"{r.Stem}: not in the table"); bad++; continue; }
                if (have != want) { Console.WriteLine($"{r.Stem}: table {have}, install {want}"); bad++; }
            }
            foreach (var c in RemixRoster.Cars.Where(c => rows.All(r => r.Stem != c.Stem))) { Console.WriteLine($"{c.Stem}: not in the install"); bad++; }
            Console.WriteLine(bad == 0 ? $"the ladder matches the install ({rows.Count} cars)" : $"{bad} differences");
            return bad == 0 ? 0 : 1;
        }
        Console.WriteLine("stem      level   E  S  B  W  G  armour   hp   dps  str   parts");
        foreach (var r in rows.OrderBy(r => r.Why is null ? 0 : 1).ThenBy(r => r.Car.Level))
            Console.WriteLine(string.Create(inv,
                $"{r.Stem,-9} {r.Car.Level,5:0.00}  {r.Gear.Engine,2} {r.Gear.Suspension,2} {r.Gear.Brakes,2} {r.Gear.Wheels,2} {r.Gear.Gun,2}  {r.Armour,6} {r.Hp,5:0.00} {r.Dps,5:0.00} {r.Strength,4:0.00}  {string.Join(" ", r.Keys)}{(r.Why is null ? "" : "   [" + r.Why + "]")}"));
        return 0;
    }
}
