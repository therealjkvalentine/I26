using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Cli;

/// <summary>
/// i76 remix build SPEC.yaml...|--builtin [--out DIR]: each remix description (or the ones the port ships) built into a mission
/// file, its objective text and its script's source in DIR (default: --remix-dir, else ./remix), never the install
/// (docs/remix-missions.md). Run one with i76 --remix-dir DIR run STEM.
/// </summary>
public static class RemixCommand
{
    public static int Run(GameInstall install, List<string> argv)
    {
        if (argv.Count < 3 || argv[1] != "build") { Console.Error.WriteLine("usage: i76 remix build SPEC.yaml...|--builtin [--out DIR]"); return 2; }
        int oi = argv.IndexOf("--out");
        string outDir = oi >= 0 ? argv[oi + 1] : GameInstall.UserMissionDir ?? "remix";
        var specs = new List<(string Name, string Yaml)>();
        for (int i = 2; i < argv.Count; i++)
        {
            if (i == oi) { i++; continue; }
            if (argv[i] == "--builtin") specs.AddRange(RemixCatalogue.Builtin());
            else specs.Add((argv[i], File.ReadAllText(argv[i])));
        }
        int bad = 0;
        foreach (var (name, yaml) in specs)
        {
            try
            {
                var output = RemixBuilder.Build(install, RemixSpec.Parse(yaml));
                foreach (var f in output.WriteTo(outDir)) Console.WriteLine($"wrote {f}");
                Console.WriteLine(output.Stamp);
            }
            catch (RemixError ex) { Console.Error.WriteLine($"{name}: {ex.Message}"); bad++; }
        }
        return bad == 0 ? 0 : 1;
    }
}
