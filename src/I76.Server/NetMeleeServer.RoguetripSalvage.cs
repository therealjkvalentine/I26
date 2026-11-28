using I76.Formats.Remix;
using I76.Net;
using I76.Sim.Trip;
using I76.Sim.Vehicles;
using LiteNetLib;

namespace I76.Server;

/// <summary>
/// A co-op roguetrip's salvage and garages between jobs (docs/remix-missions.md "Roguetrip in co-op", "Salvage"). The server keeps each
/// player's garage (the trip's inventory, by name, in the host's run) and the job's wrecks; after a won job's debrief the crew drafts the
/// shared field (<see cref="RoguetripDraft"/>: turns round the crew, a pick timer, READY), each pick going straight into the picker's
/// garage; then each player fits and repairs their own car on their own machine and sends it back; what nobody took is scrapped for
/// plate, shared out. The next job seats each player in their garage's car, its parts as the garage left them, its armour carried.
/// </summary>
public sealed partial class NetMeleeServer
{
    private SalvagePool? _rtPool;
    private List<(string Stem, int Health)>? _rtField;
    private RoguetripDraft? _rtDraft;
    private PartsDatabase? _rtPartsDb;
    private double _rtDraftAt, _rtToldAt;
    private string _rtToldKey = "";
    /// <summary>The players this job seated in their garage's car (one who chose another car in the lobby is not: their garage starts again).</summary>
    private readonly HashSet<string> _rtGarageSeats = [];

    private PartsDatabase RtParts => _rtPartsDb ??= PartsDatabase.Build(Host.Vfs);

    /// <summary>The draft running now (tests), or null.</summary>
    public RoguetripDraft? Draft { get { lock (_lock) return _rtDraft; } }

    /// <summary>A job's wrecks feed its salvage pool (the roguetrip's: no map cap), the crew's own cars left out.</summary>
    private void RoguetripWatchWrecks(MeleeHost built, string map)
    {
        var pool = _rtPool = SalvagePool.Roguetrip(RemixRun.SalvageStem(map));
        var world = built.Setup.World;
        built.Setup.Damage.Salvageable += v => { if (!world.IsHuman(v)) pool.AddDeadCar(v); };
    }

    /// <summary>A player's garage as the host keeps it, or one made from the car they drive (a player new to it).</summary>
    private TripInventory? InventoryOf(CrewCar c, string carFile)
    {
        try
        {
            if (c.Inventory is { Length: > 0 } json && TripInventory.Load(json, RtParts, Host.Vfs) is { } kept) return kept;
            if (Host.Vfs.TryRead(carFile) is not { } vcf) return null;
            var vfs = Host.Vfs;
            return TripInventory.FromCar(TripCar.FromVcf(vcf, n => VehicleFactory.EngineCompId(vfs, n)), RtParts, vfs);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException or ArgumentException or FormatException)
        {
            Log?.Invoke($"roguetrip: {c.Name}'s garage not read ({ex.Message})");
            return null;
        }
    }

    /// <summary>
    /// The job's end for the garages (after RoguetripEnded): each player's parts as the job left them (a car killed keeps its garage as it was:
    /// it comes back at the floor), the last repair order done, the field left; a won job's field made ready for the draft.
    /// </summary>
    private void RoguetripGaragesAfter(IReadOnlyList<Seat> seats, bool won, CoopJob? job)
    {
        if (Roguetrip is not { } run) return;
        foreach (var s in seats)
        {
            if (s.Actor is not CoopActor a || run.Of(s.Name) is not { } c) continue;
            // the car this seat asked for (an install car; its garage's configuration rides on it); a seat not in its garage's car (another car chosen) starts a new garage
            string asked = s.Connection is NetPeer p && _joined.TryGetValue(p.Id, out var j) && j.Car != Protocol.MissionCar ? j.Car : a.CarFile;
            if (c.Inventory is not null && !_rtGarageSeats.Contains(s.Name)) c.Inventory = null;
            c.Model = asked;
            if (InventoryOf(c, a.CarFile) is not { } inv) continue;
            if (!a.Destroyed) inv.ApplyState(I76.Sim.Trip.CarState.Capture(a.Car));
            inv.FinishRepairs();
            inv.LeaveField();
            c.Inventory = inv.Save();
        }
        _rtField = null;
        _rtGarageSeats.Clear();
        if (!won || _rtPool is not { } pool) return;
        try
        {
            var rng = new I76.Sim.Rng.CrtRand(unchecked((uint)(run.Run.Number * 31 + run.Run.Legs.Count)));
            var items = pool.Finish(n => n.EndsWith(".fsi", StringComparison.OrdinalIgnoreCase) ? null : Host.Vfs.TryRead(n), rng.Next) ?? [];
            if (job?.Spec().Prize is { } prize) items.AddRange(prize.Select(k => new SalvageItem(k, SalvageItem.Prize, true)));
            // each item's condition rolled once, here: every player sees the same part at the same health
            var roll = new TripInventory();
            roll.AddSalvage(items, RtParts, rng.Next);
            _rtField = [.. roll.Nodes.Select(n => (n.Part.Stem, n.Health))];
            Log?.Invoke($"roguetrip: the field holds {_rtField.Count} parts from the job's wrecks");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException) { Log?.Invoke($"roguetrip: no salvage ({ex.Message})"); }
        _rtPool = null;
    }

    /// <summary>Past a won job's debrief: the draft begins with the players in the lobby (the host first), or the garages at once with an empty field.</summary>
    private void RoguetripSalvageStart()
    {
        if (Roguetrip is not { } run || run.Run.Phase != "salvage" || _rtDraft is not null) return;
        var names = _lobby.OrderBy(m => m.Peer.Id == _hostPeer ? 0 : 1).Select(m => m.Request.Name).ToList();
        _rtDraft = new RoguetripDraft(_rtField ?? [], names);
        _rtDraftAt = Now;
        Log?.Invoke($"roguetrip: the salvage draft, {_rtDraft.Items.Count} parts, {string.Join(", ", names)}; {(_rtDraft.Turn is { } t ? $"{t} picks first" : "straight to the garages")}");
        TellSalvage();
    }

    /// <summary>The draft to everyone in the lobby, each with their own garage.</summary>
    private void TellSalvage()
    {
        if (_rtDraft is not { } d || Roguetrip is not { } run) return;
        foreach (var m in _lobby)
        {
            if ((m.Request.Features & PeerFeatures.Roguetrip) == 0) continue;
            string mine = run.Of(m.Request.Name)?.Inventory ?? "";
            _msg.Clear();
            RoguetripSalvage.Of(d, mine).Write(_msg);
            m.Peer.Send(_msg.Span, DeliveryMethod.ReliableOrdered);
        }
        _rtToldAt = Now;
        _rtToldKey = Key(d);
    }

    private static string Key(RoguetripDraft d) => $"{d.Turn}|{d.Garage}|{string.Join(",", d.Items.Select(i => i.Taker))}|{string.Join(",", d.Crew.Select(c => $"{c.Turns}{c.Done}{c.GarageReady}"))}";

    /// <summary>Each tick: the draft's clocks (a pick passes when its time runs out; the garages end with theirs), told on a change and once a second.</summary>
    private void RoguetripSalvageTick()
    {
        if (_rtDraft is not { } d) return;
        double now = Now;
        d.Tick(now - _rtDraftAt);
        _rtDraftAt = now;
        if (d.Over) { RoguetripSalvageEnd(); return; }
        if (Key(d) != _rtToldKey || now - _rtToldAt >= 1) TellSalvage();
    }

    /// <summary>The draft and garages over: the parts nobody took scrapped for plate (a share each), the next choice.</summary>
    private void RoguetripSalvageEnd()
    {
        if (_rtDraft is not { } d || Roguetrip is not { } run) return;
        int share = d.ScrapShare;
        foreach (var c in run.Crew.Where(c => !c.Out && d.Crew.Any(x => x.Name == c.Name))) c.State = CoopRun.Plate(c.State, share);
        Log?.Invoke($"roguetrip: salvage done: {string.Join(", ", d.Crew.Select(c => $"{c.Name} took {d.TakenBy(c.Name).Count()}"))}; {d.Left.Count()} left, {share} each scrapped for plate");
        _rtDraft = null;
        _rtField = null;
        run.SalvageDone();
        foreach (var m in _lobby) RoguetripLobbyCar(m);
        _rtOffers = null;
        TellRoguetrip();
    }


    /// <summary>A lobby player's entry in the run's crew (one who came after the last job: made, in the car they asked for).</summary>
    private CrewCar? CrewOf(LobbyPeer m)
    {
        if (Roguetrip is not { } run) return null;
        if (run.Of(m.Request.Name) is { } c) return c;
        string car = m.Request.Car == Protocol.MissionCar ? (Host.Mode as CoopMode)?.OwnCar ?? "" : m.Request.Car;
        c = new CrewCar { Name = m.Request.Name, Car = car };
        run.Crew.Add(c);
        return c;
    }
    /// <summary>A lobby player's salvage move or garage (only theirs; refused quietly when it is not their pick).</summary>
    private void ReceiveRoguetripMove(LobbyPeer m, MsgType type, ref NetReader r)
    {
        if (_rtDraft is not { } d || Roguetrip is not { } run) return;
        string who = m.Request.Name;
        if (type == MsgType.RoguetripPick)
        {
            var (kind, id) = RoguetripMoves.ReadPick(ref r);
            switch (kind)
            {
                case PickKind.Take:
                    var item = d.Items.FirstOrDefault(i => i.Id == id);
                    if (d.Pick(who, id) is { } no) { Log?.Invoke($"roguetrip: {who}'s pick refused: {no}"); break; }
                    if (item is not null && CrewOf(m) is { } c && RtParts.ByStem(item.Stem) is { } part)
                    {
                        var inv = InventoryOf(c, c.Car);
                        if (inv is not null) { inv.Nodes.Add(new PartNode(part, item.Health, PartState.Van)); c.Inventory = inv.Save(); }
                    }
                    Log?.Invoke($"roguetrip: {who} takes {item?.Stem} ({item?.Health})");
                    break;
                case PickKind.Pass: d.Pass(who); break;
                case PickKind.Done: d.Done(who); Log?.Invoke($"roguetrip: {who} is done picking"); break;
            }
        }
        else if (type == MsgType.RoguetripGarage)
        {
            string json = RoguetripMoves.ReadGarage(ref r);
            if (CrewOf(m) is not { } c) return;
            TripInventory? inv = null;
            try { inv = TripInventory.Load(json, RtParts, Host.Vfs); }
            catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or System.Text.Json.JsonException or ArgumentException) { Log?.Invoke($"roguetrip: {who}'s garage refused ({ex.Message})"); }
            if (inv is null) return;
            if (inv.Validate() is { } bad) { Log?.Invoke($"roguetrip: {who}'s garage car refused (notice {bad})"); return; }
            c.Inventory = json;
            RoguetripLobbyCar(m);
            d.GarageDone(who);
            Log?.Invoke($"roguetrip: {who}'s garage is done");
        }
        if (d.Over) RoguetripSalvageEnd(); else TellSalvage();
        RoguetripChanged?.Invoke(run);
    }

    /// <summary>
    /// A player's car for the next job is their garage's (a resumed run's, and after a garage): the inventory's car, checked as a join's
    /// configuration, set as their lobby car (asked under its model's name; served by its content).
    /// </summary>
    private void RoguetripLobbyCar(LobbyPeer m)
    {
        if (Roguetrip is not { } run || run.Of(m.Request.Name) is not { Inventory.Length: > 0 } c || InventoryOf(c, c.Car) is not { } inv) return;
        var car = inv.ToCar();
        byte[] vcf = car.ToVcf();
        if (c.Model is not { Length: > 0 } asked) return;
        if (Host.CheckJoin(asked, vcf) is { } why) { Log?.Invoke($"roguetrip: {m.Request.Name}'s garage car {asked} not taken: {why}"); return; }
        m.Request = m.Request with { Car = asked, Config = vcf };
        Log?.Invoke($"roguetrip: {m.Request.Name} drives their garage's car next ({asked})");
    }

    /// <summary>
    /// The next job's seat in a garage's car: its parts as the garage left them (the inventory's state), then the armour and chassis carried.
    /// True when the garage set it (the plain carry is then not applied).
    /// </summary>
    private bool RoguetripGarageSeat(Seat s, CoopActor a)
    {
        if (Roguetrip is not { } run || run.Of(s.Name) is not { Inventory.Length: > 0 } c) return false;
        if (s.Connection is not NetPeer p || !_joined.TryGetValue(p.Id, out var j) || c.Model is null || !j.Car.Equals(c.Model, StringComparison.OrdinalIgnoreCase) || j.Config is not { Length: > 0 }) return false;
        if (InventoryOf(c, a.CarFile) is not { } inv) return false;
        inv.State().Apply(a.Car);
        _rtGarageSeats.Add(s.Name);
        if (c.State is { } carry) RunCarry.Apply(new RunCar { Armour = carry.Armour, Chassis = carry.Chassis }, a.Car);
        Log?.Invoke($"roguetrip: {s.Name} drives their garage's car ({a.CarFile}), armour {c.State?.ArmourLeft ?? 100}%");
        return true;
    }
}
