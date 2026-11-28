using I76.Formats.Assembly;
using I76.Formats.Fsm;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Rng;
using I76.Sim.Session;
using I76.Sim.Trip;
using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>
/// A roguetrip job's field salvage measured headless (docs/remix-missions.md "Rosters and salvage"; i76 remix salvage, i76 remix check,
/// RemixSalvageTests): the built job run in the player's car with every hostile car destroyed early (the script's destroy, as a clean win's
/// kills), the pool fed where the game feeds it (each wreck's parts after the component wipe), then thinned as a won job's is and the
/// job's prize added. A tool, not part of the simulation: it reads a session it made itself.
/// </summary>
public static class RemixSalvage
{
    /// <summary>A car the job fields: its name in the description, its file, and its ladder entry (null: not on the ladder, a lead or the police).</summary>
    public sealed record Fielded(string Name, string Vcf, RemixRoster.Car? Ladder);

    /// <summary>A clean win's pool and the cars it came from.</summary>
    public sealed record Win(List<Fielded> Cars, int Destroyed, SalvagePool Pool, IReadOnlyList<string> Prize);

    /// <summary>
    /// The job (built into the generated missions' folder as <paramref name="mission"/>) run in <paramref name="car"/> with its hostile cars
    /// destroyed at 1 s; the pool is a roguetrip's (no clamp) or, <paramref name="legacy"/>, the builder v6 one's (the trip mission's level).
    /// </summary>
    public static Win CleanWin(AssetVfs vfs, ModelAssembler asm, string mission, RemixSpec spec, string car = "vppirna1.vcf", bool legacy = false, uint seed = 1)
    {
        var path = vfs.Install.FindMission(mission) ?? throw new FileNotFoundException(mission);
        var setup = MissionSetup.Build(vfs, new MissionWorld(vfs, asm, path), asm, car, rand: new CrtRand(seed), seed: unchecked((int)seed));
        var s = new SimSession(setup, SessionOptions.SinglePlayer, SimRate.Default);
        foreach (var h in setup.Humans) s.AddDriver(h);
        var me = setup.World.Player!;
        string stem = RemixRun.SalvageStem(spec.Base);
        var pool = legacy ? new SalvagePool(stem) : SalvagePool.Roguetrip(stem);
        pool.Watch(setup.Damage, me);
        int destroyed = 0;
        setup.Damage.Salvageable += v => { if (v != me) destroyed++; };
        var vm = setup.Script!.Vm!;
        var keys = MissionScriptDecompiler.Keys(vm.Actors.Select(a => a.Label));
        Vehicle? CarOf(string name)
        {
            int i = Array.IndexOf(keys, spec.Cars.ContainsKey(name) || spec.Bind.ContainsKey(name) ? RemixBuilder.PathPrefix + name : name);
            return i >= 0 && vm.Actors[i].Handle is Entity { Vehicle: { } v } ? v : null;
        }
        var hostiles = RemixRoles.Hostiles(spec).Select(r => r.Car).Distinct().ToList();
        var cars = hostiles.Select(n => new Fielded(n, spec.Cars.TryGetValue(n, out var c) ? c.Vcf : n, spec.Cars.TryGetValue(n, out var c2) ? RemixRoster.Of(c2.Vcf) : null)).ToList();
        int hz = SimRate.Default.Label, kill = hz;
        for (int t = 0; t < kill + 4 * hz; t++)
        {
            if (t == kill)
                foreach (var n in hostiles)
                    if (CarOf(n) is { } v && !Damage.DamageModel.IsDead(v)) setup.Damage.Destroy(v);
            s.Tick([default]);
        }
        return new Win(cars, destroyed, pool, spec.Prize ?? []);
    }

    /// <summary>What a won job offers after the trip's thinning, with the prize (not in a legacy job), its parts as the field shows them.</summary>
    public static List<PartNode> Offered(Win w, PartsDatabase db, int draw, bool legacy = false)
    {
        var pool = new SalvagePool("t01");
        pool.Load(w.Pool.Save());
        var rand = new CrtRand((uint)draw);
        var items = pool.Finish(_ => null, rand.Next) ?? [];
        if (!legacy) items.AddRange(w.Prize.Select(k => new SalvageItem(k, SalvageItem.Prize, true)));
        var inv = new TripInventory();
        inv.AddSalvage(items, db, rand.Next);
        return [.. inv.Nodes];
    }

    /// <summary>A part's slot for the gear (0 engine, 1 suspension, 2 brakes, 3 wheels, 4 a gun), −1 for a special.</summary>
    public static int Slot(PartDef p) => p.Type switch { 2 => 0, 3 => 1, 4 => 2, 5 => 3, 7 or 8 => 4, _ => -1 };

    /// <summary>A part's trip level (PartTiers; a wheel by its letter).</summary>
    public static int Level(PartDef p) => PartTiers.Level(p.Stem);

    /// <summary>Usable: a third of its hit points or more (the garage's severity 1 or 2, not in the red).</summary>
    public static bool Usable(PartNode n) => PartsDatabase.Severity(n.Health, n.Part.MaxHp) <= 2;

    /// <summary>Whether a part is at or above the player's gear in its slot.</summary>
    public static bool AtGear(PartNode n, RemixRoster.Gear g) => Slot(n.Part) is var k and >= 0 && Level(n.Part) >= g.Slot(k);
}
