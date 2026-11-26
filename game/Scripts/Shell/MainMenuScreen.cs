using Godot;
using I76.Formats.Shell;

namespace I76.Game.Shell;

/// <summary>
/// The main menu, 0xc00e: record 26, the buttons
/// and banners of bank 0x3e. TRIP and MELEE open rows under them, which
/// open rows of their own; hovering lights a button and shows its banner;
/// clicking empty space closes every row. Record 81 is the click.
/// </summary>
public sealed class MainMenuScreen : ShellScreen
{
    private SpriteBank _bank = null!;
    private readonly List<SpriteWidget> _shown = [];
    private SpriteWidget? _hot;
    private readonly Dictionary<int, SpriteWidget> _w = [];
    /// <summary>HOST (true) or JOIN opened the connection row.</summary>
    private bool _host;

    // id, x, y, dim sprite, banner sprite (−1 none)
    private static readonly (int Id, int X, int Y, int Sprite, int Banner)[] Table =
    [
        (0, 1, 17, 1, 42), (17, 566, 17, 31, 43), (13, 167, 286, 25, 34), (2, 387, 286, 5, 37),
        (12, 74, 340, 33, 44), (11, 150, 340, 23, 35), (10, 216, 340, 21, 36),
        (15, 345, 340, 29, 41), (1, 445, 340, 3, 38),
        (3, 343, 368, 7, -1), (5, 393, 368, 11, -1),
        (9, 399, 368, 19, 40), (4, 478, 368, 9, 39),
        (7, 324, 395, 15, -1), (14, 373, 395, 27, -1),
    ];
    private static readonly int[] Top = [0, 17, 13, 2];
    private static readonly int[] NitroTop = [0, 0x12, 2, 1];
    // the connection row keeps only what the port has (docs/deviations.md): IPX is the LAN (found by broadcast), INTERNET a
    // game at an address (the address book, internet.lst). MODEM (6), NULLMODEM (8) and OTHER (16) are not there; the two
    // left keep their places, which already sit under HOST and JOIN
    private static readonly int[] MultiRow = [3, 5], NetRow = [7, 14];
    private int[] TripRow => Nitro ? [3, 4, 5, Progress.AllDoneExceptLast ? 7 : 6, 8] : [12, 11, 10];
    private int[] MeleeRow => Nitro ? [9, 12] : [15, 1];
    private int[] AutoRow => Nitro ? [] : [9, 4];

    // The Nitro Pack's main menu (the original front end's widget builder): bank 56, the widget built
    // with the even (dim) sprite and lit by the next. SCENARIOS opens the characters (3 Taurus, 4 Jade, 5 Skeeter, 6 "?" or 7 Natty
    // Dread by the unlock) and TRAINING (8); MELEE opens MULTI MELEE (9) and INSTANT MELEE (0xc). The banners are 275 or 400 wide, at y 452.
    private const int NitroBank = 56;
    private bool Nitro => Shell.Install.IsNitro;
    private NitroProgress Progress => Shell.NitroProgress;
    private static readonly (int Id, int X, int Y, int Sprite, int Banner)[] NitroTable =
    [
        (0, 1, 18, 0, 42), (0x12, 566, 16, 20, 43), (2, 97, 286, 28, 22), (1, 409, 286, 2, 47),
        (3, 58, 340, 30, 23), (4, 126, 340, 32, 24), (5, 176, 340, 34, 25), (6, 245, 340, 36, 26), (7, 245, 340, 38, 27), (8, 161, 368, 40, 44),
        (9, 367, 340, 18, 49), (0xc, 471, 340, 6, 48),
        // MULTI MELEE's HOST / JOIN, and the connections under them: the port keeps IPX (the LAN) and INTERNET
        (10, 365, 368, 4, -1), (11, 415, 368, 8, -1), (0xe, 345, 395, 12, -1), (0xf, 396, 395, 16, -1),
    ];
    // The port's own entries (docs/deviations.md "Nitro Pack under AUTO MELEE"): with a Nitro Pack install beside the base one, NITRO in
    // AUTO MELEE's row between SCENARIO and INSTANT MELEE (the Nitro Pack's campaign, handed over in this window: Boot/NitroVisit.cs). On
    // the Nitro side of such a visit, the main menu is the character choice: SCENARIOS' row open, no MELEE, and BACK where EXIT stands.
    public const int NitroEntry = 101, BackEntry = 102;
    /// <summary>AUTO MELEE's row: its top, SCENARIO's and INSTANT MELEE's left in the original's table, and the frame's right edge it keeps clear of.</summary>
    public const int AutoRowY = 368, ScenarioX = 399, InstantX = 478, RowRightEdge = 636;

    /// <summary>
    /// AUTO MELEE's row with NITRO between SCENARIO and INSTANT MELEE: the three at the row's own gap (the original's between the two,
    /// at least 4), NITRO's box less its lettering's margin each side; SCENARIO stays where it is unless the row would pass the frame's
    /// right edge, when all three move left together. Returns the three boxes' left edges.
    /// </summary>
    public static (int Scenario, int Nitro, int Instant) AutoRowPlace(int scenarioWidth, int nitroBoxWidth, int instantWidth, int margin = 4)
    {
        int gap = Math.Max(4, InstantX - (ScenarioX + scenarioWidth));
        int nitroInk = nitroBoxWidth - 2 * margin;
        int total = scenarioWidth + gap + nitroInk + gap + instantWidth;
        int sx = Math.Min(ScenarioX, RowRightEdge - total);
        int nx = sx + scenarioWidth + gap - margin;
        int ix = nx + margin + nitroInk + gap;
        return (sx, nx, ix);
    }
    private readonly Dictionary<int, PortButton> _p = [];
    private readonly List<PortButton> _pShown = [];
    private PortButton? _pHot;
    private string? _nitroDir;
    /// <summary>The Nitro side of a visit from the base menu (Boot/NitroVisit.cs).</summary>
    private bool Visit => Nitro && I76.Game.Boot.NitroVisit.Active;
    /// <summary>MELEE's AUTO MELEE row is open (the switch guard checks that the hand-back opens it).</summary>
    public bool AutoOpen => !Nitro && _w.TryGetValue(1, out var a) && a.Selected && _w[2].Selected;
    /// <summary>The TRIP row is open (on a visit's Nitro menu: SCENARIOS, the character choice).</summary>
    /// <summary>This menu comes after a won trip's credits: it offers the New Game Plus save once (<see cref="NewGamePlus"/>).</summary>
    public bool OfferNewGamePlus { get; init; }
    public bool TripOpen => _w.TryGetValue(Nitro ? 2 : 13, out var t) && t.Selected;
    /// <summary>The port's entries shown now, by id (for the switch guard).</summary>
    public IReadOnlyList<int> PortShown => [.. _pShown.Select(b => b.Id)];
    /// <summary>A port entry's box, if built (for the switch guard's clicks).</summary>
    public Rect2I? PortBox(int id) => _p.TryGetValue(id, out var b) ? b.Box : null;

    /// <summary>The Impact labels: the base bank's by its sprite numbers, Nitro's by bank 56's (ImpactLabels.NitroMain).</summary>
    private ImpactLayer? Imp => Shell.Impact;
    /// <summary>The connection row's banners (the original's have none): what each of the port's transports is.</summary>
    private static readonly Dictionary<int, string> NetBanner = new()
    {
        [7] = "HOST OR FIND GAMES ON YOUR LOCAL NETWORK",
        [14] = "HOST, OR JOIN A GAME AT AN ADDRESS",
    };

    /// <summary>The base banners are 400 wide from x 130; Nitro's shorter ones sit centred in the same span.</summary>
    private int BannerX(int sprite) => 130 + (400 - (_bank[sprite]?.Width ?? 400)) / 2;

    public override void Enter()
    {
        _bank = Nitro ? Shell.Db.BankNative(NitroBank) : Shell.Db.Bank(0x3e);
        foreach (var (id, x, y, sprite, banner) in Nitro ? NitroTable : Table)
        {
            int hot = Nitro ? sprite + 1 : sprite - 1;
            if (Nitro)
            {
                var nb = banner >= 0 ? new SpriteWidget(-1, _bank[banner]?.Width == 275 ? 185 : 123, BannerTop, _bank, banner) { NitroBank = true } : null;
                _w[id] = new SpriteWidget(id, x, y, _bank, sprite, nb) { Hot = hot, NitroBank = true, Text = id == 0xe ? "LAN" : null };   // IPX is the LAN here
                continue;
            }
            // TRIP's banner on a New Game Plus lap (the play fork's): the lap, in place of the original's words
            if ((NetBanner.GetValueOrDefault(id) ?? (id == 13 && Shell.Trip.Plus.Lap > 1 ? NewGamePlus.Banner(Shell.Trip.Plus.Lap) : null)) is { } words)
            {
                // a banner the size of the others (sprite 41's), its words drawn in place of a sprite
                var nb = new SpriteWidget(-1, BannerX(Nitro ? 42 : 41), 460, _bank, Nitro ? 42 : 41);
                _w[id] = new SpriteWidget(id, x, y, _bank, sprite, nb) { Hot = hot, Caption = words, CaptionFont = Shell.Db.Font(0x45), Text = id == 7 ? "LAN" : null };
                continue;
            }
            var b = banner >= 0 ? new SpriteWidget(-1, BannerX(banner), 460, _bank, banner) : null;
            _w[id] = new SpriteWidget(id, x, y, _bank, sprite, b) { Hot = hot };
        }
        BuildPortEntries();
        Repaint();
        if (!Nitro && I76.Game.Boot.NitroVisit.TakeReturnToRow()) OpenAuto();
        // I76_LOAD_PROFILE: the shell's start-up, the engine's start to the main menu's first frame (docs/perf/loadtime.md)
        if (I76.Game.Boot.LoadProfiler.On)
            RenderingServer.Singleton.Connect(RenderingServer.SignalName.FramePostDraw, Callable.From(() => GD.Print($"load-profile: main menu's first frame at {Time.GetTicksMsec()} ms from the engine's start")), (uint)GodotObject.ConnectFlags.OneShot);
        // the 3D garage's first build, made while the menu stands (#143)
        Garage3D.GarageWarmup.Start(Shell, this);
        // what every mission start shares, made while the menu stands idle (after the garage's)
        MenuWarmup.Start(Shell, this);
        // the play fork's New Game Plus offer, on the menu the credits return to after a T17 win (Shell.FirstScreen)
        if (OfferNewGamePlus && !Nitro) OfferSave();
    }

    /// <summary>
    /// The port's entries: the trip row's grey and height (sprite 21's, LOAD BOOKMARK), the banner line's place and size (sprite 41's);
    /// on a visit's Nitro menu, BACK in EXIT's box with EXIT's fit and grey, its banner on Nitro's banner line.
    /// </summary>
    private void BuildPortEntries()
    {
        if (_p.Count > 0) return;
        var grey = Color.Color8(109, 109, 109);
        if (!Nitro)
        {
            _nitroDir = I76.Game.Boot.NitroVisit.FindNitro(Shell.Install.Root);
            if (_nitroDir is null) return;
            // the row's fourth button, after LOAD BOOKMARK with the gap the row keeps between NEW TRIP and LOAD BOOKMARK
            // between SCENARIO and INSTANT MELEE, which move to make room (AutoRowPlace), their banners as before
            int h = _bank[19]?.Height ?? 22;
            var nitro = RowLabel("NITRO");
            int nw = PortButton.WidthFor(nitro);
            var (sx, nx, ix) = AutoRowPlace(_bank[19]?.Width ?? 70, nw, _bank[9]?.Width ?? 100);
            _w[9] = new SpriteWidget(9, sx, AutoRowY, _bank, 19, new SpriteWidget(-1, BannerX(40), 460, _bank, 40)) { Hot = 18 };
            _w[4] = new SpriteWidget(4, ix, AutoRowY, _bank, 9, new SpriteWidget(-1, BannerX(39), 460, _bank, 39)) { Hot = 8 };
            var bannerBox = new Rect2I(BannerX(41), 460, _bank[41]?.Width ?? 400, _bank[41]?.Height ?? 18);
            _p[NitroEntry] = new PortButton(NitroEntry, new Rect2I(nx, AutoRowY, nw, h), nitro, bannerBox, "THE NITRO PACK'S CAMPAIGN, FROM ITS OWN INSTALL", grey);
            return;
        }
        if (!Visit) return;
        var exit = _bank[20];
        var back = new ImpactLabels.Spec("BACK", 24f, 1f, 0f, 32.953f, 0f, Centre: true);
        // as wide as the word needs (it is wider than EXIT), ending where EXIT's box ends
        int ew = exit?.Width ?? 72, bw = Math.Max(ew, PortButton.WidthFor(back, 2));
        var box = new Rect2I(Math.Min(566, 566 + ew - bw), 16, bw, exit?.Height ?? 40);
        var nb = new Rect2I(123, BannerTop, _bank[43]?.Width ?? 400, _bank[43]?.Height ?? 18);
        _p[BackEntry] = new PortButton(BackEntry, box, back, nb, "BACK TO INTERSTATE '76", Color.Color8(0x77, 0x73, 0x70));
    }

    /// <summary>A trip-row label: the size, baseline and word spacing of the row's sprites (NEW TRIP's fit), centred in its box.</summary>
    private static ImpactLabels.Spec RowLabel(string text) => new(text, 15f, 0f, 0f, 17.122f, 0.125f, Centre: true);

    private void ShowPort(int id)
    {
        if (!_p.TryGetValue(id, out var b)) return;
        if (!_pShown.Contains(b)) _pShown.Add(b);
        b.Draw(Frame, Imp, Shell.Db.Font(0x45));
    }

    private void HidePort()
    {
        foreach (var b in _pShown) { b.Lit = false; b.Selected = false; b.Erase(Frame, Imp); }
        _pShown.Clear();
        _pHot = null;
    }

    /// <summary>MELEE, then AUTO MELEE with NITRO in its row: the hand-back's way in (the menu comes up with the row that holds NITRO open).</summary>
    private void OpenAuto() { Click(2); Click(1); }

    /// <summary>The background and the four top buttons, every row closed.</summary>
    public override void Repaint()
    {
        // after an overlay (hook) the open MELEE, AUTO and MULTI rows are drawn again; only TRIP's collapse
        int tripButton = Nitro ? 2 : 13;
        var top = Nitro ? NitroTop : Top;
        var keep = _shown.Where(w => w.Id != tripButton && !TripRow.Contains(w.Id) && !top.Contains(w.Id)).Select(w => (w.Id, w.Selected)).ToList();
        var keepTop = top.Where(id => id != tripButton && _w[id].Selected).ToList();
        Frame.LoadBackground(Shell.Db, 26);   // on Nitro its logo card, record 22
        Imp?.Clear();
        _shown.Clear();
        _pShown.Clear(); _pHot = null;
        foreach (var p in _p.Values) { p.Lit = false; p.Selected = false; }
        if (Visit)
        {
            // the character choice: OPTIONS, SCENARIOS held open with its characters and TRAINING, and BACK
            foreach (var w in _w.Values) { w.Lit = false; w.Selected = false; }
            _hot = null;
            Show(0);
            _w[2].Selected = true; Show(2);
            foreach (int id in TripRow) Show(id);
            ShowPort(BackEntry);
            return;
        }
        foreach (var w in _w.Values) { w.Lit = false; w.Selected = false; }
        _hot = null;
        foreach (int id in keepTop) _w[id].Selected = true;
        foreach (int id in top) Show(id);
        foreach (var (id, sel) in keep) { _w[id].Selected = sel; Show(id); }
        if (!Nitro && _w[1].Selected) ShowPort(NitroEntry);   // AUTO MELEE's row kept open: NITRO with it
        DrawEditorEntry();
    }

    private void Show(int id) { var w = _w[id]; if (!_shown.Contains(w)) _shown.Add(w); w.Draw(Frame, Imp); }

    private void Hide(params int[] ids)
    {
        foreach (int id in ids)
        {
            var w = _w[id];
            if (!_shown.Remove(w)) continue;
            w.Lit = false; w.Selected = false;
            w.Erase(Frame, Imp);
            if (_hot == w) _hot = null;
        }
    }

    private void Deselect(int id) { var w = _w[id]; if (!w.Selected) return; w.Selected = false; w.Draw(Frame, Imp); }

    private void Select(int id) { var w = _w[id]; w.Selected = true; w.Lit = false; w.Draw(Frame, Imp); }

    // the port's own entry, set in Impact like the menu's labels: the map editor (docs/map-editor.md), bottom left in the 640x480 frame,
    // wholly above the hover banners (Nitro's from x 123 at y 452, the base game's from x 130 at y 460), whose drawing and erasing
    // restore their rectangles from the background and would cut into it (#156)
    private static readonly Rectangle EditorBox = new(10, 414, 140, 34);
    /// <summary>The map editor entry's rectangle (x, y, width, height) in the 640x480 frame.</summary>
    public static (int X, int Y, int W, int H) EditorEntry => (EditorBox.X, EditorBox.Y, EditorBox.W, EditorBox.H);
    /// <summary>The top of the highest hover banner: Nitro's, at y 452 (the base game's are at 460).</summary>
    public const int BannerTop = 452;
    private bool _editorHot;
    private sealed record Rectangle(int X, int Y, int W, int H) { public bool Has(Vector2I m) => m.X >= X && m.X < X + W && m.Y >= Y && m.Y < Y + H; }

    private void DrawEditorEntry()
    {
        if (Shell.OnEditor is null) return;
        var (x, y, w, h) = (EditorBox.X, EditorBox.Y, EditorBox.W, EditorBox.H);
        Frame.Restore(x, y, w, h);
        var colour = _editorHot ? Colors.White : Color.Color8(109, 109, 109);
        // set as OPTIONS and EXIT are, the corner buttons: Impact at their size, slanted, white or grey
        var spec = new ImpactLabels.Spec("MAP EDITOR", 24.125f, 0.969f, 0f, 26f, 0f, Centre: true);
        if (Imp is { Available: true } imp)
        {
            imp.Put(x, y, w, h, spec, colour);
            return;
        }
        // the 1997 look (or no layer): the same Impact, cut into the frame's pixels as the sprites are
        if (_editorMask is null && !_editorMaskTried) { _editorMaskTried = true; _editorMask = ImpactPixels.Mask(spec, w, h); }
        if (_editorMask is { } mask)
        {
            var ink = Frame.Nearest(colour);
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    if (mask[r * w + c]) Frame.Fill(x + c, y + r, 1, 1, ink);
            return;
        }
        // not even Impact: the shell's own face, white or grey
        var font = Shell.Db.Font(0x45);
        var remap = new byte[256];
        Array.Fill(remap, Frame.Nearest(colour));
        remap[0] = 0xff;
        remap[font.Key & 0xff] = 0xff;
        Frame.Text(font, x + w / 2, y + (h - font.Height) / 2, "~MAP EDITOR", remap);
    }

    private bool[]? _editorMask;
    private bool _editorMaskTried;

    public override bool HoverShows => true;
    public override IReadOnlyList<I76.Game.Shell.PadRegion> PadRegions =>
        [.. _shown.Select(w => PadRegion.Of(w.X, w.Y, Math.Max(3, w.Width - 2), Math.Max(3, w.Height - 2))), .. _pShown.Select(b => PadRegion.Of(b.Box.Position.X, b.Box.Position.Y, b.Box.Size.X - 2, b.Box.Size.Y - 2))];

    public override void Tick()
    {
        var m = Shell.Mouse;
        // hover: the topmost unselected widget under the mouse lights up, with its banner
        var over = _shown.LastOrDefault(w => !w.Selected && w.Hit(m));
        if (over != _hot)
        {
            if (_hot is not null) { _hot.Lit = false; _hot.Draw(Frame, Imp); }
            _hot = over;
            if (_hot is not null) { _hot.Lit = true; _hot.Draw(Frame, Imp); }
        }
        var pOver = _pShown.LastOrDefault(b => !b.Selected && b.Hit(m));
        if (pOver != _pHot)
        {
            if (_pHot is not null) { _pHot.Lit = false; _pHot.Draw(Frame, Imp, Shell.Db.Font(0x45)); }
            _pHot = pOver;
            if (_pHot is not null) { _pHot.Lit = true; _pHot.Draw(Frame, Imp, Shell.Db.Font(0x45)); }
        }
        bool onEditor = Shell.OnEditor is not null && EditorBox.Has(m);
        if (onEditor != _editorHot) { _editorHot = onEditor; DrawEditorEntry(); }
        if (!Shell.Clicked) return;
        if (onEditor) { Shell.Sound(81); Shell.OpenEditor(); return; }
        if (_pShown.LastOrDefault(b => b.Hit(m)) is { } ph) { Shell.Sound(81); ClickPort(ph.Id); return; }
        var hit = _shown.LastOrDefault(w => w.Hit(m));
        if (hit is null) { if (!Visit) CloseAll(); return; }
        Shell.Sound(81);
        Click(hit.Id);
    }

    private void CloseAll()
    {
        if (Nitro) { Hide(3, 4, 5, 6, 7, 8, 9, 0xc, 10, 11, 0xe, 0xf); Deselect(2); Deselect(1); Deselect(9); Deselect(10); Deselect(11); return; }
        Hide([.. TripRow, .. MeleeRow, .. AutoRow, .. MultiRow, .. NetRow]);
        HidePort();
        Deselect(13); Deselect(2);
    }

    private void ClickPort(int id)
    {
        switch (id)
        {
            case NitroEntry when _nitroDir is { } dir:
                // the trip kept as Switch Game keeps it, the next mission's preload dropped, then the Nitro install in this window
                Shell.Trip.Save();
                Shell.OnPreload?.Invoke(null);
                I76.Game.Boot.GameSwitch.ToNitro(Shell.GetTree(), Shell.Install.Root, dir);
                break;
            case BackEntry: I76.Game.Boot.GameSwitch.BackFromNitro(Shell.GetTree()); break;
        }
    }

    private void Click(int id)
    {
        if (Nitro) { ClickNitro(id); return; }
        switch (id)
        {
            case 0: Shell.Overlay(new OptionsScreen()); break;
            case 17: Shell.Quit(); break;                                           // overlay 8: no confirmation
            case 13:
                Hide([.. MeleeRow, .. AutoRow, .. MultiRow, .. NetRow]); HidePort(); Deselect(2);
                Select(13); foreach (int r in TripRow) Show(r);
                break;
            case 2:
                Hide([.. TripRow]); Deselect(13);
                Select(2); foreach (int r in MeleeRow) Show(r);
                break;
            case 1:
                Hide([.. MultiRow, .. NetRow]); Deselect(15);
                Select(1); foreach (int r in AutoRow) Show(r);
                ShowPort(NitroEntry);
                break;
            case 15:                                                                  // MULTI MELEE: HOST, JOIN
                Hide([.. AutoRow]); HidePort(); Deselect(1);
                Select(15); foreach (int r in MultiRow) Show(r);
                break;
            case 3 or 5:                                                              // HOST / JOIN: the connections
                _host = id == 3;
                Deselect(id == 3 ? 5 : 3);
                Select(id); foreach (int r in NetRow) Show(r);
                break;
            // melee mode (HOST ? 2 : 6) + {IPX 1, INTERNET 2}, 0xc026
            case 7 or 14: Shell.Go(new MeleeScreen { Mode = (_host ? 2 : 6) + (id == 7 ? 1 : 2) }); break;
            case 4: Shell.Go(new MeleeScreen { Mode = 0 }); break;                    // INSTANT MELEE, 0xc021
            case 9: Shell.Go(new MeleeScreen { Mode = 1 }); break;                    // SCENARIO, 0xc024
            case 11:                                                                  // NEW TRIP, 0xc022: straight into T01
                // a finished trip (or the developer switch) asks first: START OVER, the original's, or TRIP+ (the play fork's)
                if (Shell.Trip.Plus.Finished || Shell.Settings.TripPlusUnlocked) AskTripPlus();
                else StartOver();
                break;
            case 12: Shell.Launch(new ShellLaunch("a01", GameKind.Trip, "vppa01.vcf")); break;     // TRAINING
            case 10: Shell.Overlay(new LoadBookmarkScreen { Direct = true }); break;
        }
    }

    /// <summary>NEW TRIP as the original: T01 on lap 1 in the stock car, no garage before T01.</summary>
    private void StartOver()
    {
        Shell.Trip.StartOver();
        Shell.LaunchTrip(Shell.Trip.StockCar(Shell, "vppt01.vcf"));
    }

    /// <summary>
    /// Trip+ (the play fork's; docs/deviations.md "Trip+"): NEW TRIP after a finished trip asks on the confirm receipt. YES is TRIP+:
    /// T01 again on the next lap, in the trip car as it stands (the car, the van, the repair order); NO starts over as the original
    /// does. Esc asks nothing and leaves the trip as it is.
    /// </summary>
    private void AskTripPlus()
    {
        int lap = Shell.Trip.Plus.NextLap;
        Shell.Modal(new ConfirmPopup
        {
            // the receipt holds about 24 letters a line
            Lines = NewGamePlus.NewTripLines(lap),
            Answer = plus =>
            {
                if (!plus) { StartOver(); return; }
                Shell.Trip.StartTripPlus();
                // the trip car whole; a trip that never had one (the developer switch on a fresh install) starts in the stock car
                Shell.LaunchTrip(Shell.Trip.Inventory(Shell) ?? Shell.Trip.StockCar(Shell, "vppt01.vcf"));
            },
            Cancelled = Repaint,
        });
    }

    /// <summary>
    /// The New Game Plus offer after a won trip: YES writes a bookmark (Load Bookmark's "Scene 1. New Game+ Lap N") that starts
    /// Scene 1 on the next lap in the trip car as it stands, beside the other bookmarks, and says where it is; the trip itself is not
    /// changed and nothing starts. NO (or Esc) leaves the menu as it is: NEW TRIP still offers the lap.
    /// </summary>
    private void OfferSave()
    {
        int lap = Shell.Trip.Plus.NextLap;
        Shell.Modal(new ConfirmPopup
        {
            Lines = NewGamePlus.OfferLines(lap),
            Answer = yes =>
            {
                if (!yes) { Repaint(); return; }
                var marks = Bookmarks.Load();
                string stem = marks.NextStem();
                marks.Entries.Add(NewGamePlus.Bookmark(lap, stem, (Shell.Settings.PlayOptions & 2) != 0));
                marks.Entries.Sort((x, y) => string.CompareOrdinal(x.Stem, y.Stem));
                // the car, its parts, the van's stock and the repair order, as the trip left them
                if (Shell.Trip.Inventory(Shell) is { } inv) Bookmarks.WriteCar(stem, inv.Save());
                marks.Save();
                Repaint();
                Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Closed = Repaint, Text = NewGamePlus.SavedNotice(lap) });
            },
            Cancelled = Repaint,
        });
    }

    /// <summary>The main menu's click handler on Nitro.</summary>
    private void ClickNitro(int id)
    {
        if (Visit && id is 2 or 1 or 0x12) return;   // the visit's character choice: SCENARIOS stays open; no MELEE or EXIT
        switch (id)
        {
            case 0: Shell.Overlay(new OptionsScreen()); break;
            case 0x12: Shell.Quit(); break;
            case 2:
                Hide(9, 0xc, 10, 11, 0xe, 0xf); Deselect(1);
                Select(2); foreach (int r in TripRow) Show(r);
                break;
            case 1:
                Hide(3, 4, 5, 6, 7, 8); Deselect(2);
                Hide(10, 11, 0xe, 0xf); Deselect(9);
                Select(1); foreach (int r in MeleeRow) Show(r);
                break;
            case 3 or 4 or 5 or 7: Shell.Go(new ScenarioScreen { Character = id == 7 ? 3 : id - 3 }); break;   // 0xc024
            case 6: break;                                                                       // the locked "?"
            case 8: Shell.Launch(new ShellLaunch("b01", GameKind.Trip, "vppa01.vcf")); break;    // the training launch: b01 with vppa01
            case 0xc: Shell.Go(new MeleeScreen { Mode = 0 }); break;                              // INSTANT MELEE, 0xc021
            case 9:                                                                              // MULTI MELEE: HOST, JOIN
                Hide(0xe, 0xf); Deselect(10); Deselect(11);
                Select(9); Show(10); Show(11);
                break;
            case 10 or 11:                                                                       // the connections
                _host = id == 10;
                Deselect(id == 10 ? 11 : 10);
                Select(id); Show(0xe); Show(0xf);
                break;
            // melee mode (HOST ? 2 : 6) + {IPX 1, INTERNET 2}, as the base menu
            case 0xe or 0xf: Shell.Go(new MeleeScreen { Mode = (_host ? 2 : 6) + (id == 0xe ? 1 : 2) }); break;
        }
    }
}
