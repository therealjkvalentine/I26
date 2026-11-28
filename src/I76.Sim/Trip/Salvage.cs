using I76.Sim.Damage;
using I76.Sim.Vehicles;

namespace I76.Sim.Trip;

/// <summary>One salvage item (the vsf's SALV record): the part's file key, its health, and whether the shell rolls a random condition (health −1, flag 1).</summary>
public sealed record SalvageItem(string Name, int Health, bool RandomCondition)
{
    /// <summary>The health a roguetrip job's prize arrives with: a random condition, never under half (TripInventory.AddSalvage).</summary>
    public const int Prize = -2;
}

/// <summary>
/// The mission's field salvage: one pool per
/// attempt, fed by every non-player car that dies (its surviving parts at
/// their health) and, on a win, the mission's .fsi list (random
/// condition). Every item is clamped to what the next mission allows and
/// joins the group of items with the same type and the same (clamped) name;
/// each group is thinned on its own before the pool goes to the shell. Off
/// entirely with "No Salv. Manag.".
/// </summary>
public sealed class SalvagePool
{
    /// <summary>The groups in creation order, as the engine's list: one per type and name, so four 14in tyres are a group of their own.</summary>
    private readonly List<(int Type, List<SalvageItem> Items)> _groups = [];
    public bool Disabled { get; }
    /// <summary>The clamp's level: the next mission's number + 1 (+ Trip+'s Loot up offset, the play fork's).</summary>
    public int Level { get; }
    /// <summary>The game kind from the mission stem: t → 2 trip, s/p → 3, m/n → 1.</summary>
    public int Kind { get; }
    private readonly string _stem;

    /// <param name="levelOffset">Trip+'s Loot up (the play fork's; docs/deviations.md "Trip+"): added to the mission number the clamp reads; 0 the original's.
    /// A roguetrip's pool (<see cref="Roguetrip"/>) lets every part of the table through.</param>
    public SalvagePool(string missionStem, bool noSalvageManagement = false, int levelOffset = 0)
    {
        _stem = missionStem.ToLowerInvariant();
        Disabled = noSalvageManagement;
        int number = _stem.Length > 1 && int.TryParse(new string(_stem.Skip(1).TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 39;
        Level = number + 1 + levelOffset;
        Kind = _stem.Length > 0 ? _stem[0] switch { 't' => 2, 's' or 'p' => 3, 'm' or 'n' => 1, _ => 0 } : 0;
    }

    public IEnumerable<SalvageItem> Items => _groups.SelectMany(g => g.Items);

    /// <summary>The clamp level that lets every part of the table through (the highest trip level is 16).</summary>
    public const int Unclamped = 17;

    /// <summary>
    /// A roguetrip job's pool (docs/remix-missions.md "Rosters and salvage"): the trip mission whose map it is for the stem (T04 and T12 as
    /// T03 and T11), but no tier clamp: the roster already put the cars at the player's gear, so a wreck gives what it carried.
    /// </summary>
    public static SalvagePool Roguetrip(string stem)
    {
        var p = new SalvagePool(stem);
        return new SalvagePool(stem, false, Unclamped - p.Level);
    }

    /// <summary>The groups as bytes, for a checkpoint (the game keeps them beside the session's snapshot).</summary>
    public byte[] Save()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(_groups.Count);
            foreach (var (type, items) in _groups)
            {
                w.Write(type);
                w.Write(items.Count);
                foreach (var it in items) { w.Write(it.Name); w.Write(it.Health); w.Write(it.RandomCondition); }
            }
        }
        return ms.ToArray();
    }

    /// <summary>The groups as <see cref="Save"/> wrote them, in place of these.</summary>
    public void Load(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        _groups.Clear();
        for (int g = r.ReadInt32(); g > 0; g--)
        {
            int type = r.ReadInt32();
            var items = new List<SalvageItem>();
            for (int n = r.ReadInt32(); n > 0; n--) items.Add(new SalvageItem(r.ReadString(), r.ReadInt32(), r.ReadBoolean()));
            _groups.Add((type, items));
        }
    }

    /// <summary>The type from the file key: .wdf 0 wheel, .gdf 1 weapon, eng… 2, sus… 3, bra… 4, spc… 5; −1 unknown.</summary>
    public static int TypeOf(string path) => I76.Formats.Remix.PartTiers.TypeOf(path);

    /// <summary>
    /// The type from the name, a wheel turned into the plain
    /// style in its size (wauto_0?), the tier clamp, then appended to the
    /// group whose type matches and whose first item has the same name
    /// (stricmp), or to a new group at the end of the list. Measured: a T03
    /// win with every car killed has a group of 48 13in tyres and a separate
    /// group of the .fsi's four 14in ones. False when nothing was added for
    /// a name of no type or not in the tier table.
    /// </summary>
    public bool Add(string path, int health, bool randomCondition)
    {
        if (Disabled) return true;
        int type = TypeOf(path);
        if (type < 0) return false;     // "Unknown Salvage type %s found"
        var name = path.ToLowerInvariant();
        if (type == 0 && name.Length >= 7) name = "wauto_0" + name[7..];
        if (Clamp(name, type, Level, Kind) is not { } clamped) return false;
        var g = _groups.FirstOrDefault(x => x.Type == type && x.Items[0].Name.Equals(clamped, StringComparison.OrdinalIgnoreCase));
        if (g.Items is null) { g = (type, []); _groups.Add(g); }
        g.Items.Add(new SalvageItem(clamped, health, randomCondition));
        return true;
    }

    /// <summary>The pool fed as the engine feeds it: every car but the player's, at the moment its salvage is taken (<see cref="DamageModel.Salvageable"/>).</summary>
    public void Watch(DamageModel damage, Vehicle? player) => damage.Salvageable += v => { if (v != player) AddDeadCar(v); };

    /// <summary>
    /// A dead non-player car's parts whose health is above 0 —
    /// the six wheels (their pair's .wdf), engine, suspension, brakes, the
    /// weapons (their .gdf) at their current health, and the fitted specials
    /// at 1, whatever their health (the original's amount for a special).
    /// A part the tier table does not know (the T16 tanks' gtktank, gtptank,
    /// tthowitz) ends the wreck's collection there: its later weapons and its
    /// specials are lost, as in the original.
    /// </summary>
    public void AddDeadCar(Vehicle v)
    {
        if (Disabled || v.Health is not { } d || v.Loadout is not { } lo) return;
        for (int k = 0; k < 6; k++)
            if (d.Slots[k] is WheelPart w && w.Hp > 0 && lo.Wdf[k / 2] is { Length: > 0 } wdf && !wdf.Equals("null", StringComparison.OrdinalIgnoreCase)
                && !Add(wdf, w.Hp, false)) return;
        if (d.Engine is { Hp: > 0 } e && !Add($"eng{lo.Engine + 1:00}", e.Hp, false)) return;
        if (d.Suspension is { Hp: > 0 } s && !Add($"sus{lo.Suspension + 1:00}", s.Hp, false)) return;
        if (d.Brake is { Hp: > 0 } b && !Add($"bra{lo.Brakes + 1:00}", b.Hp, false)) return;
        for (int k = 10; k <= 16; k++)
            if (d.Slots[k] is WeaponPart wp && wp.Hp > 0 && !Add(wp.Weapon.Def.File, wp.Hp, false)) return;
        for (int k = 17; k <= 19; k++)
            if (d.Slots[k] is SpecialPart sp && !Add($"spc{sp.Type:00}", 1, false)) return;
    }

    /// <summary>
    /// The end of a won mission (the original's start-up): nothing at all after
    /// T12; T04 keeps no salvage; otherwise the .fsi list joins the pool and
    /// each group is thinned: a wheel group to four, a special
    /// group to one, any other group to 3, 1 or 2 (one rand(): &amp;1 → 3,
    /// else &amp;2 → 1, else 2), never more than it holds. The picks are
    /// rand() % count into the group's original order, drawn again when that
    /// item is already taken, and kept in pick order. Measured against the
    /// original's own draws (tests/…/data/salvage-measured.csv). Returns null
    /// when the mission writes no state at all (T12).
    /// </summary>
    public List<SalvageItem>? Finish(Func<string, byte[]?> read, Func<int> rand)
    {
        if (_stem.StartsWith("t12")) return null;
        if (_stem.StartsWith("t04") || Disabled) return [];
        if (read($"{_stem}.fsi") is { } fsi)
            foreach (var raw in System.Text.Encoding.Latin1.GetString(fsi).Split('\n'))
            {
                var line = raw.Trim('\r', ' ', '\t');
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int sp = line.IndexOf(' ');
                Add(sp < 0 ? line : line[..sp], -1, true);
            }
        var o = new List<SalvageItem>();
        foreach (var (type, items) in _groups)
        {
            int keep;
            if (type == 0) keep = 4;
            else if (type == 5) keep = 1;
            else { int r = rand(); keep = (r & 1) != 0 ? 3 : (r & 2) != 0 ? 1 : 2; }
            var left = items.ToArray<SalvageItem?>();
            for (int i = System.Math.Min(keep, items.Count); i > 0; i--)
            {
                int k;
                do k = rand() % items.Count; while (left[k] is null);
                o.Add(left[k]!);
                left[k] = null;
            }
        }
        return o;
    }

    // ---- the tier clamp (the table: I76.Formats.Remix.PartTiers, which the roguetrip's rosters also read) ----

    /// <summary>
    /// The name an item arrives as, or null if no entry matches (the item is
    /// dropped). On a match at k, walk back to the first entry that is
    /// "always" or allowed at this level for this kind; if it walked back,
    /// that entry's name goes in at the table's offset (for the weapons,
    /// offset 0, it replaces the stem and the extension stays).
    /// </summary>
    public static string? Clamp(string name, int type, int level, int kind)
    {
        var (off, entries) = I76.Formats.Remix.PartTiers.Tables[type];
        if (name.Length < off) return null;
        int k = Array.FindIndex(entries, e => string.CompareOrdinal(name, off, e.Name, 0, e.Name.Length) == 0);
        if (k < 0) return null;
        int j = k;
        while (j > 0 && !entries[j].Always && !(kind == 2 && entries[j].TripMin <= level) && !(kind == 3 && entries[j].MeleeMin <= level)) j--;
        if (j == k) return name;
        var e = entries[j];
        if (off == 0)
        {
            int dot = name.IndexOf('.');
            return e.Name + (dot < 0 ? "" : name[dot..]);
        }
        return name[..off] + e.Name + (name.Length > off + e.Name.Length ? name[(off + e.Name.Length)..] : "");
    }
}
