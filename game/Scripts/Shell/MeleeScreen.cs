using Godot;
using I76.Formats.Input;
using I76.Formats.Shell;

namespace I76.Game.Shell;

/// <summary>
/// The melee forms:
/// mode 0 INSTANT MELEE (record 24: an arena and up to seven A.I. drivers),
/// mode 1 SCENARIO (record 25), and the network melees (NetForms.cs): 3 and
/// 4 the NET MELEE HOST EVENT FORM (record 19, the LAN and an address), 7
/// and 8 its ENTRY ROSTER (record 18). The buttons are painted in the art;
/// clicks go through the hotspot table (strict), every handled click plays
/// record 82, all text is font 0x45, lists show six 17-px rows. The car's
/// blueprint front and side views come from bank 0x3c.
/// </summary>
public sealed partial class MeleeScreen : ShellScreen
{
    public required int Mode { get; init; }
    /// <summary>A notice to show over the form on entry (why the last network game did not start or ended).</summary>
    public string? Notice { get; init; }
    private bool Nitro => Shell.Install.IsNitro;
    private NitUserMel? _user;
    /// <summary>
    /// The instant melee's arenas: Nitro's .cbt files by their names, else the base game's fifteen and its addon\*.lvl levels; a host form's
    /// have the maps downloaded from network games after them (#207, <see cref="DownloadedArenas"/>).
    /// </summary>
    private IReadOnlyList<(string Name, string Msn)> Arenas => Coop ? CoopList : Nitro ? (Hosting ? HostArenas : Shell.NitroArenas) : Hosting ? HostBaseArenas : Shell.BaseArenas;
    private List<(string Name, string Msn)>? _hostBaseArenas;
    private List<(string Name, string Msn)> HostBaseArenas => _hostBaseArenas ??= [.. Shell.BaseArenas, .. DownloadedArenas.For(Shell.Install, ".lvl")];
    private bool Net => Mode >= 2;
    private bool Hosting => Mode is >= 2 and <= 5;

    // the hotspot table: id → x0, x1, y0, y1
    private static readonly Dictionary<int, (int X0, int X1, int Y0, int Y1)> Hot = new()
    {
        [0] = (31, 216, 150, 270), [1] = (9, 27, 128, 144), [2] = (9, 27, 250, 268), [3] = (610, 621, 400, 415), [4] = (437, 449, 419, 434),
        [5] = (456, 616, 422, 435), [6] = (229, 369, 453, 471), [7] = (12, 165, 44, 58), [8] = (150, 166, 288, 303), [9] = (198, 213, 288, 303),
        [10] = (9, 215, 335, 454), [11] = (31, 216, 150, 270), [12] = (9, 27, 128, 144), [13] = (9, 27, 250, 268), [14] = (173, 213, 46, 57),
        [15] = (489, 630, 453, 471),
        // (the base game's host form: the art's ids above where the original has them, the port's settings HostFormLayout's)
        [22] = (173, 213, 306, 317),
        // the Nitro host form's WEAPON RESTRICTIONS (its own rectangle is NitroHost's)
        [24] = (31, 193, 310, 326),
        // the internet roster's SERVERS (the port's, drawn: NetForms.cs), the address book
        [39] = (42, 183, 453, 471),
    };
    // The ids each mode keeps (the host forms' NO. DRIVERS ⊟ ⊞ moved down to y 338..353)
    // (opened from a co-op lobby, the form leaves the car, the game type and SIM RATE alone: NetForms.Lobby.cs)
    // (opened from the game, it leaves the car, the game type, team play, the rate and the car rules alone: NetForms.Game.cs)
    private int[] Live => LobbyMode ? [.. LiveIds.Where(id => !LobbyFixed.Contains(id))] : GameMode ? [.. LiveIds.Where(id => !GameFixed.Contains(id))] : LiveIds;
    private int[] LiveIds => Mode switch
    {
        0 => [3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        1 => [0, 1, 2, 3, 4, 5, 6, 7, 14, 15],
        3 or 4 when Nitro => [3, 4, 5, 6, 7, 11, 12, 13, 14, 15, 8, 9, 22, 23, 24, 25, 27],
        // the base game's (HostFormLayout): a co-op form has only what a story mission has a use for (no team play, score limit,
        // Bots, map to share, or melee fairness rules)
        3 or 4 => [.. HostFormLayout.Hot(_coopBase).Keys.Order()],
        8 => [3, 4, 5, 6, 7, 11, 12, 13, 14, 15, 39],
        _ => [3, 4, 5, 6, 7, 11, 12, 13, 14, 15],
    };
    private bool BaseHost => Hosting && !Nitro;
    private (int X0, int X1, int Y0, int Y1) ModeBtn => Rect(32);
    private (int X0, int X1, int Y0, int Y1) Rect(int id) =>
        BaseHost && HostFormLayout.Hot(_coopBase).TryGetValue(id, out var h) ? (h.X0, h.X1, h.Y0, h.Y1) :
        // Nitro's instant melee table is the base one's but for the driver box, 11..170: the MAKE / MODEL and VARIANT arrows are only the arrows
        Nitro && !Hosting && id == 7 ? Hot[7] with { X0 = 11, X1 = 170 } :
        Nitro && Hosting && NitroHost.TryGetValue(id, out var r) ? r : Hosting && id is 8 or 9 ? Hot[id] with { Y0 = 338, Y1 = 353 } : Hot[id];

    private const int Row = 17;
    /// <summary>The area list's rows shown: the art's six, or on the base host form four (HostFormLayout: its box takes the room).</summary>
    private int Rows => BaseHost ? HostFormLayout.ListRows : 6;
    /// <summary>The drivers' box shows seven rows (the original's whole list: the player and seven opponents fill it); I26 allows <see cref="MaxOpponents"/> opponents and scrolls the box.</summary>
    private const int DriverRows = 7;
    /// <summary>
    /// Opponents in an instant melee: 31, so a game is 32 cars like the online game's (docs/multiplayer.md "Many players"). The
    /// arenas have 8-17 spawn points: the rest are spread to clear ground beside the best free point (MeleeGame.Place).
    /// </summary>
    public const int MaxOpponents = MaxDrivers - 1;
    /// <summary>The original's own limit: the player and seven opponents (MULTICAR.def's eight slots). nituser.mel gets no more than this.</summary>
    public const int OriginalSlots = 8;
    private int _dscroll;
    private ShellFont _font = null!;
    private SpriteBank _cars = null!;
    private MeleeData _data = null!;
    private MeleeSetup _setup = null!;
    private readonly List<(string Name, int Model, int Variant)> _slots = [];
    private readonly List<string> _pool = [];
    private int _shown, _scroll;
    private bool _renaming;
    private readonly I76.Formats.Input.HoldRepeat _hold = new();
    private readonly Random _rand = new();

    public override void Enter()
    {
        // back from the chassis form: the form as it was
        if (_slots.Count > 0) { Repaint(); NetResume(); return; }
        _font = Shell.Db.Font(0x45);
        _cars = Nitro ? Shell.Db.BankNative(54) : Shell.Db.Bank(0x3c);
        _data = Shell.Melee;
        _setup = MeleeSetup.Load();
        if (Nitro && Mode == 0)
        {
            // nituser.mel's instant melee block
            _user = NitroUser.Load(Shell.Install);
            (Area, _setup.Model, _setup.Variant, _setup.Drivers, _setup.Driver) = (_user.MeleeArena, _user.MeleeModel, _user.MeleeVariant, _user.MeleeDrivers, _user.MeleeDriver);
        }
        if (_data.Models.Count == 0) { Shell.Go(new MainMenuScreen()); return; }
        _setup.Model = Math.Clamp(_setup.Model, 0, _data.Models.Count - 1);
        _setup.Variant = Math.Clamp(_setup.Variant, 1, _data.Models[_setup.Model].Variants.Count);
        _slots.Add((_setup.Driver, _setup.Model, _setup.Variant));
        if (Mode == 0)
        {
            // The 53 names, and one A.I. driver to start with
            _pool.AddRange(MeleeData.DriverNames);
            AddDriver();
            Area = Math.Clamp(Area, 0, Arenas.Count - 1);
        }
        else if (Mode == 1)
        {
            RemixScenarios.Ensure(Shell.Install.Root);   // the generated scenarios after the stock ones (docs/remix-missions.md)
            Area = Math.Clamp(Area, 0, MeleeData.Scenarios.Length - 1);
        }
        else NetEnter();
        _scroll = Mode is 7 or 8 ? 0 : Math.Clamp(Area - Rows + 1, 0, Math.Max(0, ListCount - Rows));
        Repaint();
        if (Net) NetShown();
        else if (Notice is { Length: > 0 } n) Say(n);
    }

    private int ListCount => Mode switch { 1 => MeleeData.Scenarios.Length, 7 or 8 => _rows.Count, _ => Arenas.Count };
    private const int ListW = 182;

    /// <summary>One row of the list (<see cref="ListRowText"/>); the co-op list in one handwriting (<see cref="CoopListSmaller"/>).</summary>
    private void DrawListRow(int i, int x, int y)
    {
        var small = Shell.Db.Font(0x44);
        var (text, smaller) = ListRowText(ListName(i), _font.Measure, small.Measure, ListW, Coop && CoopListSmaller);
        if (smaller) Frame.Text(small, x, y + 2, text);
        else Frame.Text(_font, x, y, text);
    }

    /// <summary>
    /// Whether the co-op list is drawn in the smaller handwriting: when any of its titles is wider than the list in the form's own ("Scene 10:
    /// Van to Tatum"), every row is, so the trip missions' "Scene N:" all read the same (upstream #234: a row that changed hand read as another kind).
    /// </summary>
    private bool CoopListSmaller => _coopSmaller ??= CoopList.Any(m => _font.Measure(m.Name) > ListW);
    private bool? _coopSmaller;

    /// <summary>
    /// A list row's words in <paramref name="w"/> pixels: as they are in the form's handwriting, else (or when <paramref name="smaller"/>) in the
    /// smaller handwriting (font 0x44), cut at the end if even that is too wide. A trip mission's number leads its title and is never cut.
    /// </summary>
    public static (string Text, bool Smaller) ListRowText(string t, Func<string, int> measure, Func<string, int> smallMeasure, int w, bool smaller = false)
    {
        if (!smaller && measure(t) <= w) return (t, false);
        while (t.Length > 0 && smallMeasure(t) > w) t = t[..^1];
        return (t, true);
    }

    /// <summary>
    /// Where list row k's text goes. The Nitro host form's art has its first row 22 px tall (the highlight's own row, rules at 147 and 169) and the
    /// rest 17 each from 169, where the other forms' rules run 147, 164, 181...; the rows follow the rules there, so no line cuts through the words.
    /// </summary>
    private int ListRowY(int k) => Nitro && Hosting && k > 0 ? 154 + Row * k : 149 + Row * k;
    private int ListRowAt(int y) => Nitro && Hosting ? (y < 169 ? 0 : 1 + (y - 169) / Row) : (y - 151) / Row;

    private string ListName(int i) => Mode == 1 ? MeleeData.Scenarios[i].Name : Arenas[i].Name + (DownloadedArenas.Is(Arenas[i].Msn) ? DownloadedArenas.Mark : "");

    /// <summary>
    /// A co-op list row's tooltip (the fork's; upstream #197): the mission's title and what it is about, its first objective in the mission's
    /// own words (MissionTitles.CoopSummary), so a mission is told by more than its name.
    /// </summary>
    private (string Text, Rect2I Zone)? CoopRowTip(Vector2I p)
    {
        if (!Coop || Mode is 7 or 8 || p.X < 28 || p.X > 224 || p.Y < 151) return null;
        int k = ListRowAt(p.Y), i = _scroll + k;
        if (k < 0 || k >= Rows || i < 0 || i >= ListCount) return null;
        var (name, msn) = Arenas[i];
        string what = I76.Formats.Mission.MissionTitles.CoopSummary(Shell.Install, msn, Shell.Vfs);
        int y = ListRowY(k);
        return (what.Length > 0 ? $"{name}: {what}" : name, new Rect2I(28, y, 196, Row));
    }

    private void AddDriver()
    {
        if (_slots.Count > MaxOpponents || _pool.Count == 0) return;
        int n = _rand.Next(Math.Max(1, _pool.Count - 1));
        int model = _rand.Next(Math.Max(1, _data.StockCount));      // never a hidden model (Hidden Weapons & Cars)
        // Nitro's drivers get a random variant too (base: always the first)
        _slots.Add((_pool[n], model, Nitro ? _rand.Next(_data.Models[model].Variants.Count) + 1 : 1));
        _pool.RemoveAt(n);
        _dscroll = DriverScrollMax;
    }

    private void RemoveDriver()
    {
        if (_slots.Count <= 1) return;     // the handler's `cmp count,1 / jle`: down to no A.I. driver at all
        _pool.Add(_slots[^1].Name);
        _slots.RemoveAt(_slots.Count - 1);
        _shown = Math.Min(_shown, _slots.Count - 1);
        _dscroll = Math.Clamp(_dscroll, 0, DriverScrollMax);
    }

    private int DriverScrollMax => Math.Max(0, _slots.Count - 1 - DriverRows);
    private bool OverDrivers(Vector2I p) => Rect(10) is var r && r.X0 < p.X && p.X < r.X1 && r.Y0 < p.Y && p.Y < r.Y1;

    /// <summary>The scrollbar of a drivers' box with more than seven opponents: a thin thumb at the box's right edge.</summary>
    private void DrawDriverBar()
    {
        int opp = _slots.Count - 1;
        if (opp <= DriverRows) return;
        int top = 338, h = Row * DriverRows - 2;
        int th = Math.Max(8, h * DriverRows / opp), ty = top + (h - th) * _dscroll / DriverScrollMax;
        Frame.Fill(212, top, 1, h, Frame.Nearest(Color.Color8(70, 50, 30)));
        Frame.Fill(211, ty, 3, th, Frame.Nearest(Color.Color8(150, 36, 22)));
    }

    public override void Repaint()
    {
        _carTips.Clear();
        _modeBtnBaked = false;
        _applyBaked = false;
        _boxBaked = 0;
        _art = null;
        if (Nitro && Mode == 0) Frame.LoadBackground(Shell.Db.BackgroundNative(20), 24, 20);   // base 24's art, byte for byte
        else Frame.LoadBackground(Shell.Db, Mode switch { 0 => 24, 1 => 25, 3 or 4 => 19, _ => 18 });
        Draw();
    }

    /// <summary>Every text and the car pictures, over the clean art.</summary>
    private void Draw()
    {
        Frame.RestoreAll();
        var (name, model, variant) = _slots[_shown];
        int x0 = Mode == 1 ? 40 : 34;
        Frame.Text(_font, 12, 45, _slots[0].Name + (_renaming ? "_" : ""));
        Frame.Text(_font, 288, 58, name);
        if (Mode is 7 or 8) { DrawGames(); DrawBuild(); }
        else if (ListCount == 0) Frame.Text(_font, x0, 109, "No arenas");
        else
        {
            DrawListRow(Area, x0, Nitro && Hosting ? 130 : 109);
            for (int k = 0; k < Rows && _scroll + k < ListCount; k++) DrawListRow(_scroll + k, x0, ListRowY(k));
            // the SCENARIO form's list runs past its six rows with the generated scenarios: say so beside its arrows, and a scroll bar
            if (Mode == 1 && ListCount > Rows)
            {
                var (above, below) = MeleeData.ScrollHint(_scroll, Rows, ListCount);
                if (above is not null) Frame.Text(_font, 40, 128, above);
                if (below is not null) Frame.Text(_font, 40, 252, below);
                DrawListBar();
            }
        }
        if (Nitro && Hosting) DrawNitroHost();
        else if (Hosting) DrawHost();
        if (Hosting) DrawBuild();
        if (LobbyMode || GameMode) DrawApplyButton();
        if (Mode == 1 && Victory.Lightest(MeleeData.Scenarios[Area].Msn) is { } best && best.Model >= 0 && best.Model < _data.Models.Count)
            Frame.Text(_font, 16, 308, _data.Models[best.Model].MakeModel);     // after the art's "Lightest car to win:"
        if (Mode == 0)
        {
            Frame.Text(_font, 173, 288, $"{_slots.Count - 1:00}");
            for (int s = 1 + _dscroll; s < _slots.Count && s - _dscroll <= DriverRows; s++) Frame.Text(_font, 20, 338 + Row * (s - 1 - _dscroll), _slots[s].Name);
            DrawDriverBar();
        }
        if (StoryCar)
        {
            // a co-op seat in the mission's own car: the story's, whatever it is (its model is the mission's, not the list's)
            Frame.Text(_font, 330, 399, "Mission's own car");
            Frame.Text(_font, 292, 421, "the story's own");
        }
        else DrawCar(Frame, _font, _cars, _data, model, variant, Shell.Parts);
        // the arena's sketch between the car's views, over them (the base host form's; the port's)
        if (Hosting && !Nitro) { DrawListBar(); DrawArenaSketch(); }
    }

    /// <summary>The car panel's weapon rows (x 470, from y 58) and PARTS rows (from y 177), 17 px apart: where <see cref="DrawCar"/> prints them.</summary>
    public const int PanelX = 470, PanelW = 160, PanelWeaponsY = 58, PanelPartsY = 177;

    /// <summary>The car panel the melee and scenario forms share: the blueprint views, the weapons, the make and model, the variant.</summary>
    public static void DrawCar(ShellFrame frame, ShellFont font, SpriteBank cars, MeleeData data, int model, int variant, I76.Sim.Trip.PartsDatabase parts, string? makeModel = null, string? variantText = null, bool front = true)
    {
        var m = data.Models[model];
        int v = m.Art;
        if (front) frame.Blit(cars[2 * v], 229, 79);
        frame.Blit(cars[2 * v + 1], 229, 301);
        var weapons = data.Weapons(model, variant);
        for (int k = 0; k < weapons.Count; k++) frame.Text(font, 470, 58 + Row * k, weapons[k]);
        var kit = data.Parts(model, variant, parts);
        for (int k = 0; k < kit.Count; k++) frame.Text(font, 470, 177 + Row * k, kit[k]);
        frame.Text(font, 330, 399, makeModel ?? m.MakeModel);
        frame.Text(font, 292, 421, variantText ?? m.Variants[Math.Clamp(variant - 1, 0, m.Variants.Count - 1)]);
    }

    private readonly CarPanelTips _carTips = new();
    private bool _carTipShown;

    /// <summary>The host form's settings' tooltips first (NetForms.cs), then the car panel's weapons and parts (the equipment tooltips).</summary>
    public override (string Text, Rect2I Zone)? TipAt(Vector2I p)
    {
        _carTipShown = false;
        if (CoopRowTip(p) is { } coop) return coop;
        if (LobbyTipAt(p) is { } fixedTip) return fixedTip;
        if (GameTipAt(p) is { } gameTip) return gameTip;
        if (HostTipAt(p) is { } host) return host;
        if (StoryCar || _slots.Count == 0) return null;
        var (_, model, variant) = _slots[_shown];
        var t = _carTips.At(Shell, _data, model, variant, p);
        _carTipShown = t is not null;
        return t;
    }

    public override bool TipBesideRow => _carTipShown;

    public override bool TextEditing => _renaming || _naming;
    public override void StopEditing() { _renaming = false; _naming = false; Draw(); }

    private bool ScrollDrivers(int by)
    {
        int to = Math.Clamp(_dscroll + by, 0, DriverScrollMax);
        if (to == _dscroll) return false;
        _dscroll = to;
        Draw();
        return true;
    }

    private bool ScrollList(int by)
    {
        int rows = Mode is 7 or 8 ? ListRows : Rows;
        int to = Math.Clamp(_scroll + by, 0, Math.Max(0, ListCount - rows));
        if (to == _scroll) return false;
        _scroll = to;
        Draw();
        return true;
    }

    /// <summary>The form's hotspots, but for the lists: their rows one by one (the first and last scroll a press past them), the drivers' list likewise.</summary>
    public override IReadOnlyList<PadRegion> PadRegions
    {
        get
        {
            var l = new List<PadRegion>();
            foreach (int id in Live)
            {
                if (id is 0 or 11)
                {
                    bool games = Mode is 7 or 8;
                    int rows = games ? ListRows : Rows, y0 = id == 11 ? (games ? (Nitro ? 173 : 151) : 151) : Hot[0].Y0 + 1;
                    int n = Math.Min(rows, ListCount - _scroll), x0 = Rect(id).X0 + 1, x1 = games ? 206 : Rect(id).X1 - 1;
                    bool nh = !games && Nitro && Hosting;
                    for (int k = 0; k < n; k++)
                    {
                        int kk = k;
                        l.Add(PadRegion.Of(x0, nh ? (k == 0 ? 148 : 169 + Row * (k - 1)) : y0 + Row * k, x1 - x0, nh && k == 0 ? 21 : Row, d => d == NavDir.Up && kk == 0 ? ScrollList(-1) : d == NavDir.Down && kk == n - 1 ? ScrollList(1) : false));
                    }
                }
                else if (id == 10)
                    for (int sl = 1 + _dscroll; sl < _slots.Count && sl - _dscroll <= DriverRows; sl++)
                    {
                        int first = 1 + _dscroll, last = Math.Min(_slots.Count - 1, _dscroll + DriverRows), s2 = sl;
                        l.Add(PadRegion.Of(10, 338 + Row * (sl - 1 - _dscroll), 204, Row, d => d == NavDir.Up && s2 == first && _dscroll > 0 ? ScrollDrivers(-1) : d == NavDir.Down && s2 == last && _dscroll < DriverScrollMax ? ScrollDrivers(1) : false));
                    }
                else l.Add(PadRegion.Of(Rect(id)));
            }
            return l;
        }
    }

    private int HitId(Vector2I p) => Live.Where(id => Rect(id) is var r && r.X0 < p.X && p.X < r.X1 && r.Y0 < p.Y && p.Y < r.Y1).DefaultIfEmpty(-1).First();

    public override void Tick()
    {
        if (Mode is 7 or 8) PollGames();
        if (_renaming) { Rename(); return; }
        if (_naming) { RenameGame(); return; }
        // opened from a co-op lobby: Esc is CANCEL (back to the lobby, nothing changed)
        if (LobbyMode && Shell.Keys.Contains(Key.Escape)) { Shell.Sound(82); CancelLobby(); return; }
        // opened from the game: Esc is CANCEL too (back to the game, nothing changed)
        if (GameMode && Shell.Keys.Contains(Key.Escape)) { Shell.Sound(82); CancelGame(); return; }
        if (Shell.Wheel != 0 && Mode == 0 && OverDrivers(Shell.Mouse)) { _dscroll = Math.Clamp(_dscroll + Shell.WheelRows, 0, DriverScrollMax); Draw(); }
        else if (Shell.Wheel != 0) { _scroll = Math.Clamp(_scroll + Shell.WheelRows, 0, Math.Max(0, ListCount - (Mode is 7 or 8 ? ListRows : Rows))); Draw(); }
        // ⊞ / ⊟ and the list arrows repeat every 100 ms while the button is held
        int held = _hold.Id;
        if (_hold.Update(Shell.MouseHeld, HitId(Shell.Mouse), Shell.Delta) is var reps and > 0) { for (int r = 0; r < reps; r++) Act(held, Shell.Mouse); Draw(); }
        if (!Shell.Clicked) return;
        int id = HitId(Shell.Mouse);
        if (id < 0) return;
        Shell.Sound(82);
        if (id is 1 or 2 or 8 or 9 or 12 or 13 or 20 or 21) _hold.Begin(id);
        Act(id, Shell.Mouse);
        if (Shell.Current == this) Draw();
    }

    private void Act(int id, Vector2I p)
    {
        if (Net && NetAct(id, p)) return;
        switch (id)
        {
            case 0 or 11:      // a list row
                // Row = scroll + (y - 151) / 17, refused past the drawn rows
                if (p.Y < 151 || ListRowAt(p.Y) >= Rows) break;
                int row = _scroll + ListRowAt(p.Y);
                if (row >= 0 && row < ListCount) Area = row;
                break;
            case 1 or 12: _scroll = Math.Max(0, _scroll - 1); break;
            case 2 or 13: _scroll = Math.Min(Math.Max(0, ListCount - Rows), _scroll + 1); break;
            case 3: ModelPopup(); break;
            case 4: VariantPopup(); break;
            case 5:            // CONFIGURE CHASSIS: the chassis form for the car on display
                var (who, cm, cv) = _slots[_shown];
                Shell.Go(ChassisForm.Create(Shell, new MeleeChassis(cm, cv, n => { _slots[_shown] = (who, cm, n); OwnCarOff(); Shell.Go(this); }, () => Shell.Go(this))
                    { DoneAs = (m, n) => { _slots[_shown] = (who, m, n); OwnCarOff(); Shell.Go(this); } }));
                break;
            case 6: EnterArea(); break;
            case 7: _shown = 0; break;
            case 8: RemoveDriver(); break;
            case 9: AddDriver(); break;
            case 10:
                int slot = (p.Y - 338) / Row + 1 + _dscroll;
                if (slot >= 1 && slot < _slots.Count) _shown = slot;
                break;
            case 14: _renaming = true; break;
            case 15 when LobbyMode: CancelLobby(); break;
            case 15 when GameMode: CancelGame(); break;
            case 15: Leave(save: false); StopGames(); Shell.Go(new MainMenuScreen()); break;
        }
    }

    private void Rename()
    {
        var (name, model, variant) = _slots[0];
        bool changed = false;
        foreach (char c in Shell.Typed) if (name.Length < 24) { name += c; changed = true; }
        if (Shell.Keys.Contains(Key.Backspace) && name.Length > 0) { name = name[..^1]; changed = true; }
        if (Shell.Keys.Contains(Key.Enter) || Shell.Keys.Contains(Key.KpEnter) || Shell.Keys.Contains(Key.Escape) || Shell.Clicked) { _renaming = false; changed = true; }
        _slots[0] = (name, model, variant);
        if (changed) Draw();
    }

    private void ModelPopup()
    {
        // a co-op seat's list begins with the mission's own car (the default); a pick of a model takes the player's own car
        int own = StoryContext ? 1 : 0;
        Shell.Modal(new ListPopup
        {
            Sprite = 7, NativeSprite = Nitro ? 6 : null, X = 206, Y = 122, TextX = 22, TextY = 48, Visible = 9,
            Items = [.. (own == 1 ? new[] { "Mission's own car" } : []), .. _data.Models.Select(m => m.MakeModel)], Font = _font,
            Up = new(18, 36, 12, 25), Down = new(18, 36, 204, 217), List = new(16, 214, 47, 198), Cancel = new(146, 215, 209, 225),
            Picked = i =>
            {
                if (own == 1 && i >= 0) { Shell.Settings.CoopOwnCar = i == 0; Shell.Settings.Save(); i--; }
                // Variant back to 1, the model into the slot on show
                // choosing the model already on show changes nothing (the variant stays)
                if (i >= 0 && i != _slots[_shown].Model) _slots[_shown] = (_slots[_shown].Name, i, 1);
                Repaint();
            },
        });
    }

    /// <summary>A car picked on a co-op seat's form is the player's own pick, not the mission's.</summary>
    private void OwnCarOff()
    {
        if (!StoryContext || !Shell.Settings.CoopOwnCar) return;
        Shell.Settings.CoopOwnCar = false;
        Shell.Settings.Save();
    }

    private void VariantPopup() => VariantPopups.Show(Shell, _font, _data, _slots[_shown].Model, _slots[_shown].Variant, Nitro,
        v => { _slots[_shown] = (_slots[_shown].Name, _slots[_shown].Model, v); OwnCarOff(); Repaint(); },
        (n, left) =>
        {
            // the cars on this model that used a later variant slide down with it; one that had the deleted one takes its neighbour
            int model = _slots[_shown].Model;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Model == model) _slots[i] = (_slots[i].Name, model, VariantFiles.Remap(_slots[i].Variant, n, left));
            _setup.Variant = Math.Clamp(_setup.Variant, 1, left);
        },
        Repaint);

    /// <summary>ENTER AREA: the opponents (MULTICAR.def's slots 1..n), the player's car (vehscn.vcf) and the arena or scenario.</summary>
    private void EnterArea()
    {
        Leave(save: true);
        var (_, model, variant) = _slots[0];
        var car = _data.Vcf(model, variant);
        var (driver, make) = (_slots[0].Name, _data.Models[model].MakeModel);
        if (Mode == 0)
        {
            var opponents = _slots.Skip(1).Select(s => new MeleeOpponent(s.Name, _data.Vcf(s.Model, s.Variant))).ToList();
            Shell.Launch(new ShellLaunch(Arenas[Area].Msn, GameKind.AutoMelee, car) { Opponents = opponents, Driver = driver, Model = make, ModelIndex = model });
        }
        else
        {
            var msn = MeleeData.Scenarios[Area].Msn;
            // RANDOM JOB: a new job rolled and built before each launch (docs/remix-missions.md "The director")
            if (msn == I76.Formats.Remix.RemixDirector.JobStem && RemixScenarios.RollJob(Shell.Install.Root) is { Error: { } err })
            {
                GD.PrintErr(err);
                return;
            }
            Shell.Launch(new ShellLaunch(msn, GameKind.Scenario, car) { Driver = driver, Model = make, ModelIndex = model });
        }
    }

    /// <summary>Any exit but CANCEL writes user.mel.</summary>
    private void Leave(bool save)
    {
        if (!save) return;
        _setup.Driver = _slots[0].Name;
        _setup.Model = _slots[0].Model;
        _setup.Variant = _slots[0].Variant;
        if (!Net) _setup.Drivers = _slots.Count;     // I26's own count (melee.cfg): up to MaxOpponents + 1
        if (_user is { } u)
        {
            (u.MeleeArena, u.MeleeModel, u.MeleeVariant, u.MeleeDrivers, u.MeleeDriver) = (Area, _setup.Model, _setup.Variant, Math.Min(_setup.Drivers, OriginalSlots), _setup.Driver);   // the original's file never holds more than it reads
            NitroUser.Save(u);
            MeleeSetup.SaveNitroDrivers(_setup.Drivers);     // nituser.mel holds at most the original's eight; I26's own count is beside it
            return;
        }
        if (Hosting && !Nitro) _setup.CoopHost = _coopBase;
        if (Coop) _setup.CoopArena = Arenas.Count > 0 ? Arenas[Math.Clamp(Area, 0, Arenas.Count - 1)].Msn : _setup.CoopArena;
        if (Hosting) { if (!Coop) _setup.Game = _gameName; _setup.Bots = _bots; _setup.BotSkill = (int)_botSkill; _setup.BotCars = [.. _botCars]; _setup.HostRate = _rate; _setup.HostTurrets = _turrets; _setup.HostDrivers = _drivers; }
        if (Hosting && Shell.Settings.HostBotSkillDifficulty != _botSkillDifficulty) { Shell.Settings.HostBotSkillDifficulty = _botSkillDifficulty; Shell.Settings.Save(); }
        // the type's rows as hosted (the older keys above stay the last game's, for a build that reads only those)
        if (Hosting) _setup.Host(HostTypeNow).Merge(FormRows(HostTypeNow));
        if (Nitro && Hosting)
        {
            _setup.NitroKind = _kind;
            _setup.NitroArena = HostArenas.Count > 0 ? HostArenas[Math.Clamp(Area, 0, HostArenas.Count - 1)].Msn : "";
        }
        _setup.Save();
    }

    /// <summary>Off the screen (to the chassis form, or for good): the session search stops; it starts again on the way back.</summary>
    public override void Leave() => StopGames();
}

/// <summary>
/// A list picker (a modal loop): a
/// bank 0x24 sprite with a 6 px shadow, rows from (TextX, TextY) relative to
/// it, ▲ ▼ to scroll, a row picks and closes, CANCEL closes without.
/// Hotspots are relative to the popup and strict.
/// </summary>
public sealed class ListPopup : ShellScreen
{
    public required int Sprite { get; init; }
    public required int X { get; set; }
    public required int Y { get; set; }
    public required int TextX { get; set; }
    public required int TextY { get; set; }
    public required int Visible { get; set; }
    public required List<string> Items { get; init; }
    public required ShellFont Font { get; init; }
    public required (int X0, int X1, int Y0, int Y1) Up { get; set; }
    public required (int X0, int X1, int Y0, int Y1) Down { get; set; }
    public required (int X0, int X1, int Y0, int Y1) List { get; set; }
    public required (int X0, int X1, int Y0, int Y1) Cancel { get; set; }
    public required Action<int> Picked { get; init; }
    /// <summary>The Nitro shell's popups are bank 30 sprites: by their own number, and (if its art differs from the base popup's) where things are.</summary>
    public int? NativeSprite { get; init; }
    public PopupLayout? NativeLayout { get; init; }
    /// <summary>Where the list starts, and where it was left (the shell remembers some lists' offsets).</summary>
    public int Scroll { get; init; }
    public Action<int>? Scrolled { get; init; }
    /// <summary>
    /// The Nitro VARIANT popup's mode: a click on a row only selects it, DONE (<see cref="Done"/>) takes the
    /// selected row, DELETE (<see cref="DeleteBtn"/>) asks <see cref="DeleteRequested"/> about it and CANCEL leaves.
    /// </summary>
    public (int X0, int X1, int Y0, int Y1)? Done { get; set; }
    public (int X0, int X1, int Y0, int Y1)? DeleteBtn { get; set; }
    public Action<int>? DeleteRequested { get; init; }
    /// <summary>
    /// I26's addition to the base game's VARIANT popup (which has only CANCEL): a DELETE button drawn beside CANCEL in the
    /// art's own button colours, for the row under the mouse or the pad's cursor; the Delete key and the pad's X do the same.
    /// Rows still pick on a click, as the original's.
    /// </summary>
    public (int X0, int X1, int Y0, int Y1)? DrawnDelete { get; set; }
    /// <summary>The row selected on opening (select mode).</summary>
    public int Cursor { get; init; }
    /// <summary>The pad picks (the port's): the D-pad and the left stick move a cursor, A picks, B and Start cancel. Always on now; the screens that asked for it still may.</summary>
    public bool PadPicks { get; init; }
    public override bool OwnsPad => true;
    /// <summary>The cursor shows from the start (the list was opened from the pad); else on the D-pad's first press.</summary>
    public bool CursorShown { get; set; }
    public override bool EscapeOpensOptions => false;
    private int _scroll, _w, _h, _cursor;
    private const int Row = 17;
    private readonly I76.Formats.Input.PadRepeater _pad = new();
    private readonly I76.Formats.Input.HoldRepeat _hold = new();

    public override void Enter()
    {
        if (NativeLayout is { } nl)
        {
            (X, Y, TextX, TextY, Visible, Up, Down, List, Cancel) = (nl.X, nl.Y, nl.TextX, nl.TextY, nl.Visible, nl.Up, nl.Down, nl.List, nl.Cancel);
            if (DeleteRequested is not null) (Done, DeleteBtn) = (nl.Done, nl.Delete);
        }
        else if (DeleteRequested is not null) LayOutDelete();
        var s = Art;
        _w = s?.Width ?? 200; _h = s?.Height ?? 236;
        _scroll = Math.Clamp(Scroll, 0, Math.Max(0, Items.Count - Visible));
        _cursor = _scroll;
        if (Select || DrawnDelete is not null)
        {
            _cursor = Math.Clamp(Cursor, 0, Math.Max(0, Items.Count - 1));
            _scroll = Math.Clamp(_scroll, _cursor - Visible + 1, _cursor);
            CursorShown = true;
        }
        // the Nitro VARIANT popup (case 6): with more than 8 variants it opens scrolled to count - 8, the newest
        if (Select && Items.Count > Visible) _scroll = Items.Count - Visible;
        _pad.Settle();      // the A that opened it
        Draw();
    }

    private bool Select => Done is not null && DeleteBtn is not null;

    private Sprite? Art => NativeSprite is { } n ? Shell.Db.BankNative(30)[n] : Shell.Db.Bank(0x24)[Sprite];

    private void Draw()
    {
        Frame.Blit(Art, X, Y);
        Frame.Fill(X + 6, Y + _h, _w, 6, 0x0f);
        Frame.Fill(X + _w, Y + 6, 6, _h, 0x0f);
        // an entry marked '*' (a list header) is drawn without it
        for (int k = 0; k < Visible && _scroll + k < Items.Count; k++) Frame.Text(Font, X + TextX, Y + TextY + Row * k, Items[_scroll + k].StartsWith('*') ? Items[_scroll + k][1..] : Items[_scroll + k]);
        if (DrawnDelete is { } d) DrawDeleteButton(d);
        // the pad's cursor: a red pencil mark before its row and a line under it, inside the list
        if (CursorShown && _cursor - _scroll is >= 0 and var k2 && k2 < Visible)
        {
            byte red = Frame.Nearest(Color.Color8(150, 36, 22));
            int ry = Y + TextY + Row * k2;
            Frame.Fill(X + TextX - 1, ry + Font.Height, _w - TextX - 14, 1, red);
            for (int i = 0; i < 3; i++) Frame.Fill(X + TextX - 7 + i, ry + 3 + i, 1, 7 - 2 * i, red);
        }
    }

    /// <summary>
    /// Where DELETE and CANCEL go when DELETE is drawn: the row from just right of the down arrow to CANCEL's right border shared
    /// by two buttons as the address book's are (57 px wide, 8 apart: CANCEL's 71 px leave no room for a second one beside the
    /// arrow), CANCEL moved right to keep its right edge. Found from the art's own CANCEL (its border and its word), so both
    /// looks and every palette agree; without them, the art's places as given.
    /// </summary>
    private void LayOutDelete()
    {
        _cancelArt = null;
        if (Art is not { } a || CancelArt(a) is not { } art) { DrawnDelete = (Cancel.X0 - 49, Cancel.X0 - 4, Cancel.Y0, Cancel.Y1); return; }
        _cancelArt = art;
        var b = art.Border;
        int right = b.End.X, left = Down.X1 + 2, w = (right - left - DeleteGap) / 2;
        DrawnDelete = (left, left + w - 1, b.Position.Y, b.End.Y - 1);
        Cancel = (right - w, right - 1, b.Position.Y, b.End.Y - 1);
    }

    /// <summary>The gap between the two buttons, as the address book's (its 57 px buttons 9 px apart, start to start 66).</summary>
    private const int DeleteGap = 8;

    /// <summary>The art's CANCEL: its border's bounds and its word, relative to the popup (the art's own coordinates).</summary>
    private (Rect2I Border, ArtButtons.ArtLabel Word)? _cancelArt;

    private (Rect2I Border, ArtButtons.ArtLabel Word)? CancelArt(Sprite a)
    {
        byte Px(int x, int y) => a.Pixels[Math.Clamp(y, 0, a.Height - 1) * a.Width + Math.Clamp(x, 0, a.Width - 1)];
        var pal = Frame.Palette;
        if (pal is null) return null;
        var c = Cancel;
        return ArtButtons.Label(Px, pal, new Rect2I(c.X0 - 4, c.Y0 - 4, c.X1 - c.X0 + 9, c.Y1 - c.Y0 + 9)) is { } border
            && ArtButtons.Label(Px, pal, border.Box.Grow(-3)) is { } word ? (border.Box, word) : null;
    }

    /// <summary>
    /// DELETE and CANCEL drawn into the frame over the popup (so they show over an upscaled popup too): the paper under the row
    /// first (the plain paper between the down arrow and CANCEL, repeated), then each button with CANCEL's face; DELETE is lettered
    /// like CANCEL's word (<see cref="ArtButtons.Letter"/>: Arial Bold as tall as it, in its ink).
    /// There is no DELETE in the base game's banks.
    /// </summary>
    private void DrawDeleteButton((int X0, int X1, int Y0, int Y1) d)
    {
        if (Art is not { } a) return;
        byte At(int x, int y) => a.Pixels[Math.Clamp(y, 0, a.Height - 1) * a.Width + Math.Clamp(x, 0, a.Width - 1)];
        byte Px(int x, int y) => At(x - X, y - Y);
        if (_cancelArt is not var (border, word))
        {
            Frame.Text(Shell.Db.Font(0x47), X + d.X0 + 2, Y + d.Y0 + 2, "DELETE", ShellFrame.DefaultInk);
            return;
        }
        // the paper: the art's plain columns between the arrow and the old CANCEL, along the row and two pixels round it
        int p0 = Down.X1 + 2, plain = Math.Max(1, border.Position.X - 3 - p0);
        for (int y = border.Position.Y - 2; y < border.End.Y + 2; y++)
            for (int x = p0; x < border.End.X + 1; x++) Frame.Fill(X + x, Y + y, 1, 1, At(p0 + (x - p0) % plain, y));
        var src = new Rect2I(X + border.Position.X, Y + border.Position.Y, border.Size.X, border.Size.Y);
        var label = new ArtButtons.ArtLabel(new Rect2I(X + word.Box.Position.X, Y + word.Box.Position.Y, word.Box.Size.X, word.Box.Size.Y), word.Ink, word.Soft);
        Rect2I Dest((int X0, int X1, int Y0, int Y1) r) => new(X + r.X0, Y + r.Y0, r.X1 - r.X0 + 1, r.Y1 - r.Y0 + 1);
        var del = Dest(d);
        var cancel = Dest(Cancel);
        ArtButtons.Face(Frame, Px, src, label.Box.Position.X, del, 3);
        ArtButtons.Face(Frame, Px, src, label.Box.Position.X, cancel, 3);
        // the art's own word where it is printed in the frame's pixels (the 1997 look); lettered as DELETE is where the words are
        // drawn at the window's resolution (the modern look), so the two match and neither is the 640x480 word blown up
        if (Frame.HdFonts is null) ArtButtons.Word(Frame, Px, label.Box, cancel);
        else ArtButtons.Letter(Frame, Shell.Db.Font(0x47), "CANCEL", label, "CANCEL", cancel);
        ArtButtons.Letter(Frame, Shell.Db.Font(0x47), "DELETE", label, "CANCEL", del);
    }

    private Vector2I _lastMouse;
    private bool _padDeleteHeld;

    /// <summary>Asks to delete the cursor's row (the button, the Delete key, the pad's X).</summary>
    private void AskDelete()
    {
        if (_cursor < 0 || _cursor >= Items.Count) return;
        Shell.Sound(82);
        DeleteRequested!(_cursor);
    }

    /// <summary>The pad: up and down (D-pad or left stick, repeating) move the cursor (the list scrolling to keep it in view), A picks, B and Start cancel.</summary>
    private void PadTick()
    {
        var pads = I76.Game.Controls.PadSource.Read();
        var st = _pad.Update(pads.Count > 0 && I76.Game.Controls.Focused() ? pads[0] : default, Shell.Delta);
        int step = st.Dir == I76.Formats.Input.NavDir.Up ? -1 : st.Dir == I76.Formats.Input.NavDir.Down ? 1 : 0;
        if (step != 0)
        {
            if (CursorShown) _cursor = Math.Clamp(_cursor + step, 0, Items.Count - 1);
            CursorShown = true;
            _scroll = Math.Clamp(_scroll, _cursor - Visible + 1, _cursor);
            Shell.Sound(82);
            Draw();
        }
        if (DrawnDelete is not null)
        {
            bool x = pads.Count > 0 && I76.Game.Controls.Focused() && pads[0].Has(I76.Formats.Input.PadButton.X);
            bool edge = x && !_padDeleteHeld;
            _padDeleteHeld = x;
            if (edge) { AskDelete(); return; }
        }
        if ((st.Buttons & I76.Formats.Input.PadRepeater.Btn.Accept) != 0 && CursorShown) Close(_cursor);
        else if ((st.Buttons & (I76.Formats.Input.PadRepeater.Btn.Back | I76.Formats.Input.PadRepeater.Btn.Start)) != 0) Close(-1);
    }

    private bool In((int X0, int X1, int Y0, int Y1) r, Vector2I p) => r.X0 < p.X - X && p.X - X < r.X1 && r.Y0 < p.Y - Y && p.Y - Y < r.Y1;

    public override void Tick()
    {
        PadTick();
        if (Shell.Current != this) return;
        // Esc cancels, as the pad's B does (the port's: the original's lists took only CANCEL)
        if (Shell.Keys.Contains(Key.Escape)) { Close(-1); return; }
        if (Shell.Wheel != 0) { _scroll = Math.Clamp(_scroll + Shell.WheelRows, 0, Math.Max(0, Items.Count - Visible)); Draw(); }
        var p = Shell.Mouse;
        if (DrawnDelete is not null)
        {
            // the mouse moving over a row moves the mark to it (the row DELETE will take); a click still picks it
            if (p != _lastMouse && In(List, p) && _scroll + (p.Y - Y - TextY) / Row is var hr && hr >= 0 && hr < Items.Count && hr != _cursor) { _cursor = hr; Draw(); }
            _lastMouse = p;
            if (Shell.Keys.Contains(Key.Delete)) { AskDelete(); return; }
        }
        // the arrows repeat while the button is held (HoldRepeat): 0 up, 1 down
        int held = _hold.Id;
        for (int n = _hold.Update(Shell.MouseHeld, In(Up, p) ? 0 : In(Down, p) ? 1 : -1, Shell.Delta); n > 0; n--) ArrowStep(held);
        if (!Shell.Clicked) return;
        if (DrawnDelete is { } dd && In(dd, p)) { AskDelete(); return; }
        if (In(Up, p)) { _hold.Begin(0); ArrowStep(0); }
        else if (In(Down, p)) { _hold.Begin(1); ArrowStep(1); }
        else if (In(List, p))
        {
            int i = _scroll + (p.Y - Y - TextY) / Row;
            if (i < 0 || i >= Items.Count) return;
            if (Select) { _cursor = i; Shell.Sound(82); Draw(); }
            else Close(i);
        }
        else if (Select && In(Done!.Value, p)) { if (Items.Count > 0) Close(_cursor); }
        else if (Select && In(DeleteBtn!.Value, p)) { Shell.Sound(82); DeleteRequested!(_cursor); }
        else if (In(Cancel, p)) Close(-1);
    }

    private void ArrowStep(int arrow)
    {
        int to = Math.Clamp(_scroll + (arrow == 0 ? -1 : 1), 0, Math.Max(0, Items.Count - Visible));
        if (to == _scroll) return;
        _scroll = to; Shell.Sound(82); Draw();
    }

    private void Close(int i)
    {
        Shell.Sound(82);
        Frame.Restore(X, Y, _w + 6, _h + 6);
        Shell.Modal(null);
        Scrolled?.Invoke(_scroll);
        Picked(i);
    }
}

/// <summary>
/// victory.rec: per scenario, the lightest car to win it — its
/// model and built mass — updated when a win comes in lighter (or first).
/// Kept in user://victory.cfg.
/// </summary>
public static class Victory
{
    private const string Path = "user://victory.cfg";

    public static (int Model, int Mass)? Lightest(string scenario)
    {
        var c = new ConfigFile();
        if (c.Load(Path) != Error.Ok || !c.HasSectionKey(scenario, "model")) return null;
        return ((int)c.GetValue(scenario, "model"), (int)c.GetValue(scenario, "mass"));
    }

    public static void Record(string scenario, int model, float mass)
    {
        if (model < 0) return;
        int m = (int)mass;
        if (Lightest(scenario) is { } best && best.Mass <= m) return;
        var c = new ConfigFile();
        c.Load(Path);
        c.SetValue(scenario, "model", model);
        c.SetValue(scenario, "mass", m);
        c.Save(Path);
    }
}

/// <summary>
/// 0xc025 STANDINGS: record 28, NSCR.DAT's rows at
/// y = 70 + 24i — driver at x 32, model 191, deaths 436, kills 500, score
/// 553 — one row in a single-player melee, every player's in a network one
/// (best first). DONE goes back to the melee form the game came from.
/// </summary>
public sealed class StandingsScreen : ShellScreen
{
    public MeleeResult? Result { get; init; }
    /// <summary>A network melee's rows (the car files are named by their model).</summary>
    public IReadOnlyList<StandingRow>? Rows { get; init; }
    /// <summary>Where DONE goes: the instant melee form unless told.</summary>
    public Func<ShellScreen>? Then { get; init; }
    /// <summary>Said over the standings on entry (the game ended early: the server went away).</summary>
    public string? Notice { get; init; }

    public override void Enter()
    {
        Repaint();
        if (Notice is { Length: > 0 } n) Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = n, Closed = Repaint });
    }

    /// <summary>The network game had team play (NSCR's teamMode): a header row per team with the summed stats, members indented.</summary>
    public bool TeamPlay { get; init; }
    /// <summary>A Nitro network game's kind: race and CTF show only the score column on their own backgrounds.</summary>
    public I76.Sim.Missions.MeleeKind Kind { get; init; }

    /// <summary>A team's header row: a capture the flag game's teams are its own colours (1 Red Dawn ... 4 Black Plague), the others the eight of team play.</summary>
    public static string TeamHeader(I76.Sim.Missions.MeleeKind kind, int team) =>
        team is >= 1 and <= 8 && (kind != I76.Sim.Missions.MeleeKind.Ctf || team <= 4) ? I76.Sim.Missions.MeleeGame.TeamName(kind, team) : "";

    public override void Repaint()
    {
        // Background 23 for combat, 24 for CTF, 25 for race; race and CTF list only the score (x 500)
        bool scoreOnly = Shell.Install.IsNitro && Kind is I76.Sim.Missions.MeleeKind.Race or I76.Sim.Missions.MeleeKind.Ctf;
        int bg = Kind switch { I76.Sim.Missions.MeleeKind.Ctf => 24, I76.Sim.Missions.MeleeKind.Race => 25, _ => 23 };
        if (Shell.Install.IsNitro) Frame.LoadBackground(Shell.Db.BackgroundNative(bg), native: bg);
        else Frame.LoadBackground(Shell.Db, 28);
        // (indent, driver, model, deaths, kills, score)
        var lines = new List<(bool Header, string Driver, string Model, int Deaths, int Kills, int Score)>();
        if (Rows is { } all)
        {
            var sorted = all.OrderByDescending(r => r.Score).ToList();
            if (TeamPlay)
            {
                // One header per team (its name, the members' sums), then its members as "  name"
                foreach (var g in sorted.GroupBy(r => r.Team).OrderByDescending(g => g.Sum(r => r.Score)))
                {
                    lines.Add((true, TeamHeader(Kind, g.Key), "", g.Sum(r => r.Deaths), g.Sum(r => r.Kills), g.Sum(r => r.Score)));
                    foreach (var r in g) lines.Add((false, "  " + r.Driver, Shell.Melee.CarName(r.Car), r.Deaths, r.Kills, r.Score));
                }
            }
            else foreach (var r in sorted) lines.Add((false, r.Driver, Shell.Melee.CarName(r.Car), r.Deaths, r.Kills, r.Score));
        }
        else if (Result is { } m) lines.Add((false, m.Driver, m.Model, m.Deaths, m.Kills, m.Score));
        var f = Shell.Db.Font(0x45);
        int y = 70;
        _count = lines.Count;
        _top = Math.Clamp(_top, 0, Math.Max(0, _count - PerPage));
        // the sheet holds 16 rows; more drivers (to 64) page: the wheel, Up/Down, PgUp/PgDn
        if (_count > PerPage) Frame.Text(f, 410, 459, $"{_top + 1}-{Math.Min(_top + PerPage, _count)} of {_count}", ShellFrame.DefaultInk);
        foreach (var l in lines.Skip(_top).Take(PerPage))
        {
            Frame.Text(f, 32, y, l.Driver, ShellFrame.DefaultInk);
            if (!scoreOnly)
            {
                Frame.Text(f, 191, y, l.Model, ShellFrame.DefaultInk);
                Frame.Text(f, 436, y, l.Deaths.ToString(), ShellFrame.DefaultInk);
                Frame.Text(f, 500, y, l.Kills.ToString(), ShellFrame.DefaultInk);
            }
            Frame.Text(f, scoreOnly ? 500 : 553, y, l.Score.ToString(), ShellFrame.DefaultInk);
            y += 24;
        }
    }

    public override IReadOnlyList<PadRegion> PadRegions => [PadRegion.Of(250, 456, 138, 15)];

    private const int PerPage = 16;
    private int _top, _count;

    private void Page(int rows)
    {
        int to = Math.Clamp(_top + rows, 0, Math.Max(0, _count - PerPage));
        if (to == _top) return;
        _top = to;
        Repaint();
    }

    public override void Tick()
    {
        if (Shell.Wheel != 0) Page(Shell.WheelRows);
        if (Shell.Keys.Contains(Key.Down)) Page(1);
        if (Shell.Keys.Contains(Key.Up)) Page(-1);
        if (Shell.Keys.Contains(Key.Pagedown)) Page(PerPage);
        if (Shell.Keys.Contains(Key.Pageup)) Page(-PerPage);
        var m = Shell.Mouse;
        if (Shell.Clicked && 249 < m.X && m.X < 389 && 455 < m.Y && m.Y < 472) { Shell.Sound(82); Shell.Go(Then?.Invoke() ?? new MeleeScreen { Mode = 0 }); }
    }
}

/// <summary>Where a Nitro popup sits and what it holds (relative to the popup: arrows, rows, CANCEL).</summary>
public sealed record PopupLayout(int X, int Y, int TextX, int TextY, int Visible, (int X0, int X1, int Y0, int Y1) Up, (int X0, int X1, int Y0, int Y1) Down, (int X0, int X1, int Y0, int Y1) List, (int X0, int X1, int Y0, int Y1) Cancel)
{
    /// <summary>The VARIANT popup's DONE and DELETE (ids 3 and 4), relative to the popup.</summary>
    public (int X0, int X1, int Y0, int Y1) Done { get; init; } = (65, 136, 238, 254);
    public (int X0, int X1, int Y0, int Y1) Delete { get; init; } = (12, 83, 259, 275);
}

/// <summary>
/// The VARIANT list (bank 36 sprite 9; Nitro's bank 30 sprite 8 with DONE, DELETE and CANCEL). In Nitro a click
/// selects a row and DONE takes it; DELETE removes the player's saved variant after "Sure you wanna delete this variant?",
/// a stock one with only a notice (bank 36 sprite 34).
/// The base game's list (no DELETE in the original) keeps its click-to-pick rows and gets a DELETE button, the Delete key and
/// the pad's X on the marked row, with the same confirm and rules (I26's addition, docs/deviations.md).
/// </summary>
public static class VariantPopups
{
    /// <summary>Shows the list for <paramref name="model"/> with <paramref name="current"/> (1-based) selected. <paramref name="deleted"/> gets the number deleted and the count left.</summary>
    public static void Show(Shell shell, ShellFont font, MeleeData data, int model, int current, bool nitro,
        Action<int> picked, Action<int, int> deleted, Action repaint)
    {
        var m = data.Models[model];
        void Reopen(int cur) { repaint(); Show(shell, font, data, model, cur, nitro, picked, deleted, repaint); }   // the form again: the notice and the receipt wiped part of it
        shell.Modal(new ListPopup
        {
            Sprite = 9, X = 235, Y = 122, TextX = 15, TextY = 50, Visible = 9,
            Items = [.. m.Variants], Font = font, Cursor = current - 1,
            Up = new(18, 36, 12, 25), Down = new(18, 36, 204, 217), List = new(16, 158, 47, 198), Cancel = new(89, 158, 209, 225),
            NativeSprite = nitro ? 8 : null,
            NativeLayout = nitro ? new PopupLayout(220, 97, 16, 82, 8, new(14, 38, 60, 78), new(14, 38, 220, 238), new(12, 188, 80, 216), new(118, 188, 259, 275)) : null,
            Picked = i => { if (i >= 0) picked(i + 1); else repaint(); },
            DeleteRequested = i => Delete(shell, data, model, i + 1, nitro, deleted, Reopen),
        });
    }

    private static void Delete(Shell shell, MeleeData data, int model, int n, bool nitro, Action<int, int> deleted, Action<int> reopen)
    {
        var m = data.Models[model];
        if (n <= m.Base)
        {
            // Nitro has the notice's own art (bank 36 sprite 34); the base game has none, so the blank notice carries the words
            shell.Modal(nitro
                ? new NoticePopup { Sprite = 0, NativeSprite = 34, Ok = new Rect2I(119, 61, 42, 16), Closed = () => reopen(n) }
                : new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = "Can't delete a default variant.", Closed = () => reopen(n) });
            return;
        }
        shell.Modal(new ConfirmPopup
        {
            Lines = ["Sure you wanna delete", "this variant?"],
            Answer = yes =>
            {
                if (!yes) { reopen(n); return; }
                var r = data.DeleteVariant(model, n);
                if (r == VariantFiles.Result.Deleted)
                {
                    int left = m.Variants.Count;
                    shell.CarListsChanged(data);
                    deleted(n, left);
                    // the selection stays where it was, or moves to the variant that slid into its place
                    reopen(Math.Min(n, left));
                }
                else shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = "That variant is not one of yours to delete.", Closed = () => reopen(n) });
            },
        });
    }
}

/// <summary>
/// The melee and scenario forms' car panel tooltips (the equipment tooltips, the port's): the weapon and PARTS rows of
/// <see cref="MeleeScreen.DrawCar"/> name their parts; the part each name is (the parts database by display name) explains itself.
/// The lists are read once per car shown, not every frame.
/// </summary>
public sealed class CarPanelTips
{
    private (MeleeData Data, int Model, int Variant)? _for;
    private List<string> _weapons = [], _parts = [];

    /// <summary>The car shown may have changed under the same model and variant (a saved variant): read it again.</summary>
    public void Clear() => _for = null;

    public (string Text, Rect2I Zone)? At(Shell shell, MeleeData data, int model, int variant, Vector2I m)
    {
        if (shell.PartTips is not { } tips || model < 0 || model >= data.Models.Count) return null;
        if (_for is not { } f || f.Data != data || f.Model != model || f.Variant != variant)
        {
            _weapons = data.Weapons(model, variant);
            _parts = data.Parts(model, variant, shell.Parts);
            _for = (data, model, variant);
        }
        const int x = MeleeScreen.PanelX, w = MeleeScreen.PanelW, row = 17;
        if (m.X < x - 2 || m.X >= x + w) return null;
        (string, Rect2I)? Of(List<string> names, int y0, int[] types)
        {
            int k = m.Y >= y0 - 2 ? (m.Y - y0 + 2) / row : -1;
            if (k < 0 || k >= names.Count) return null;
            return tips.TextByName(shell.Parts, names[k], types) is { } t ? (t, new Rect2I(x - 2, y0 - 2 + k * row, w + 2, row)) : null;
        }
        // PARTS: the engine, the suspension, the brakes, then the tyres
        return Of(_weapons, MeleeScreen.PanelWeaponsY, [7, 8, 9]) ?? Of(_parts, MeleeScreen.PanelPartsY, [2, 3, 4, 5]);
    }
}