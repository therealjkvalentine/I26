using I76.Formats.Imaging;
using I76.Sim.Fsm;
using I76.Sim.Math;
using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>
/// Co-op Scaling (the port's; docs/deviations.md, docs/multiplayer.md "Co-op Scaling"): with N humans in the mission, each enemy
/// car the script sets going is joined by up to min(N − 1, <see cref="CoopSetup.ExtraEnemies"/>) copies of it beside it. A copy
/// takes every order and setting the script gives its original, aimed at the human nearest to the copy; once the original is
/// dead it attacks the nearest human until it is destroyed. The script never sees the copies: they have no instance id, are
/// left out of its vehicle lists (allEnemyDead, nearestEnemy, isWithinEnemy) and hold no actor slot, so its counters and
/// objectives follow the originals alone; whoShot and the like name a copy's original instead (<see cref="ScriptHandle"/>), so no
/// copy ever enters the actor table. A car the mission is about (<see cref="CoopKeyCars"/>: one the script decides an objective
/// or the mission on alone, or films in a cut-scene) is never copied; where <see cref="CoopEscorts"/> names it, it gets escort cars
/// instead.
/// With the host's coop_copy_key_cars (<see cref="CoopSetup.CopyKeyCars"/>, the fork's earlier rule) a key car is copied as any
/// other and gets no escorts, and every copy is told apart: it and its original do each other no damage (<see cref="Spares"/>),
/// so a copy cannot hurt, disable or kill the car the mission is about, nor mark it shot or rammed; a path the original drives
/// (goto, evade, race) or a car it follows, the copy does not drive a second time: it follows its original, keeping station
/// beside and behind it, and fires at the human the original's order is against; each copy wears a respray of its own
/// (<see cref="ExtraPaint"/>), the original keeping the mission's paint, and its name carries its number (<see cref="ExtraName"/>);
/// players see it as the car it copies (<see cref="OriginalName"/>).
/// </summary>
public sealed partial class ScriptHost
{
    /// <summary>The copies of an original (living or not), in the order they were made.</summary>
    public IEnumerable<Entity> ExtrasOf(Entity original) => Entities.Where(x => x.ExtraOf == original && !x.Removed);

    /// <summary>The entity and, for an original under Co-op Scaling, its living copies: what a setting the script gives it applies to.</summary>
    private IEnumerable<Entity> WithExtras(object o)
    {
        var e = E(o);
        yield return e;
        if (Coop is not { ExtraEnemies: > 0 } || e.ExtraOf is not null || _escorted.ContainsKey(e)) yield break;   // an escort keeps its own settings
        foreach (var x in ExtrasOf(e).ToList()) if (!x.Dead) yield return x;
    }

    /// <summary>The cars the mission is about (<see cref="CoopKeyCars"/>), which Co-op Scaling leaves alone; found at load.</summary>
    public IReadOnlyDictionary<Entity, CoopKeyCars.Why> KeyCars => _keyCars;
    private readonly Dictionary<Entity, CoopKeyCars.Why> _keyCars = [];

    /// <summary>
    /// Finds the key cars in the mission's script (its FSM chunk) once the VM has bound its actors, and those of them
    /// <see cref="CoopEscorts"/> gives escorts in <paramref name="mission"/> (the mission file's stem); only under Co-op Scaling.
    /// </summary>
    internal void FindKeyCars(byte[] fsmPayload, FsmVm vm, string mission)
    {
        _keyCars.Clear();
        _escorted.Clear();
        if (Coop is not { ExtraEnemies: > 0 } c) return;
        var a = CoopKeyCars.Analyze(fsmPayload, i => i >= 0 && i < vm.Actors.Count && vm.Actors[i].Handle is { } h && IsPlayerHandle(h));
        foreach (var (i, why) in a.Key)
            if (i >= 0 && i < vm.Actors.Count && vm.Actors[i].Handle is Entity e && !IsHumanEntity(e)) _keyCars[e] = _keyCars.GetValueOrDefault(e) | why;
        if (!c.CopyKeyCars) FindEscorted(mission);
    }

    /// <summary>
    /// A car Co-op Scaling copies: an AI ground car (no helicopter or gun emplacement) on a team neither the humans' nor nobody's,
    /// and not one the mission is about (but with coop_copy_key_cars).
    /// </summary>
    private bool Scalable(Entity e) => EnemyGroundCar(e) && (Coop is { CopyKeyCars: true } || !_keyCars.ContainsKey(e));

    /// <summary>An AI ground car (no helicopter or gun emplacement, no copy) on a team neither the humans' nor nobody's.</summary>
    private bool EnemyGroundCar(Entity e) =>
        e.Vehicle is { IsHeli: false, IsEmplacement: false } && e.ExtraOf is null && !IsHumanEntity(e) && e.Team != 0 && Lead is { } lead && e.Team != lead.Team;

    /// <summary>What the script may hold of an entity: a copy is its original (whoShot, whoRammed, whoAttacked never name a copy).</summary>
    private static Entity? ScriptHandle(Entity? e) => e?.ExtraOf ?? e;

    /// <summary>
    /// With coop_copy_key_cars, a packet <paramref name="victim"/> does not take from <paramref name="attacker"/> (the damage model's <see cref="Damage.DamageModel.Spared"/>):
    /// a copy and its original, or two copies of one original, are one car's escort and never hurt each other. So a copy cannot
    /// damage, disable or kill the car the script tests (its hpLesser, isDead) nor set its isShot, isRammed or isAttacked.
    /// </summary>
    internal bool Spares(Vehicle victim, Vehicle attacker)
    {
        if (Of(victim) is not { } v || Of(attacker) is not { } a) return false;
        var ov = v.ExtraOf ?? v; var oa = a.ExtraOf ?? a;
        return ov == oa && (v.ExtraOf is not null || a.ExtraOf is not null);
    }

    /// <summary>
    /// An order to an original reaches its copies: made the first time the script orders it (as many as the humans then present
    /// call for), then each living copy given the same order, a human target or reference taken as the human nearest the copy
    /// (with coop_copy_key_cars its own plan from the order: the class summary). A key car with an escort rule gets its escorts then
    /// instead, which take no orders from the script (<see cref="ExtrasFrame"/>).
    /// </summary>
    private void OrderExtras(Entity e, Entity? target, AiOrder order, IReadOnlyList<System.Numerics.Vector3>? path, float speed, int followFlag, float f1, float f2, object? reference)
    {
        if (Coop is not { ExtraEnemies: > 0 } c || e.ExtraOf is not null) return;
        if (!e.Scaled)
        {
            e.Scaled = true;
            int n = System.Math.Min(c.Seats.Count(s => s.Present) - 1, c.ExtraEnemies);
            if (_escorted.TryGetValue(e, out var rule)) { SpawnEscorts(c, e, rule, n); return; }
            if (!Scalable(e))
            {
                if (_keyCars.TryGetValue(e, out var why)) Log($"co-op: {e.Name} is a car the mission is about ({why}): no extras");
                return;
            }
            for (int k = 0; k < n; k++) SpawnExtra(c, e, k + 1);
        }
        if (_escorted.ContainsKey(e)) return;
        int i = 0;
        foreach (var x in ExtrasOf(e).ToList())
        {
            int k = i++;
            if (x.Dead) continue;
            if (c.CopyKeyCars && order is AiOrder.Goto or AiOrder.Evade or AiOrder.Race or AiOrder.Follow)
            {
                // the escort: station on the original, firing at the human the original's order is against (a goto's: none;
                // a follow's: its fire reference when the order fires)
                var at = order == AiOrder.Follow ? (followFlag != 0 && reference is Entity r ? Aimed(r, x) : null) : Aimed(target, x);
                var (side, back) = Station(k);
                Order(x, e, AiOrder.Follow, null, 0, at is not null ? 1 : 0, side, back, at);
            }
            else Order(x, Aimed(target, x), order, path, speed, followFlag, f1, f2, reference is Entity rr ? Aimed(rr, x) : reference);
        }
    }

    /// <summary>
    /// The k-th copy's station in its original's frame (metres: right, forward): left and right of its track 8 behind it, then
    /// 16 behind; the follow order's offsets.
    /// </summary>
    public static (float Side, float Back) Station(int k) => ((k % 2 == 0 ? -6f : 6f), -8f * (1 + k / 2));

    /// <summary>A human the original was aimed at is the human nearest the copy (with its hysteresis); anyone else stays.</summary>
    private Entity? Aimed(Entity? target, Entity copy) => target is not null && IsHumanEntity(target) ? NearestHuman(copy) as Entity : target;

    /// <summary>The name of an original's k-th copy (from 1): the original's, then "+k" (the server's own tell; players are shown the original's, <see cref="OriginalName"/>).</summary>
    public static string ExtraName(string original, int k) => $"{original}+{k}";

    /// <summary>The name a car is shown by: a copy's is its original's (<see cref="ExtraName"/> without the "+k"), any other car's its own.</summary>
    public static string OriginalName(string name) => ExtraIndex(name) > 0 ? name[..name.LastIndexOf('+')] : name;

    /// <summary>The copy number in a car's name (<see cref="ExtraName"/>), 0 for a car that is no copy.</summary>
    public static int ExtraIndex(string name) =>
        name.LastIndexOf('+') is var i and > 0 && int.TryParse(name.AsSpan(i + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int k) && k > 0 ? k : 0;

    /// <summary>A copy of the original's car beside it, facing and moving as it does, its AI settings the original's (with coop_copy_key_cars in a paint and a name of its own).</summary>
    private void SpawnExtra(CoopGroup c, Entity e, int k)
    {
        var o = e.Vehicle!;
        if (c.BuildEnemy is null || o.Definition is not { Length: > 0 } def) return;
        var v = c.BuildEnemy(def);
        v.Paint = c.CopyKeyCars ? ExtraPaint.For(k, o.Paint, c.SchemePaint?.Invoke(def)) : o.Paint;
        v.Rot = o.Rot;
        v.Pos = SpawnSpace.Beside(World, o, v, c.Paths);
        v.Vel = o.Vel;
        v.Speed = o.Speed;
        v.Flags = (v.Flags & ~VehicleFlags.Lights) | (o.Flags & VehicleFlags.Lights);
        World.Vehicles.Add(v);
        var x = new Entity(c.CopyKeyCars ? ExtraName(e.Name, k) : e.Name, [], e.Type, e.ObjectFlags) { Vehicle = v, ExtraOf = e, Scaled = true };
        Add(x);
        SetSkill(x, e.SkillA, e.SkillB);
        x.Aggression = e.Aggression; x.Avoid = e.Avoid; x.MaxAttackers = e.MaxAttackers; x.Lights = e.Lights; x.Siren = e.Siren;
        Log($"co-op: {e.Name} has extra {k} beside it ({v.Paint?.Primary?.Hex ?? "factory"})");
        c.RaiseSpawned(v, def);
    }

    /// <summary>The script teleported an original: its living copies (or escorts) come along beside it.</summary>
    private void TeleportExtras(Entity e)
    {
        if (Coop is not { ExtraEnemies: > 0 } c || e.ExtraOf is not null || e.Vehicle is not { } o) return;
        foreach (var x in ExtrasOf(e).ToList())
            if (!x.Dead && x.Vehicle is { } v)
            {
                v.Rot = o.Rot;
                v.Pos = SpawnSpace.Beside(World, o, v, c.Paths);
                v.Vel = o.Vel; v.Speed = o.Speed; v.AngVel = System.Numerics.Vector3.Zero;
                v.Steer = v.Throttle = 0;
            }
    }

    /// <summary>
    /// Co-op Scaling's frame, after the script's: a living copy whose original is dead (or gone) attacks the human nearest it, as
    /// its original's orders no longer come; so does an escort whose car is. An escort whose car lives escorts it
    /// (<see cref="EscortFrame"/>).
    /// </summary>
    internal void ExtrasFrame()
    {
        if (Coop is not { ExtraEnemies: > 0 }) return;
        var slots = new Dictionary<Entity, int>();
        foreach (var x in Entities.Where(x => x.ExtraOf is not null && !x.Removed).ToList())
        {
            var o = x.ExtraOf!;
            // an escort's station is its number among its car's escorts, the dead ones too, so a death moves nobody
            int slot = slots.GetValueOrDefault(o);
            slots[o] = slot + 1;
            if (x.Dead) continue;
            if (o.Dead || o.Removed) { if (NearestHuman(x) is Entity h) Order(x, h, AiOrder.Attack, null, 0); }
            else if (_escorted.ContainsKey(o)) EscortFrame(x, o, slot);
        }
    }
}

/// <summary>
/// The paint of Co-op Scaling's copies: the same car as its original (the same file, build and paint scheme), resprayed
/// (<see cref="CarPaint"/>, the garage's RESPRAY) in a colour of its own, so that the copies are told from the original, which
/// keeps the mission's paint, and from each other. The k-th copy takes the k-th of the classic paints below that stands clear
/// of the original's paint: its respray's primary colour if it has one, else its scheme's primary paint (<see cref="ResprayPaint.Scheme"/>,
/// which the build reads from the car's body textures). A pure function of the copy's number and the original's car, decided on
/// the server, and sent to the clients with the car (<c>CarInfo.Paint</c>): every screen shows the same.
/// </summary>
public static class ExtraPaint
{
    /// <summary>The candidates in order (the garage's classic paints): strong hues first, then the neutrals.</summary>
    public static readonly (string Name, PaintColour Colour)[] Choices =
    [
        ("Grabber Blue", new(0x1E, 0x9B, 0xE0)), ("Hugger Orange", new(0xF0, 0x54, 0x1E)), ("Sublime", new(0x8D, 0xD0, 0x2A)),
        ("Plum Crazy", new(0x5B, 0x1E, 0x8C)), ("Lemon Twist", new(0xF5, 0xD6, 0x0A)), ("Panther Pink", new(0xE8, 0x62, 0x8F)),
        ("Turquoise", new(0x2A, 0xA6, 0xA0)), ("Rallye Red", new(0xC4, 0x1E, 0x1E)), ("White", new(0xF2, 0xF2, 0xEE)), ("Black", new(0x0E, 0x0E, 0x0F)),
    ];

    /// <summary>The least hue gap (degrees) between a copy's paint and a coloured original's.</summary>
    public const float MinHueGap = 50f;

    /// <summary>The k-th copy's paint (k from 1): the k-th candidate clear of the original's colour, gloss.</summary>
    /// <param name="original">The original's respray, if it has one.</param>
    /// <param name="scheme">The original's paint scheme's primary paint (its factory colour), if known.</param>
    public static CarPaint For(int k, CarPaint? original, ResprayPaint.PaintRef? scheme)
    {
        ResprayPaint.Hsv? against = original?.Primary is { } p ? ResprayPaint.ToHsv(p.R / 255f, p.G / 255f, p.B / 255f)
            : scheme is { } s ? new ResprayPaint.Hsv(s.H, s.Chromatic ? System.Math.Max(s.S, 0.3f) : System.Math.Min(s.S, 0.1f), s.V) : null;
        var clear = Choices.Where(c => against is not { } a || Clear(c.Colour, a)).ToList();
        if (clear.Count == 0) clear = [.. Choices];
        return new CarPaint(clear[(System.Math.Max(1, k) - 1) % clear.Count].Colour);
    }

    /// <summary>Whether a candidate reads apart from the original's colour: hues at least <see cref="MinHueGap"/> apart, a hue against a grey, or greys of other brightness.</summary>
    public static bool Clear(PaintColour c, ResprayPaint.Hsv against)
    {
        var h = ResprayPaint.ToHsv(c.R / 255f, c.G / 255f, c.B / 255f);
        bool hue = h.S > 0.25f && h.V > 0.15f, hueA = against.S > 0.25f && against.V > 0.15f;
        if (hue && hueA) return ResprayPaint.HueGap(h.H, against.H) >= MinHueGap;
        if (hue != hueA) return true;
        return System.MathF.Abs(h.V - against.V) >= 0.4f;
    }

    /// <summary>
    /// The primary paint of a car file's paint scheme, read from its body textures as the respray reads them (<see cref="I76.Formats.Assembly.PaintMasks"/>);
    /// null when the car does not assemble or has no body texture.
    /// </summary>
    public static ResprayPaint.PaintRef? SchemeOf(I76.Formats.Vfs.AssetVfs vfs, string vcf)
    {
        try
        {
            if (new I76.Formats.Assembly.ModelAssembler(vfs).Assemble(vcf) is not { } model) return null;
            var res = new I76.Formats.Assembly.TextureResolver(vfs);
            var masks = I76.Formats.Assembly.PaintMasks.Compute(model, res,
                m => res.Load(m) is { } t ? (t.Width, t.Height, TextureReader.ToRgba(t)) : null);
            return masks.Primary;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { return null; }
    }
}
