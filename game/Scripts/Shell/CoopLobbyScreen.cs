using Godot;
using I76.Formats.Input;
using I76.Formats.Shell;
using I76.Net;

namespace I76.Game.Shell;

/// <summary>
/// The co-op game's lobby (protocol 20, docs/multiplayer.md "The co-op lobby"): where the players of a hosted story mission gather before it
/// starts. It is the net melee's own ENTRY ROSTER form (record 18, the join form): the list box holds the players in the order they came, each with
/// a READY mark (the host's row says HOST), the box line above it and the car panel show the highlighted player's car, and the player's own row
/// has the form's MAKE / MODEL, VARIANT and CONFIGURE CHASSIS to change theirs (a co-op list begins with "Mission's own car"). The left button
/// is READY (NOT READY once marked), or START for the host (a confirmation when someone is not ready); CANCEL leaves. The parchment under the
/// list holds the chat (Enter types a line, Enter sends it, Esc drops it). The host boots the highlighted player with Delete. LOCAL DRIVER's
/// RENAME edits the player's name in place, kept as the form keeps it, and sends it to a server that takes it (the fork's).
/// Hosted in its own shell over the loading screen, as <see cref="ReconfigureScreen"/> is over the game; both looks use the form's art.
/// </summary>
public sealed class CoopLobbyScreen : ShellScreen
{
    /// <summary>The lobby as the server last sent it (a new object each time it changes); null before the first.</summary>
    public required Func<LobbyState?> State { get; init; }
    /// <summary>The chat lines not yet taken, as "name: text".</summary>
    public required Func<LobbyChat?> TakeChat { get; init; }
    /// <summary>This player's ready mark, and the car when it changes (a car file or <see cref="Protocol.MissionCar"/>, with its .vcf for an own variant).</summary>
    public required Action<bool, string?, byte[]?> Send { get; init; }
    public required Action<bool> Start { get; init; }
    public required Action<string> Chat { get; init; }
    /// <summary>The host boots this player (by name).</summary>
    public required Action<string> Boot { get; init; }
    public required Action Quit { get; init; }
    /// <summary>
    /// This player's new name to the server (<see cref="PeerFeatures.LobbyRename"/>): false when the server does not take one (an upstream
    /// host's lobby, where the name is the join's); null in a test.
    /// </summary>
    public Func<string, bool>? Rename { get; init; }
    /// <summary>Whether the server takes a new name (<see cref="Rename"/>).</summary>
    public Func<bool>? CanRename { get; init; }
    /// <summary>The model and variant to show for a car the lists do not have (the launch's model).</summary>
    public int FallbackModel { get; init; } = -1;
    /// <summary>
    /// The game's setup when this player hosts it in this process (a listen host: the fork's GAME SETUP; docs/multiplayer.md "Changing the game in
    /// the lobby"), null otherwise (a guest, a dedicated server's lobby): only the host has the button.
    /// </summary>
    public Func<I76.Server.LobbySetup?>? Setup { get; init; }
    /// <summary>The host's new setup to the server; the task's answer is null when made, else why not.</summary>
    public Func<I76.Server.LobbySetup, System.Threading.Tasks.Task<string?>>? ChangeSetup { get; init; }
    /// <summary>A co-op roguetrip's run as the host last told it (docs/remix-missions.md "Roguetrip in co-op"), null in any other game.</summary>
    public Func<RoguetripState?>? Roguetrip { get; init; }
    /// <summary>The route map's view of the run for this player (the host's choices to its server), opened by ROUTE MAP.</summary>
    public Func<RoguetripCoopView?>? RouteMap { get; init; }
    /// <summary>A co-op roguetrip's salvage draft and garage for this player (null until the host's draft has come).</summary>
    public Func<CoopSalvage?>? SalvageFlow { get; init; }
    public override bool EscapeOpensOptions => false;

    // the join form's hotspots (MeleeScreen's table): the list's arrows and rows, MAKE / MODEL, VARIANT, CONFIGURE CHASSIS, JOIN GAME, CANCEL and
    // LOCAL DRIVER's RENAME 14; 39 the host's GAME SETUP under the list, where the internet roster has its SERVERS
    private static readonly Dictionary<int, (int X0, int X1, int Y0, int Y1)> Hot = new()
    {
        [1] = (9, 27, 128, 144), [2] = (9, 27, 250, 268), [0] = (31, 216, 150, 270),
        [3] = (610, 621, 400, 415), [4] = (437, 449, 419, 434), [5] = (456, 616, 422, 435), [6] = (229, 369, 453, 471), [15] = (489, 630, 453, 471),
        [14] = (173, 213, 46, 57), [39] = (42, 183, 453, 471),
    };
    /// <summary>The host's change being made (the server builds the new mission), or null.</summary>
    private System.Threading.Tasks.Task<string?>? _change;
    private bool CanSetUp => State() is { IsHost: true } && Setup?.Invoke() is not null;
    /// <summary>A co-op roguetrip's ROUTE MAP button (everyone's: the host chooses on it, the others watch), where GAME SETUP is.</summary>
    private bool CanRoute => Roguetrip?.Invoke() is not null && RouteMap is not null;
    /// <summary>The run's phase last seen: the debrief and the run's end open the sheet by themselves, once each.</summary>
    private string? _routePhase;
    private const int Row = 17;
    private ShellFont _font = null!;
    private SpriteBank _cars = null!;
    private MeleeData _data = null!;
    private bool Nitro => Shell.Install.IsNitro;
    private int Rows => Nitro ? 5 : 6;
    private int FirstRowY => Nitro ? 171 : 149;
    private int BoxY => Nitro ? 130 : 109;

    private LobbyState? _seen;
    private int _scroll;
    private byte _selected;
    private bool _selectedSet;
    /// <summary>The car asked for and not yet answered (shown as the player's own meanwhile).</summary>
    private string? _asked;
    private (int Model, int Variant) _own = (0, 1);
    private readonly List<string> _chat = [];
    private bool _typing;
    private string _line = "";
    private string _notice = "";
    private bool _buttonBaked;
    /// <summary>RENAME is editing the name (<see cref="_name"/>); the name sent and not yet in a lobby state from the server.</summary>
    private bool _renaming;
    private string _name = "";
    private string? _renamedTo;

    public override void Enter()
    {
        _font = Shell.Db.Font(0x45);
        _cars = Nitro ? Shell.Db.BankNative(54) : Shell.Db.Bank(0x3c);
        _data = Shell.Melee;
        Repaint();
    }

    public override void Repaint()
    {
        Frame.LoadBackground(Shell.Db, 18);
        _buttonBaked = false;
        Draw();
    }

    private LobbyMember? Me => State()?.Me;

    /// <summary>The car file a member has on show: this player's own is what they asked for until the server answers.</summary>
    private string CarOf(LobbyMember m) => m.Id == State()?.You && _asked is { } a ? a : m.Car;

    private LobbyMember? Shown
    {
        get
        {
            var s = State();
            if (s is null || s.Members.Count == 0) return null;
            return s.Members.FirstOrDefault(m => _selectedSet && m.Id == _selected) ?? s.Me ?? s.Members[0];
        }
    }

    /// <summary>The model and variant of a car on show, or the last pick's for a car this list does not have.</summary>
    private (int Model, int Variant)? ModelOf(string file) =>
        file.Length == 0 || file == Protocol.MissionCar ? null : _data.Find(file) is { } f ? f : _data.Models.Count == 0 ? null : (Math.Clamp(FallbackModel, 0, _data.Models.Count - 1), 1);

    private void Draw()
    {
        var s = State();
        Frame.RestoreAll();
        if (s is null) return;
        BakeButton();
        Frame.Text(_font, 12, 45, _renaming ? _name + "_" : _renamedTo ?? s.Me?.Name ?? "");
        string title = Roguetrip?.Invoke() is { } rt ? RouteTitle(rt) : I76.Formats.Mission.MissionTitles.CoopTitle(Shell.Install, s.Mission);
        var sh = Shown;
        // the box line: the game, as the roster's top line shows the highlighted game
        Frame.Text(_font, 34, BoxY, AddressBookPopup.Fit(_font, $"{title} ({s.Members.Count}/{s.Max})", 176));
        int nx = Nitro ? 34 : 14;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, s.Members.Count - Rows));
        for (int k = 0; k < Rows && _scroll + k < s.Members.Count; k++)
        {
            var m = s.Members[_scroll + k];
            int y = FirstRowY + Row * k;
            string tag = m.Id == s.Host ? "HOST" : m.Ready ? "READY" : "";
            int tagW = tag.Length > 0 ? _font.Measure(tag) : 0;
            string name = (m.Id == sh?.Id ? "> " : "") + m.Name;
            Frame.Text(_font, nx, y, AddressBookPopup.Fit(_font, name, 203 - nx - tagW - 4));
            if (tag.Length > 0) Frame.Text(_font, 203 - tagW, y, tag);
        }
        // under the list: what the game waits for, and the chat
        string wait = CanRoute && Roguetrip!.Invoke() is { } rw ? (rw.Phase is "over" or "done" ? "Run over: ROUTE MAP" : s.IsHost ? "ROUTE MAP; START" : "Host: ROUTE MAP")
            : s.Host == LobbyState.NoHost
            ? (s.AutoMin > 1 ? $"Starts at {s.AutoMin}+ ready." : "Starts when all ready.")
            : s.IsHost ? (s.Members.Count > 1 && s.Members.All(m => m.Id == s.Host || m.Ready) ? "All ready: START." : "START when ready (Del boots).")
            : "The host starts the mission.";
        Frame.Text(_font, 14, 280, AddressBookPopup.Fit(_font, _notice.Length > 0 ? _notice : wait, 200));
        int rows = 8, top = 302;
        var shown = _chat.Skip(Math.Max(0, _chat.Count - rows)).ToList();
        for (int k = 0; k < shown.Count; k++) Frame.Text(_font, 14, top + 16 * k, AddressBookPopup.Fit(_font, shown[k], 200));
        // (a line higher with the host's GAME SETUP under it)
        Frame.Text(_font, 14, CanSetUp || CanRoute ? 432 : 440, AddressBookPopup.Fit(_font, _typing ? "> " + _line + "_" : "Enter: chat", 200));
        // the car panel: the highlighted player's
        if (sh is { } who)
        {
            string file = CarOf(who);
            bool story = file == Protocol.MissionCar;
            if (ModelOf(story ? s.OwnCar : file) is var (model, variant) && model >= 0)
                MeleeScreen.DrawCar(Frame, _font, _cars, _data, model, variant, Shell.Parts, story ? "Mission's own car" : null, story ? "the story's own" : null);
            else
            {
                Frame.Text(_font, 330, 399, "Mission's own car");
                Frame.Text(_font, 292, 421, "the story's own");
            }
            Frame.Text(_font, 288, 58, AddressBookPopup.Fit(_font, who.Name, 130));
        }
        DrawLeftButton(s);
        if (CanSetUp) DrawSetupButton();
        else if (CanRoute) DrawSetupButton("ROUTE MAP");
    }

    private (Pcx Art, Rect2I Border, ArtButtons.ArtLabel Word)? _cancelArt;

    /// <summary>
    /// GAME SETUP (the host's): the art's CANCEL button drawn again under the list without its word, and GAME SETUP lettered as the art letters
    /// CANCEL, as the internet roster's SERVERS is drawn on the same art in the same place.
    /// </summary>
    private void DrawSetupButton(string label = "GAME SETUP")
    {
        if (_cancelArt is null)
        {
            var bg = Shell.Db.Background(18);
            byte Px(int x, int y) => bg.Pixels[Math.Clamp(y, 0, bg.Height - 1) * bg.Width + Math.Clamp(x, 0, bg.Width - 1)];
            var c = Hot[15];
            if (ArtButtons.Label(Px, bg.Palette, new Rect2I(c.X0 - 4, c.Y0 - 4, c.X1 - c.X0 + 8, c.Y1 - c.Y0 + 8)) is not { } border
                || ArtButtons.Label(Px, bg.Palette, border.Box.Grow(-3)) is not { } word) return;
            _cancelArt = (bg, border.Box, word);
        }
        var (art, b, word0) = _cancelArt.Value;
        byte P(int x, int y) => art.Pixels[Math.Clamp(y, 0, art.Height - 1) * art.Width + Math.Clamp(x, 0, art.Width - 1)];
        var dest = new Rect2I(Hot[39].X0 + b.Position.X - Hot[15].X0, b.Position.Y, b.Size.X, b.Size.Y);
        ArtButtons.Face(Frame, P, b, word0.Box.Position.X, dest, 4);
        ArtButtons.Letter(Frame, Shell.Db.Font(0x47), label, word0, "CANCEL", dest);
    }

    /// <summary>
    /// GAME SETUP: the host form, as the game was hosted from it and set to the game as it stands, over the lobby (its own art); APPLY sends
    /// the new setup to the server, which builds it and tells everyone; the lobby comes back meanwhile.
    /// </summary>
    private void OpenSetup()
    {
        if (Setup?.Invoke() is not { } now || ChangeSetup is null) return;
        if (_change is { IsCompleted: false }) { _notice = "The last change is still being made."; return; }
        string car = Me is { } me ? CarOf(me) : Protocol.MissionCar;
        Shell.Overlay(null);
        Shell.Go(new MeleeScreen
        {
            Mode = 3, FromLobby = now, LobbyCar = car, LobbyDriver = Me?.Name,
            SetupDone = s =>
            {
                Back();
                if (s is null) return;
                _change = ChangeSetup(s);
                _notice = "Changing the game for everyone...";
                Draw();
            },
        });
    }

    // ---- a co-op roguetrip (docs/remix-missions.md "Roguetrip in co-op") ----

    /// <summary>The box line of a roguetrip's lobby: the run and where it is.</summary>
    private static string RouteTitle(RoguetripState rt) => rt.Phase switch
    {
        "debrief" => $"JOB {rt.Legs.Count} DONE",
        "over" or "done" => "RUN OVER",
        _ => $"ROGUETRIP #{Math.Min(I76.Formats.Remix.RemixRun.Jobs, rt.Legs.Count + 1)}",
    };

    /// <summary>ROUTE MAP: the roguetrip's sheet over the lobby (the choice, the debrief or the run's end), back to the lobby with Esc.</summary>
    internal void OpenRoute()
    {
        if (RouteMap?.Invoke() is not { } view || Roguetrip?.Invoke() is not { } st) return;
        _routePhase = st.Phase;
        view.Close = Back;
        Shell.Overlay(null);
        Shell.Go(new RoguetripScreen { Run = st.ToRun(view.Me()), CoopView = view });
    }


    /// <summary>A won job's salvage: the field salvage screen over the lobby, this player's garage after it; back to the lobby when it closes.</summary>
    internal void OpenSalvage(CoopSalvage co)
    {
        _routePhase = "salvage";
        co.Close = Back;
        Shell.Overlay(null);
        co.Changed();
        Shell.Go(new FieldSalvageScreen { Own = co.Inventory(Shell), Coop = co });
    }
    /// <summary>The debrief, or the run's end, has come since the sheet was last opened: it opens by itself on every game.</summary>
    private bool RouteDue()
    {
        if (!CanRoute || Roguetrip!.Invoke() is not { } st) return false;
        if (st.Phase == _routePhase) return false;
        if (st.Phase is "debrief" or "over" or "done") return true;
        // a won job's salvage: the draft's screens open on every game once the host's draft has come
        if (st.Phase == "salvage") return SalvageFlow?.Invoke() is not null;
        _routePhase = st.Phase;
        return false;
    }

    /// <summary>
    /// The form's JOIN GAME button says READY, NOT READY or START instead: its edges and a plain column of it stretched over the label
    /// (a change to the background, so an upscaled background is changed the same way).
    /// </summary>
    private void BakeButton()
    {
        if (_buttonBaked) return;
        _buttonBaked = true;
        const int x0 = 229, x1 = 370, y0 = 455, h = 14, edge = 4, plain = 238;
        for (int x = x0 + edge; x < x1 - edge; x++) Frame.CopyBackground(plain, y0, 1, h, x, y0);
    }

    /// <summary>The art's JOIN GAME border (the button the word is centred across) and CANCEL's word beside it (record 18's own pixels; ArtButtons).</summary>
    private (Rect2I Join, ArtButtons.ArtLabel Cancel)? _art;
    private bool _artRead;

    private (Rect2I Join, ArtButtons.ArtLabel Cancel)? Art()
    {
        if (_artRead) return _art;
        _artRead = true;
        var bg = Shell.Db.Background(18);
        byte Px(int x, int y) => bg.Pixels[Math.Clamp(y, 0, bg.Height - 1) * bg.Width + Math.Clamp(x, 0, bg.Width - 1)];
        Rect2I Around((int X0, int X1, int Y0, int Y1) r) => new(r.X0 - 4, r.Y0 - 4, r.X1 - r.X0 + 8, r.Y1 - r.Y0 + 8);
        if (ArtButtons.Label(Px, bg.Palette, Around(Hot[6])) is { } join && ArtButtons.Label(Px, bg.Palette, Around(Hot[15])) is { } cancel
            && ArtButtons.Label(Px, bg.Palette, cancel.Box.Grow(-3)) is { } word)
            _art = (join.Box, word);
        return _art;
    }

    /// <summary>
    /// The left button's word, lettered as the art letters CANCEL beside it (<see cref="ArtButtons.Letter"/>: Arial Bold as tall as CANCEL's
    /// word, on its line, as heavy, in its ink; at the window's resolution in the modern look, into the palette in the 1997 look), centred
    /// across the JOIN GAME button it replaces. Without the art's measure, or without Arial, the shell's label face doubled for weight.
    /// </summary>
    private void DrawLeftButton(LobbyState s)
    {
        string t = s.IsHost ? "START" : s.Me?.Ready == true ? "NOT READY" : "READY";
        var f = Shell.Db.Font(0x47);
        if (Art() is var (join, cancel)) { ArtButtons.Letter(Frame, f, t, cancel, "CANCEL", join); return; }
        int w = 370 - 229, tx = 229 + (w - f.Measure(t)) / 2, ty = 455 + (14 - f.Height) / 2 + 1;
        Frame.Text(f, tx, ty, t, ShellFrame.DefaultInk);
        Frame.Text(f, tx + 1, ty, t, ShellFrame.DefaultInk);
    }

    public override IReadOnlyList<PadRegion> PadRegions
    {
        get
        {
            var l = new List<PadRegion>();
            var s = State();
            int n = s is null ? 0 : Math.Min(Rows, s.Members.Count - _scroll);
            for (int k = 0; k < n; k++)
            {
                int kk = k;
                l.Add(PadRegion.Of(Hot[0].X0, FirstRowY + Row * k, Hot[0].X1 - Hot[0].X0, Row, d => d == NavDir.Up && kk == 0 ? Scroll(-1) : d == NavDir.Down && kk == n - 1 ? Scroll(1) : false));
            }
            if (s?.Me is not null && Shown?.Id == s.You) foreach (int id in new[] { 3, 4, 5 }) l.Add(PadRegion.Of(Hot[id].X0, Hot[id].Y0, Hot[id].X1 - Hot[id].X0, Hot[id].Y1 - Hot[id].Y0));
            foreach (int id in CanSetUp || CanRoute ? new[] { 14, 39, 6, 15 } : [14, 6, 15]) l.Add(PadRegion.Of(Hot[id].X0, Hot[id].Y0, Hot[id].X1 - Hot[id].X0, Hot[id].Y1 - Hot[id].Y0));
            return l;
        }
    }

    /// <summary>The mission's line over the list: its title and what it is about (the fork's; MissionTitles.CoopSummary), as the host form's list rows tell it.</summary>
    public override (string Text, Rect2I Zone)? TipAt(Vector2I p)
    {
        var rn = Hot[14];
        if (CanRename?.Invoke() == false && rn.X0 < p.X && p.X < rn.X1 && rn.Y0 < p.Y && p.Y < rn.Y1)
            return (NoRename, new Rect2I(rn.X0, rn.Y0, rn.X1 - rn.X0, rn.Y1 - rn.Y0));
        var g = Hot[39];
        if (CanSetUp && g.X0 < p.X && p.X < g.X1 && g.Y0 < p.Y && p.Y < g.Y1)
            return ("GAME SETUP (S): pick another mission or change the game's settings for everyone here; the players stay in the lobby.", new Rect2I(g.X0, g.Y0, g.X1 - g.X0, g.Y1 - g.Y0));
        if (State() is not { } s || p.X < 28 || p.X > 214 || p.Y < BoxY - 2 || p.Y > BoxY + 16) return null;
        string title = Roguetrip?.Invoke() is { } rt ? RouteTitle(rt) : I76.Formats.Mission.MissionTitles.CoopTitle(Shell.Install, s.Mission);
        string what = I76.Formats.Mission.MissionTitles.CoopSummary(Shell.Install, s.Mission, Shell.Vfs);
        return (what.Length > 0 ? $"{title}: {what}" : title, new Rect2I(28, BoxY - 2, 186, 18));
    }

    public override bool TextEditing => _typing || _renaming;
    public override void StopEditing() { _typing = false; _renaming = false; Draw(); }

    /// <summary>Why RENAME does nothing in this lobby: the host's game takes the name only with the join (an upstream build's).</summary>
    public const string NoRename = "This host's game takes your name only as you join: leave, RENAME on the form, and join again.";

    private bool Scroll(int by)
    {
        int max = Math.Max(0, (State()?.Members.Count ?? 0) - Rows), to = Math.Clamp(_scroll + by, 0, max);
        if (to == _scroll) return false;
        _scroll = to;
        Draw();
        return true;
    }

    private int HitId(Vector2I p) => Hot.Where(kv => kv.Value.X0 < p.X && p.X < kv.Value.X1 && kv.Value.Y0 < p.Y && p.Y < kv.Value.Y1).Select(kv => kv.Key).DefaultIfEmpty(-1).First();

    public override void Tick()
    {
        var s = State();
        bool redraw = false;
        if (!ReferenceEquals(s, _seen))
        {
            // the server's word is the car: what was asked for is given up (taken, it is the server's now; refused, Answer has said so)
            if (s is not null && _seen is not null && s.Me?.Car != _seen.Me?.Car) _asked = null;
            // and the name: the server's answer to a new one is a new state
            if (s is not null && _seen is not null) _renamedTo = null;
            _seen = s;
            if (s is not null && _own == (0, 1) && s.Me is { } me && ModelOf(me.Car) is { } mo) _own = mo;
            redraw = true;
        }
        while (TakeChat() is { } c) { AddChat($"{c.Name}: {c.Text}"); redraw = true; }
        // the host's change made (the server's lines say what changed), or why not
        if (_change is { IsCompleted: true } done)
        {
            _notice = done.IsCompletedSuccessfully && done.Result is { } why ? $"Not changed: {why}" : done.IsFaulted ? "Not changed: the server failed" : "";
            _change = null;
            redraw = true;
        }
        if (s is null) { if (redraw) Draw(); return; }
        if (_typing) { redraw |= Typing(s); if (redraw) Draw(); return; }
        if (_renaming) { redraw |= Renaming(); if (redraw) Draw(); return; }
        if (Shell.Keys.Contains(Key.Escape)) { Quit(); return; }
        if (Shell.Keys.Contains(Key.S) && CanSetUp) { Shell.Sound(82); OpenSetup(); return; }
        if (Shell.Keys.Contains(Key.R) && CanRoute) { Shell.Sound(82); OpenRoute(); return; }
        if (RouteDue()) { if (Roguetrip!.Invoke() is { Phase: "salvage" } && SalvageFlow?.Invoke() is { } sco) OpenSalvage(sco); else OpenRoute(); return; }
        if (Shell.Keys.Contains(Key.Enter) || Shell.Keys.Contains(Key.KpEnter)) { _typing = true; _line = ""; Draw(); return; }
        if (Shell.Keys.Contains(Key.Down)) { Select(s, 1); redraw = true; }
        if (Shell.Keys.Contains(Key.Up)) { Select(s, -1); redraw = true; }
        if (Shell.Keys.Contains(Key.Delete) && s.IsHost && Shown is { } victim && victim.Id != s.You) { Boot(victim.Name); _notice = $"{victim.Name} was booted."; redraw = true; }
        if (Shell.Wheel != 0) { _scroll = Math.Clamp(_scroll + Shell.WheelRows / 3, 0, Math.Max(0, s.Members.Count - Rows)); redraw = true; }
        if (Shell.Clicked)
        {
            int id = HitId(Shell.Mouse);
            if (id >= 0) { Shell.Sound(82); redraw |= Act(s, id); }
        }
        if (redraw) Draw();
    }

    private void AddChat(string line)
    {
        foreach (var l in NoticePopup.Wrap(_font, line, 200, 3)) _chat.Add(l);
        if (_chat.Count > 60) _chat.RemoveRange(0, _chat.Count - 60);
    }

    private bool Typing(LobbyState s)
    {
        bool changed = false;
        foreach (char c in Shell.Typed) if (_line.Length < Protocol.MaxChat) { _line += c; changed = true; }
        if (Shell.Keys.Contains(Key.Backspace) && _line.Length > 0) { _line = _line[..^1]; changed = true; }
        if (Shell.Keys.Contains(Key.Escape)) { _typing = false; _line = ""; return true; }
        if (Shell.Keys.Contains(Key.Enter) || Shell.Keys.Contains(Key.KpEnter))
        {
            _typing = false;
            if (_line.Trim().Length > 0) Chat(_line.Trim());
            _line = "";
            return true;
        }
        return changed;
    }

    /// <summary>
    /// RENAME's edit, as the form's (at most 24 characters): Enter or a click takes the name, Esc keeps the old one. A new name is kept as the
    /// form keeps it (melee.cfg's driver, what the next game's form opens with) and goes to the server, which tells everyone.
    /// </summary>
    private bool Renaming()
    {
        string was = _name;
        bool done = global::I76.Game.Shell.Typing.Edit(Shell, ref _name, 24);
        if (!done) return _name != was;
        _renaming = false;
        string name = _name.Trim();
        if (Shell.Keys.Contains(Key.Escape) || name.Length == 0 || name == (_renamedTo ?? Me?.Name)) return true;
        MeleeSetup.SaveDriver(name);
        if (Rename?.Invoke(name) == true) _renamedTo = name;
        else _notice = "Your next game's name.";
        return true;
    }

    private void Select(LobbyState s, int by)
    {
        int i = Math.Max(0, s.Members.Select((m, k) => (m, k)).FirstOrDefault(x => x.m.Id == Shown?.Id).k);
        i = Math.Clamp(i + by, 0, s.Members.Count - 1);
        _selected = s.Members[i].Id;
        _selectedSet = true;
        if (i < _scroll) _scroll = i;
        if (i >= _scroll + Rows) _scroll = i - Rows + 1;
    }

    private bool Act(LobbyState s, int id)
    {
        _notice = "";
        switch (id)
        {
            case 1: Scroll(-1); return false;
            case 2: Scroll(1); return false;
            case 0:
            {
                int k = (Shell.Mouse.Y - FirstRowY) / Row;
                if (Shell.Mouse.Y >= FirstRowY && k < Rows && _scroll + k < s.Members.Count) { _selected = s.Members[_scroll + k].Id; _selectedSet = true; }
                return true;
            }
            case 6:
                if (s.IsHost) { StartPressed(s); return false; }
                Send(s.Me?.Ready != true, null, null);
                return false;
            case 15: Quit(); return false;
            case 39 when CanSetUp: OpenSetup(); return false;
            case 14 when CanRename?.Invoke() == false:
                Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = NoRename, Closed = Draw });
                return false;
            case 14: _renaming = true; _name = _renamedTo ?? s.Me?.Name ?? ""; return true;
            case 39 when CanRoute: OpenRoute(); return false;
            case 39: return false;
            case 3 or 4 or 5 when Shown?.Id != s.You: _notice = "Highlight yourself to pick a car."; return true;
            case 3: ModelPopup(); return false;
            case 4:
                if (ModelOf(CarOf(s.Me!)) is not var (m4, v4)) { ModelPopup(); return false; }
                VariantPopups.Show(Shell, _font, _data, m4, v4, Nitro, v => PickCar(_data.Vcf(m4, v), m4, v),
                    (n, left) => { }, Repaint);
                return false;
            case 5:
            {
                if (ModelOf(CarOf(s.Me!)) is not var (m5, v5)) { ModelPopup(); return false; }
                Shell.Overlay(null);
                Shell.Go(ChassisForm.Create(Shell, new MeleeChassis(m5, v5, n => { PickCar(_data.Vcf(m5, n), m5, n); Back(); }, Back) { DoneAs = (nm, n) => { PickCar(_data.Vcf(nm, n), nm, n); Back(); } }));
                return false;
            }
        }
        return false;
    }

    private void Back()
    {
        Shell.Go(new HostScreen());
        Shell.Overlay(this);
    }

    private void StartPressed(LobbyState s)
    {
        if (s.Members.All(m => m.Id == s.Host || m.Ready)) { Start(false); return; }
        var waiting = s.Members.Where(m => m.Id != s.Host && !m.Ready).Select(m => m.Name).ToList();
        Shell.Modal(new ConfirmPopup
        {
            Lines = ["Not everyone is ready:", AddressBookPopup.Fit(_font, string.Join(", ", waiting), 170), "Start anyway?"],
            Answer = yes => { Draw(); if (yes) Start(true); },
        });
    }

    private void ModelPopup()
    {
        Shell.Modal(new ListPopup
        {
            Sprite = 7, NativeSprite = Nitro ? 6 : null, X = 206, Y = 122, TextX = 22, TextY = 48, Visible = 9,
            Items = [.. new[] { "Mission's own car" }, .. _data.Models.Select(m => m.MakeModel)], Font = _font,
            Up = new(18, 36, 12, 25), Down = new(18, 36, 204, 217), List = new(16, 214, 47, 198), Cancel = new(146, 215, 209, 225),
            Picked = i =>
            {
                if (i == 0) PickCar(Protocol.MissionCar, -1, 1);
                else if (i > 0) PickCar(_data.Vcf(i - 1, 1), i - 1, 1);
                Repaint();
            },
        });
    }

    private void PickCar(string file, int model, int variant)
    {
        if (file != Protocol.MissionCar) _own = (model, variant);
        Shell.Settings.CoopOwnCar = file == Protocol.MissionCar;
        Shell.Settings.Save();
        byte[]? config = file == Protocol.MissionCar ? null
            : I76.Formats.Vfs.AssetVfs.Addon.TryGetValue(file, out var own) ? own
            : Shell.Install.Resolve("addon", file) is { } path ? System.IO.File.ReadAllBytes(path) : null;
        _asked = file;
        Send(false, file, config);
    }

    /// <summary>The server's answer to a new car: taken, it is the player's; refused, the old one stays and the reason shows.</summary>
    public void Answer(ReconfigureAnswer a)
    {
        if (a.Taken) return;
        _asked = null;
        _notice = $"Refused: {a.Text}";
        Draw();
        Shell.Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Text = $"The host refused that car: {a.Text}", Closed = Draw });
    }
}
