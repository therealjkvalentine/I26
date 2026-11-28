using I76.Net;
using I76.Sim.Trip;

namespace I76.Game.Shell;

/// <summary>
/// A co-op roguetrip's salvage and garage on one player's machine (docs/remix-missions.md "Roguetrip in co-op", "Salvage"): the host's draft
/// as it last said it, this player's garage (the host's record of it) with the field's parts still there laid in as field salvage, the
/// player's moves to the host, and the way back to the lobby.
/// </summary>
public sealed class CoopSalvage
{
    public required Func<RoguetripSalvage?> State { get; init; }
    public required Func<string> Me { get; init; }
    /// <summary>This player's car file (a garage made from it when the host has none yet).</summary>
    public required Func<string> Car { get; init; }
    public required Action<PickKind, int> Send { get; init; }
    public required Action<string> SendGarage { get; init; }
    /// <summary>Back to the lobby (set by the lobby that opened the screens).</summary>
    public Action Close { get; set; } = () => { };

    private RoguetripSalvage? _seen;
    private readonly Dictionary<PartNode, int> _ids = [];
    private double _autoAt = -1;

    /// <summary>The host's word changed since the screen last looked.</summary>
    public bool Changed()
    {
        var s = State();
        if (ReferenceEquals(s, _seen)) return false;
        _seen = s;
        return true;
    }

    /// <summary>The salvage step is over (or gone): the screens close.</summary>
    public bool Over() => State() is null;

    public bool MyTurn => State() is { } s && s.Turn == Me() && !s.Garage;

    /// <summary>This player's garage with the field laid in: the host's record (or the car's own), the parts nobody has taken as field salvage.</summary>
    public TripInventory Inventory(Shell shell)
    {
        var s = State();
        TripInventory? inv = null;
        try
        {
            if (s?.Mine is { Length: > 0 } json) inv = TripInventory.Load(json, shell.Parts, shell.Vfs);
            if (inv is null && shell.Vfs.TryRead(Car()) is { } vcf)
                inv = TripInventory.FromCar(TripCar.FromVcf(vcf, n => I76.Sim.Vehicles.VehicleFactory.EngineCompId(shell.Vfs, n)), shell.Parts, shell.Vfs);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException or System.Text.Json.JsonException) { Godot.GD.PrintErr($"roguetrip salvage: {ex.Message}"); }
        inv ??= new TripInventory();
        _ids.Clear();
        if (s is not null && !s.Garage)
            foreach (var it in s.Items.Where(i => i.Taker.Length == 0))
                if (shell.Parts.ByStem(it.Stem) is { } part)
                {
                    var n = new PartNode(part, it.Health, PartState.Field);
                    inv.Nodes.Add(n);
                    _ids[n] = it.Id;
                }
        return inv;
    }

    public int? IdOf(PartNode n) => _ids.TryGetValue(n, out int id) ? id : null;

    private double _garageAt = -1;

    /// <summary>
    /// I76_ROGUE_GARAGE=S (a capture hook): S seconds into this player's garage, fit the van's engine, suspension or brakes that beats what
    /// the car has (the garage's own Install), then DONE. True when it is time.
    /// </summary>
    public bool AutoGarage(TripInventory inv)
    {
        if (!double.TryParse(System.Environment.GetEnvironmentVariable("I76_ROGUE_GARAGE"), System.Globalization.CultureInfo.InvariantCulture, out double secs)) return false;
        double now = Godot.Time.GetTicksMsec() / 1000.0;
        if (_garageAt < 0) _garageAt = now;
        if (now - _garageAt < secs) return false;
        _garageAt = -1;
        foreach (var n in inv.Nodes.Where(n => n.State == PartState.Van && n.Part.Type is 2 or 3 or 4).ToList())
        {
            int slot = inv.Slots.FindIndex(s => s.Type == n.Part.Type);
            if (slot < 0) continue;
            int have = inv.InSlot(slot) is { } f ? I76.Formats.Remix.PartTiers.Level(f.Part.Stem) : -1;
            if (I76.Formats.Remix.PartTiers.Level(n.Part.Stem) <= have) continue;
            inv.Install(slot, n);
            if (inv.Validate() is not null) { Godot.GD.Print($"roguetrip: I76_ROGUE_GARAGE: {n.Part.Stem} does not fit"); continue; }
            Godot.GD.Print($"roguetrip: I76_ROGUE_GARAGE: fitted {n.Part.Stem} in slot {slot}");
        }
        return true;
    }

    /// <summary>The status: whose pick and the seconds (the salvage screen shows the first line), or the garages' clock and who is still fitting.</summary>
    public (string, string) Status()
    {
        if (State() is not { } s) return ("", "");
        if (s.Garage) return ($"GARAGES: {(int)Math.Ceiling(s.GarageLeft)} S", string.Join(", ", s.Crew.Where(c => !c.GarageReady).Select(c => c.Name)));
        string me = Me();
        return s.Turn == me ? ($"YOUR PICK: {(int)Math.Ceiling(s.TurnLeft)} S", "click a part; ACCEPT SALVAGE: done")
            : s.Turn.Length > 0 ? ($"{s.Turn.ToUpperInvariant()} PICKS: {(int)Math.Ceiling(s.TurnLeft)} S", "taken parts leave the field") : ("THE FIELD IS DONE", "");
    }

    /// <summary>
    /// I76_ROGUE_SALVAGE=K (a capture hook, docs/dev-hooks.md): on this player's pick, two seconds in, take the K-th part still in the field
    /// (best first by its level), or say done when there is none.
    /// </summary>
    public bool AutoPick()
    {
        if (System.Environment.GetEnvironmentVariable("I76_ROGUE_SALVAGE") is not { } k || !int.TryParse(k, out int nth) || !MyTurn || State() is not { } s) { _autoAt = -1; return false; }
        double now = Godot.Time.GetTicksMsec() / 1000.0;
        if (_autoAt < 0) _autoAt = now;
        if (now - _autoAt < 2) return false;
        _autoAt = -1;
        var left = s.Items.Where(i => i.Taker.Length == 0).OrderByDescending(i => I76.Formats.Remix.PartTiers.Level(i.Stem)).ThenBy(i => i.Id).ToList();
        if (left.Count == 0) { Send(PickKind.Done, 0); return true; }
        var pick = left[Math.Min(nth, left.Count - 1)];
        Godot.GD.Print($"roguetrip: I76_ROGUE_SALVAGE: take {pick.Stem} ({pick.Health})");
        Send(PickKind.Take, pick.Id);
        return true;
    }
}

public sealed partial class FieldSalvageScreen
{
    private int _coopFrames;
    // the free paper left of the FIELD SALVAGE title, over the engines list
    private const int StatusX = 12, StatusY = 258;

    /// <summary>The co-op screen's frame (true: taken): the host's word redraws it; the capture hook picks.</summary>
    private bool CoopTick()
    {
        if (Coop is not { } co) return false;
        if (co.Over()) { co.Close(); return true; }
        if (co.State() is { Garage: true }) { CoopDone(); return true; }   // the field is done: on to the garage
        if (co.Changed())
        {
            Inv = co.Inventory(Shell);
            foreach (var l in _lists) l.Clear();
            Build();
            Repaint();
        }
        else if (++_coopFrames % 15 == 0) { Frame.Restore(StatusX - 2, StatusY - 1, 200, 18); CoopStatus(); }
        co.AutoPick();
        if (System.Environment.GetEnvironmentVariable("I76_ROGUE_SALVAGE") is { } && co.State() is { } s && s.Crew.FirstOrDefault(c => c.Name == co.Me()) is { Done: false } && !s.Items.Any(i => i.Taker.Length == 0)) AcceptSalvage();
        return false;
    }

    /// <summary>A click on a row in a co-op draft: a field part is asked of the host on this player's pick; the car's own parts stay (no scrapping in co-op).</summary>
    private bool CoopMove(int from, int k)
    {
        if (Coop is not { } co) return false;
        var n = _lists[from][k];
        if (n.State != PartState.Field) return true;
        if (!co.MyTurn) { Shell.Sound(82); return true; }
        if (co.IdOf(n) is { } id) { Shell.Sound(82); co.Send(PickKind.Take, id); }
        return true;
    }

    /// <summary>ACCEPT SALVAGE in a co-op draft: done picking; on to this player's garage.</summary>
    private void CoopDone()
    {
        if (Coop is not { } co) return;
        if (co.State() is { } s && s.Crew.FirstOrDefault(c => c.Name == co.Me()) is { Done: false }) co.Send(PickKind.Done, 0);
        // the garage works on this player's garage without the field (what nobody took is the host's to scrap)
        Inv.LeaveField();
        Shell.Go(new GarageScreen
        {
            Own = Inv, Coop = co,
            Finished = (inv, _) => { co.SendGarage(inv.Save()); co.Close(); },
        });
    }

    private void CoopStatus()
    {
        if (Coop is not { } co) return;
        var (line, _) = co.Status();
        Text(F45, StatusX, StatusY, line);   // the salvage shows the first line; the garage both
    }
}

public sealed partial class GarageScreen
{
    private int _clockFrames;

    /// <summary>A co-op garage: the garages' clock and who is still fitting, on the paper under the legends, every 15 frames.</summary>
    private void CoopClock()
    {
        if (Coop is not { } co || ++_clockFrames % 15 != 1) return;
        var (a, b) = co.Status();
        Frame.Restore(460, 334, 170, 24);
        Text(F45, 462, 335, a);
        Text(F45, 462, 346, b);
    }
}
