namespace I76.Net;

/// <summary>A part of a co-op roguetrip's shared field salvage: its id in the draft, its part file stem, its health, and who took it (empty: still in the field).</summary>
public sealed record DraftItem(int Id, string Stem, int Health, string Taker = "");

/// <summary>A player of the draft: their name, the turns they have had (picks and passes), whether they are done picking, and done in the garage.</summary>
public sealed record DraftSeat(string Name, int Turns, bool Done, bool GarageReady);

/// <summary>
/// A co-op roguetrip's salvage after a won job (docs/remix-missions.md "Roguetrip in co-op", "Salvage"): one shared pool from the job's wrecks,
/// picked in turns round the crew (a snake draft by fewest turns had, the host first on a tie), a pick timer that passes for a player who
/// lets it run out, READY ending a player's picking; then each player's garage on their own machine, with a ready mark and an overall time
/// limit. Pure: the server runs it and tells the crew (<see cref="RoguetripSalvage"/>).
/// </summary>
public sealed class RoguetripDraft
{
    /// <summary>Seconds a player has for a pick before it passes; seconds the garages have between them.</summary>
    public const double PickSeconds = 20, GarageSeconds = 150;

    public List<DraftItem> Items { get; } = [];
    /// <summary>The crew in their order (the host first, then as they came).</summary>
    public List<DraftSeat> Crew { get; } = [];
    /// <summary>Whose pick it is (null: the draft is over).</summary>
    public string? Turn { get; private set; }
    public double TurnLeft { get; private set; }
    /// <summary>The garage step (after the draft): the seconds it has left.</summary>
    public bool Garage { get; private set; }
    public double GarageLeft { get; private set; }

    public RoguetripDraft(IEnumerable<(string Stem, int Health)> items, IEnumerable<string> crew)
    {
        int id = 0;
        foreach (var (s, h) in items) Items.Add(new DraftItem(++id, s, h));
        foreach (var n in crew.Distinct(StringComparer.Ordinal)) Crew.Add(new DraftSeat(n, 0, false, false));
        Next();
    }

    public bool DraftOver => Turn is null;
    public bool Over => Garage && (GarageLeft <= 0 || Crew.All(c => c.GarageReady));
    public IEnumerable<DraftItem> Left => Items.Where(i => i.Taker.Length == 0);

    private int Index(string who) => Crew.FindIndex(c => c.Name == who);

    /// <summary>The next picker: of those not done, the fewest turns had, the first in the crew's order on a tie; none when nobody is or the field is empty.</summary>
    private void Next()
    {
        var c = Left.Any() ? Crew.Where(x => !x.Done).OrderBy(x => x.Turns).ThenBy(x => Crew.IndexOf(x)).FirstOrDefault() : null;
        Turn = c?.Name;
        TurnLeft = PickSeconds;
        if (Turn is null && !Garage) { Garage = true; GarageLeft = GarageSeconds; }
    }

    private void Turned(string who)
    {
        int k = Index(who);
        Crew[k] = Crew[k] with { Turns = Crew[k].Turns + 1 };
        Next();
    }

    /// <summary>A player takes item <paramref name="id"/>: on their turn, while it is in the field. Null when taken, else why not.</summary>
    public string? Pick(string who, int id)
    {
        if (Turn != who) return Turn is null ? "the salvage is over" : $"it is {Turn}'s pick";
        int i = Items.FindIndex(x => x.Id == id);
        if (i < 0 || Items[i].Taker.Length > 0) return "that part is gone";
        Items[i] = Items[i] with { Taker = who };
        Turned(who);
        return null;
    }

    /// <summary>A player lets their turn go (or its time runs out).</summary>
    public void Pass(string who) { if (Turn == who) Turned(who); }

    /// <summary>A player is done picking (READY); out of the draft for good.</summary>
    public void Done(string who)
    {
        int k = Index(who);
        if (k < 0 || Crew[k].Done) return;
        Crew[k] = Crew[k] with { Done = true };
        if (Turn == who || Turn is null) Next(); else if (Left.Any() && Crew.All(c => c.Done)) Next();
    }

    /// <summary>A player's garage is done (READY in the garage).</summary>
    public void GarageDone(string who)
    {
        int k = Index(who);
        if (k >= 0) Crew[k] = Crew[k] with { GarageReady = true };
    }

    /// <summary>A player left the game: out of the draft and counted ready in the garage.</summary>
    public void Leave(string who)
    {
        int k = Index(who);
        if (k < 0) return;
        Crew[k] = Crew[k] with { Done = true, GarageReady = true };
        if (Turn == who) Next();
    }

    /// <summary>The clocks: a pick's time running out passes it; the garage's ends it.</summary>
    public void Tick(double dt)
    {
        if (Turn is { } who) { TurnLeft -= dt; if (TurnLeft <= 0) Pass(who); }
        else if (Garage) GarageLeft = Math.Max(0, GarageLeft - dt);
    }

    /// <summary>What a player has taken.</summary>
    public IEnumerable<DraftItem> TakenBy(string who) => Items.Where(i => i.Taker == who);

    /// <summary>The parts nobody took, shared out for plate: each player's share (rounded down, at least none).</summary>
    public int ScrapShare => Crew.Count == 0 ? 0 : Left.Count() / Crew.Count;
}

/// <summary>
/// The draft as the server tells a player (the I26 play fork's <see cref="MsgType.RoguetripSalvage"/>, behind <see cref="PeerFeatures.Roguetrip"/>):
/// the field, the crew, whose pick and how long, the garage's clock, and this player's own garage (their inventory as the host keeps it).
/// </summary>
public sealed record RoguetripSalvage(bool Garage, IReadOnlyList<DraftItem> Items, IReadOnlyList<DraftSeat> Crew, string Turn, float TurnLeft, float GarageLeft, string Mine)
{
    public const int MaxItems = 400;

    public static RoguetripSalvage Of(RoguetripDraft d, string mine) =>
        new(d.Garage, [.. d.Items], [.. d.Crew], d.Turn ?? "", (float)d.TurnLeft, (float)d.GarageLeft, mine);

    public void Write(NetWriter w)
    {
        w.U8((byte)MsgType.RoguetripSalvage);
        w.Bool(Garage);
        w.VarU((uint)Math.Min(Items.Count, MaxItems));
        foreach (var i in Items.Take(MaxItems)) { w.VarU((uint)i.Id); w.Str(i.Stem); w.VarI(i.Health); w.Str(i.Taker); }
        w.U8((byte)Math.Min(Crew.Count, Protocol.MaxCoopSeats * 2));
        foreach (var c in Crew.Take(Protocol.MaxCoopSeats * 2)) { w.Str(c.Name); w.VarU((uint)c.Turns); w.Bool(c.Done); w.Bool(c.GarageReady); }
        w.Str(Turn);
        w.F32(TurnLeft);
        w.F32(GarageLeft);
        w.BlobU16(System.Text.Encoding.UTF8.GetBytes(Mine));
    }

    public static RoguetripSalvage Read(ref NetReader r)
    {
        bool garage = r.Bool();
        uint n = r.VarU();
        if (n > MaxItems) throw new WireException($"salvage of {n}");
        var items = new List<DraftItem>((int)n);
        for (int i = 0; i < n; i++) items.Add(new DraftItem((int)r.VarU(), r.Str(), r.VarI(), r.Str()));
        int m = r.U8();
        if (m > Protocol.MaxCoopSeats * 2) throw new WireException($"crew of {m}");
        var crew = new List<DraftSeat>(m);
        for (int i = 0; i < m; i++) crew.Add(new DraftSeat(r.Str(), (int)r.VarU(), r.Bool(), r.Bool()));
        string turn = r.Str();
        float tl = r.F32(), gl = r.F32();
        string mine = System.Text.Encoding.UTF8.GetString(r.BlobU16());
        return new RoguetripSalvage(garage, items, crew, turn, tl, gl, mine);
    }
}

/// <summary>A player's salvage move (<see cref="MsgType.RoguetripPick"/>): take an item, pass the turn, or done picking.</summary>
public enum PickKind : byte { Take = 0, Pass = 1, Done = 2 }

/// <summary>The messages a co-op roguetrip's player sends the server between jobs.</summary>
public static class RoguetripMoves
{
    public static void Pick(NetWriter w, PickKind kind, int id)
    {
        w.U8((byte)MsgType.RoguetripPick);
        w.U8((byte)kind);
        w.VarU((uint)Math.Max(0, id));
    }

    public static (PickKind Kind, int Id) ReadPick(ref NetReader r)
    {
        byte k = r.U8();
        if (k > (byte)PickKind.Done) throw new WireException($"pick {k}");
        return ((PickKind)k, (int)r.VarU());
    }

    /// <summary>The garage's DONE: the player's inventory as they left it (TripInventory.Save).</summary>
    public static void Garage(NetWriter w, string inventory)
    {
        w.U8((byte)MsgType.RoguetripGarage);
        w.BlobU16(System.Text.Encoding.UTF8.GetBytes(inventory));
    }

    public static string ReadGarage(ref NetReader r) => System.Text.Encoding.UTF8.GetString(r.BlobU16());
}
