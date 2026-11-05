using Godot;
using I76.Formats.Shell;
using I76.Formats.Vfs;

namespace I76.Game.Shell;

/// <summary>
/// What a shell launch hands the game: the mission,
/// the game kind, the player's car (the shell's vehscn.vcf; null keeps the
/// mission's own).
/// </summary>
public sealed record ShellLaunch(string Mission, GameKind Kind, string? Car = null)
{
    /// <summary>An instant melee's A.I. drivers (MULTICAR.def's slots 1..n).</summary>
    public IReadOnlyList<MeleeOpponent> Opponents { get; init; } = [];
    /// <summary>The trip car's state as the shell's vsf carries it (damage between missions).</summary>
    public I76.Sim.Trip.CarState? State { get; init; }
    /// <summary>"No Salv. Manag.": no salvage pool in the mission.</summary>
    public bool NoSalvage { get; init; }
    /// <summary>The driver's name and the car's MAKE/MODEL (config +0x0e / +0x2e): the standings row's text, and the model's list index for victory.rec.</summary>
    public string Driver { get; init; } = "Unnamed";
    public string Model { get; init; } = "";
    public int ModelIndex { get; init; } = -1;
    /// <summary>A network melee (kind 5): host it or join it.</summary>
    public NetLaunch? Net { get; init; }
}

/// <summary>
/// A network melee from the MULTI MELEE forms (melee modes 3/4 host, 7/8
/// join): a listen server in the game's process on <see cref="Port"/> with the
/// host form's rules, or a join of the game at <see cref="Connect"/>. The
/// mission is the arena; the car and driver are the launch's.
/// </summary>
public sealed record NetLaunch(int Mode)
{
    /// <summary>host:port to join; null to host.</summary>
    public string? Connect { get; init; }
    public int Port { get; init; } = I76.Net.Protocol.DefaultPort;
    public string GameName { get; init; } = "";
    /// <summary>NO. DRIVERS: the seats.</summary>
    public int MaxPlayers { get; init; } = 4;
    public bool TeamPlay { get; init; }
    public bool InviteOnly { get; init; }
    /// <summary>Hosting: the game's password (blank is open); joining: the one to join with. Never saved.</summary>
    public string Password { get; init; } = "";
    /// <summary>Hosting: the host form's WEAPON RESTRICTIONS (docs/hidden-content.md), enforced by the server.</summary>
    public I76.Sim.Trip.WeaponRestrictions Restrictions { get; init; } = I76.Sim.Trip.WeaponRestrictions.None;
    /// <summary>Hosting: Hidden Weapons &amp; Cars, the host's setting, which decides for the game.</summary>
    public bool Hidden { get; init; }
    /// <summary>Hosting: Unlim. Turrets, the host form's option: cars may carry several roof turrets in this game.</summary>
    public bool Turrets { get; init; }
    /// <summary>The score to end on, 0 for none (MAX SCORE off).</summary>
    public int MaxScore { get; init; }
    /// <summary>CHOOSE A TEAM's pick, 1–8 (0 without team play).</summary>
    public int Team { get; init; }
    /// <summary>Nitro's arena kinds: the rules (combat, race, CTF), what ends the game, its value, and the CTF teams (2-4). Null: the arena's own.</summary>
    public I76.Sim.Missions.MeleeKind? Kind { get; init; }
    public I76.Sim.Missions.MeleeGoal Goal { get; init; }
    public int GoalValue { get; init; }
    public int CtfTeams { get; init; } = 2;
    /// <summary>A minutes limit (0 none): the game ends on the clock.</summary>
    public int Minutes { get; init; }
    /// <summary>Joining a game listed on an anet server: the server ("host" or "host:port") and the game's record there (the join goes through its introduction and the hole punch).</summary>
    public string? AnetServer { get; init; }
    public I76.Net.Anet.AnetSession? AnetGame { get; init; }
    /// <summary>Hosting: the anet server to list the game on ("host[:port]"); null: not listed.</summary>
    public string? ListOn { get; init; }
    /// <summary>Hosting: a custom arena is sent to players who lack it (the form's Share Maps).</summary>
    public bool ShareMaps { get; init; } = true;
    /// <summary>Hosting: players may reset their car upright (the form's Car Reset; on, as the original's F12 trick worked online).</summary>
    public bool CarReset { get; init; } = true;
    /// <summary>Hosting: nobody switches their engine off in the game (the form's IGN. LOCK; off).</summary>
    public bool IgnitionStaysOn { get; init; }
    /// <summary>Hosting: the CB radio (voice chat) is on in the game (the form's CB Radio; on).</summary>
    public bool Cb { get; init; } = true;
    /// <summary>Hosting: the Handicaps rule (the form's Hcp; off): Manual, the host sets each player's level from the score window; Auto, the game sets them from the standings.</summary>
    public I76.Sim.Missions.HandicapMode Handicaps { get; init; }
    /// <summary>Hosting: the chase crosshair may show for the players who have it on (the form's Chase Crosshair; off).</summary>
    public bool Crosshair { get; init; }
    /// <summary>Hosting: the form's Bots, players the server drives (up to the free seats), and how well.</summary>
    public int Bots { get; init; }
    public I76.Sim.Missions.BotSkill BotSkill { get; init; } = I76.Sim.Missions.BotSkill.Normal;
    /// <summary>Hosting: the form's A.I. CARS, the car file of each bot seat (empty: Random).</summary>
    public IReadOnlyList<string> BotCars { get; init; } = [];
    /// <summary>Hosting: the game's simulation rate in Hz, the host form's choice (0: the Play Options' setting); everyone in the game plays at it.</summary>
    public int Rate { get; init; }
    /// <summary>The co-op form's game, a story mission (the launch's mission) for up to <see cref="MaxPlayers"/> (1-8) players: hosting it, or joining one (its briefing plays first).</summary>
    public bool Coop { get; init; }
    /// <summary>A co-op game's seat in the mission's own car (the launch's car is not used); joining a melee, ignored.</summary>
    public bool StoryCar { get; init; }
    /// <summary>Hosting a co-op game: the form's Co-op Scaling, the most extra copies of each enemy car (0 off).</summary>
    public int CoopScale { get; init; }
    /// <summary>Hosting a co-op game: the form's Co-op Damage, the factor on what the players' cars take (1, the original's).</summary>
    public float CoopDamage { get; init; } = 1f;
    /// <summary>Hosting a co-op game: the form's Rolling Start, a respawn or joiner at a moving teammate's speed (off: standing still).</summary>
    public bool CoopRollingStart { get; init; }
    /// <summary>Joining a co-op game the roster showed in its lobby: the briefing plays when the host starts the mission, not before the join. Hosting a co-op game always opens the lobby.</summary>
    public bool InLobby { get; init; }
}

/// <summary>One line of a network melee's STANDINGS: the driver, their car file (the screen names its model), deaths, kills, score.</summary>
public sealed record StandingRow(string Driver, string Car, int Deaths, int Kills, int Score, int Team = 0);

/// <summary>What a network melee hands back: the last scoreboard, and why it ended if it did not end well (a refused join, a lost server).</summary>
public sealed record NetResult(IReadOnlyList<StandingRow> Rows, string? Error, bool TeamPlay = false, I76.Sim.Missions.MeleeKind Kind = I76.Sim.Missions.MeleeKind.Combat);

/// <summary>A melee's or scenario's outcome: NSCR.DAT's row (driver, model, deaths, kills, score), whether it ended in state 1, and the car's built mass (victory.rec's weight).</summary>
public sealed record MeleeResult(string Driver, string Model, int Deaths, int Kills, int Score, bool Won, float Mass);

/// <summary>What a won trip mission hands back (the engine's vsf): the car's state (null after T12, which writes nothing) and the field salvage.</summary>
public sealed record TripResult(I76.Sim.Trip.CarState? State, List<I76.Sim.Trip.SalvageItem>? Salvage);

/// <summary>One A.I. driver of an instant melee: the name on the form and the car (a model's variant .vcf).</summary>
public sealed record MeleeOpponent(string Name, string Car);

/// <summary>The player-state block's +0x20, as the engine reads it.</summary>
public enum GameKind { Trip = 2, AutoMelee = 3, Scenario = 4, Multiplayer = 5 }

/// <summary>How the last mission ended, as the engine reports it to the shell.</summary>
public enum MissionResult { Failed = 0, Won = 1, Quit = 2, FirstEntry = 5, Reconfigure = 8 }

/// <summary>
/// The front end: the
/// 640x480 frame shown letterboxed at 4:3, one screen at a time with the
/// overlays (the Joe's Fish Shack notepad) over it, the menu music, and the
/// hand-over to a mission. Clicks act on press, as everywhere in the shell.
/// </summary>
public partial class Shell : CanvasLayer
{
    public required GameInstall Install { get; init; }
    /// <summary>How the last mission ended and what it was, to pick the first screen (the original front end's main loop).</summary>
    public MissionResult LastResult { get; init; } = MissionResult.FirstEntry;
    public ShellLaunch? LastLaunch { get; init; }
    /// <summary>Starts the game: the controller reloads the scene into the mission.</summary>
    public required Action<ShellLaunch> OnLaunch { get; init; }
    /// <summary>The 1997 look: the frame sampled nearest.</summary>
    public bool Faithful { get => _faithful; init => _faithful = value; }
    private bool _faithful;
    /// <summary>
    /// The port's in-mission host (the escape menu's Control Configuration): a shell with no front end, only this overlay over a blank
    /// screen, on top of the paused game; <see cref="Options"/> (the overlay's way back to the Options Menu) ends it through <see cref="HostDone"/>.
    /// </summary>
    public ShellScreen? Hosted { get; init; }
    /// <summary>The network host form being open with its UNLIM. TURRETS on: the garages it opens lift the one-turret rule for the game it hosts.</summary>
    public bool HostTurrets { get; set; }
    /// <summary>Whether the garages let a car carry several roof turrets: the Play Option, or the host form's option for its game.</summary>
    public bool UnlimTurrets => Settings.UnlimTurrets || HostTurrets;
    public Action? HostDone { get; init; }
    /// <summary>The canvas layer a hosted shell is on (over the game: 51); the co-op lobby is over the loading screen (100).</summary>
    public int? HostLayer { get; init; }

    /// <summary>Back to the Options Menu — or, hosted in a mission, back to the escape menu.</summary>
    public void Options()
    {
        if (Hosted is not null) HostDone?.Invoke();
        else Overlay(new OptionsScreen());
    }

    public ShellDatabase Db { get; private set; } = null!;
    public ShellFrame Frame { get; } = new();
    private Label? _build;
    private Control? _updateLink;
    public TripState Trip { get; private set; } = null!;
    public ShellSettings Settings { get; private set; } = null!;
    private I76.Formats.Vfs.AssetVfs? _vfs;
    /// <summary>The game's files, opened on first use.</summary>
    public I76.Formats.Vfs.AssetVfs Vfs => _vfs ??= new I76.Formats.Vfs.AssetVfs(Install);
    private MeleeData? _melee;
    /// <summary>The melee forms' models and lists, read from the install on first use, and again when Hidden Weapons &amp; Cars changes.</summary>
    public MeleeData Melee
    {
        get
        {
            if (_melee is { } m && m.Hidden == Settings.HiddenContent) return m;
            return _melee = Install.IsNitro ? new MeleeData(Vfs, NitroLists.MeleeCars(Install), NitroAddon, Settings.HiddenContent) : new MeleeData(Vfs, Settings.HiddenContent);
        }
    }
    /// <summary>Where Nitro's saved chassis variants go (the original's addon\&lt;stem&gt;&lt;n&gt;.vcf), apart from the base game's.</summary>
    public const string NitroAddon = "user://nitro/addon";
    private MeleeData? _scenarioCars;
    /// <summary>Nitro's scenario cars (nitscar.def, 31).</summary>
    public MeleeData ScenarioCars => _scenarioCars ??= new MeleeData(Vfs, NitroLists.ScenarioCars(Install), NitroAddon);
    /// <summary>A variant was deleted from one list: the other Nitro list (which reads the same folder) is read again on next use.</summary>
    public void CarListsChanged(MeleeData keep) { if (_melee != keep) _melee = null; if (_scenarioCars != keep) _scenarioCars = null; }
    private NitroProgress? _progress;
    /// <summary>Nitro's campaign progress (user://saves/nitro/scenario.dat).</summary>
    public NitroProgress NitroProgress => _progress ??= NitroProgress.Load(Install);
    private List<(string Name, string Msn)>? _arenas;
    /// <summary>Nitro's combat arenas for the instant melee.</summary>
    public List<(string Name, string Msn)> NitroArenas => _arenas ??= NitroLists.CombatArenas(Install);
    private List<(string Name, string Msn)>? _baseArenas;
    /// <summary>Base I76's arenas: the fifteen, then the addon\*.lvl levels.</summary>
    public List<(string Name, string Msn)> BaseArenas => _baseArenas ??= NitroLists.BaseArenas(Install);
    private I76.Sim.Trip.PartsDatabase? _parts;
    /// <summary>The parts database (callback [0]).</summary>
    public I76.Sim.Trip.PartsDatabase Parts => _parts ??= I76.Sim.Trip.PartsDatabase.Build(Vfs);
    /// <summary>What the last won trip mission handed back, applied on entry.</summary>
    public TripResult? LastTrip { get; init; }
    /// <summary>What the last melee or scenario handed back.</summary>
    public MeleeResult? LastMelee { get; init; }
    /// <summary>What the last network melee handed back.</summary>
    public NetResult? LastNet { get; init; }
    /// <summary>Why the last launch could not be built, or null.</summary>
    public string? LoadError { get; init; }
    private readonly Random _rand = new();
    /// <summary>The shell's rand().</summary>
    public int Rand() => _rand.Next(0x8000);
    /// <summary>Opens the plain mission list (the melee forms are not ported yet).</summary>
    public Action? OnMissionList { get; init; }
    /// <summary>Opens the map editor's map list (the main menu's Map Editor entry, F8).</summary>
    public Action? OnEditor { get; init; }
    public void OpenEditor() => OnEditor?.Invoke();
    /// <summary>--dev or I76_DEV=1 for this run; the setting (<see cref="ShellSettings.DeveloperTools"/>) turns the panel on as well.</summary>
    public bool Developer { get; init; }
    private CanvasLayer? _devPanel;

    /// <summary>The main menu's Developer panel (<see cref="ShellDevPanel"/>), or closes it.</summary>
    private void ToggleDevPanel()
    {
        if (_devPanel is not null) { _devPanel.QueueFree(); _devPanel = null; return; }
        _devPanel = new CanvasLayer { Layer = Layer + 1, Name = "DevPanel" };
        _devPanel.AddChild(new ShellDevPanel { Shell = this, Close = ToggleDevPanel });
        AddChild(_devPanel);
    }
    /// <summary>Seconds since the last tick.</summary>
    public double Delta { get; private set; }
    /// <summary>In a trip: the Options Menu offers Save Bookmark and Exit Trip (block +0x20 == 2).</summary>
    public bool InTrip => _screen is TripScreen and not GarageScreen { Melee: not null };   // a melee chassis form is not the trip (block +0x20 is 3 or 4 there)
    /// <summary>The screen under the overlays.</summary>
    public ShellScreen? Underneath => _screen;
    /// <summary>The one being ticked: a modal popup, else the overlay, else the screen.</summary>
    public ShellScreen? Current => _modal ?? _overlay ?? _screen;

    /// <summary>The mouse in shell coordinates, and whether its button went down since the last tick.</summary>
    public Vector2I Mouse { get; private set; }
    public bool Clicked { get; private set; }
    /// <summary>Mouse wheel notches this frame (positive: up), for the scrolling lists.</summary>
    public int Wheel { get; private set; }
    /// <summary>The rows a list scrolls this frame (positive: down; three a notch, as the list pop-ups do).</summary>
    public int WheelRows => -Math.Sign(Wheel) * 3;
    private int _wheelPending;
    private bool _clickPending, _rightPending, _scriptHeld;
    /// <summary>The left button is down (for the arrows that repeat while held; a capture's "hold:" counts).</summary>
    public bool MouseHeld => _scriptHeld || Input.IsMouseButtonPressed(MouseButton.Left);
    /// <summary>A right click this tick (Control Configuration's capture).</summary>
    public bool RightClicked { get; private set; }
    /// <summary>When set, the next key or mouse button goes here (by the original's input name) instead of to the screens.</summary>
    public Action<string?>? Capture { get; set; }
    private readonly Queue<Key> _keys = new();
    /// <summary>Keys pressed since the last tick.</summary>
    public IEnumerable<Key> Keys => _keys;
    private readonly System.Text.StringBuilder _typed = new();
    /// <summary>Printable characters typed since the last tick (for the text fields).</summary>
    public string Typed { get; private set; } = "";

    private AudioStreamPlayer _voice = null!;

    /// <summary>A voice record played async (callback 0x2c), replacing any voice still playing.</summary>
    public void Voice(int record)
    {
        if (I76.Formats.Audio.SoundFiles.Parse(Db.Wave(record)) is not { } pcm || Assets.AudioBank.ToStream(pcm) is not { } s) return;
        _voice.Stream = s;
        _voice.Play();
    }

    public void StopVoice() => _voice.Stop();

    /// <summary>Opening Options re-reads the settings file: unsaved edits are gone, the shell and the mission agree.</summary>
    public void ReloadSettings()
    {
        Settings = ShellSettings.Load();
        I76.Game.Assets.MaterialLibrary.ApplyCarInteriors(Settings.CarInteriors);
        I76.Game.Assets.MaterialLibrary.ApplyHoodGloss(Settings.HoodGloss);
        I76.Game.MouseCamera.Confine = Settings.KeepMouseInWindow;
        I76.Game.Sim.ModernFx.ApplyLook(Settings);
        ApplyMusicLevel();
        I76.Game.Boot.Main.ApplyAudio(Settings);   // the SFX and voice levels Audio Control set live go back too
    }
    public bool VoicePlaying => _voice.Playing;

    private TextureRect _view = null!;
    /// <summary>The Impact labels drawn over the frame, or null in the 1997 look.</summary>
    public ImpactLayer? Impact { get; private set; }
    /// <summary>The upscaled backgrounds under the frame and its text over it at the window's resolution, or null in the 1997 look.</summary>
    private ShellBackdrop? _backdrop;
    private ShellTextLayer? _text;
    /// <summary>Whether the background shown is an upscale (record 16's then has no action names: the screen prints them).</summary>
    public bool Upscaled => _backdrop?.Has(Frame.Record, Frame.Native) == true;
    private readonly Dictionary<int, bool[]?> _kept = [];

    /// <summary>
    /// Where a background that borrows another's upscale differs from that one's
    /// original (by colour, grown by 2 px for the upscale's softer edges): those
    /// pixels stay the frame's own. Null when it shows its own upscale.
    /// </summary>
    private bool[]? Kept(int record, int native)
    {
        if (_backdrop is null || _backdrop.Source(record, native) is var src && (src == record || src <= 0)) return null;
        if (_kept.TryGetValue(record, out var k)) return k;
        Pcx a = Db.Background(record), b = Db.Background(src);
        const int W = ShellFrame.W, H = ShellFrame.H, Grow = 2;
        var diff = new bool[W * H];
        for (int y = 0; y < Math.Min(H, Math.Min(a.Height, b.Height)); y++)
            for (int x = 0; x < Math.Min(W, Math.Min(a.Width, b.Width)); x++)
            {
                int i = a.Pixels[y * a.Width + x] * 3, j = b.Pixels[y * b.Width + x] * 3;
                diff[y * W + x] = a.Palette[i] != b.Palette[j] || a.Palette[i + 1] != b.Palette[j + 1] || a.Palette[i + 2] != b.Palette[j + 2];
            }
        k = new bool[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!diff[y * W + x]) continue;
                for (int yy = Math.Max(0, y - Grow); yy <= Math.Min(H - 1, y + Grow); yy++)
                    for (int xx = Math.Max(0, x - Grow); xx <= Math.Min(W - 1, x + Grow); xx++) k[yy * W + xx] = true;
            }
        return _kept[record] = k;
    }
    private ImageTexture? _tex;
    private ShellScreen? _screen, _overlay, _modal;
    private readonly Dictionary<int, AudioStreamWav?> _sounds = [];
    private AudioStreamPlayer _sfx = null!, _music = null!;

    public override void _Ready()
    {
        Layer = Hosted is null ? 50 : HostLayer ?? 51;
        if (Hosted is not null) ProcessMode = ProcessModeEnum.Always;
        Db = ShellDatabase.Open(Install.FindFile("DATABASE.MW2") ?? Install.FindFile("database.tvf") ?? throw new FileNotFoundException("DATABASE.MW2 or database.tvf"));
        Trip = TripState.Load();
        ShellSettings.Nitro = Install.IsNitro;
        Settings = ShellSettings.Load();
        I76.Game.Assets.MaterialLibrary.ApplyCarInteriors(Settings.CarInteriors);
        I76.Game.Assets.MaterialLibrary.ApplyHoodGloss(Settings.HoodGloss);
        // the menus keep the pointer in the window too (Main's cursor state reads it every frame)
        I76.Game.MouseCamera.Confine = Settings.KeepMouseInWindow;
        I76.Game.Sim.ModernFx.ApplyLook(Settings);
        AddChild(new ColorRect { Color = Colors.Black, AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore });
        _view = new TextureRect
        {
            AnchorRight = 1, AnchorBottom = 1, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_view);
        ApplyLook();
        AddChild(_focusLayer = new PadFocusLayer { Shell = this, Name = "PadFocus" });
        // the version, small and dim, in the main menu's lower right corner
        _build = new Label { Text = I76.Game.Boot.BuildInfo.Line, Name = "Build", MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right, Modulate = new Color(1, 1, 1, 0.35f) };
        _build.AddThemeFontSizeOverride("font_size", 11);
        _build.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize);
        _build.GrowHorizontal = Control.GrowDirection.Begin;
        _build.OffsetLeft = -300; _build.OffsetRight = -6; _build.OffsetTop = -20; _build.OffsetBottom = -4;
        AddChild(_build);
        AddChild(_updateLink = I76.Game.Boot.UpdateNotice.Link());
        I76.Game.Boot.UpdateNotice.Start(this);
        AddChild(_sfx = new AudioStreamPlayer { Name = "Click" });
        AddChild(_voice = new AudioStreamPlayer { Name = "Voice" });
        AddChild(_music = new AudioStreamPlayer { Name = "Music", VolumeDb = -8 });
        if (Hosted is not null)
        {
            Go(new HostScreen());
            Overlay(Hosted);
            return;
        }
        PlayMusic(13);
        ShellScreen first;
        try { TestThrow("FirstScreen"); first = FirstScreen(); }
        catch (Exception ex) { Recover(ex, "the way back from the mission"); return; }
        Go(first);
        // on the Compatibility renderer (no Vulkan or D3D12 on this machine), once a run: what is different
        if (I76.Game.Boot.RenderBackend.Compatibility && Settings.Renderer != I76.Game.Boot.RendererChoice.OpenGL && !I76.Game.Boot.RenderBackend.NoticeShown)
        {
            I76.Game.Boot.RenderBackend.NoticeShown = true;
            Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Closed = () => { _screen?.Repaint(); FontsNotice(); }, Text = I76.Game.Boot.RenderBackend.Notice });
        }
        else FontsNotice();
    }

    /// <summary>
    /// Once per user (a marker in user://; I76_FONTS_MISSING forces it every time and leaves no marker): Arial or Impact is not installed and not in the
    /// HD Menu Pack, so the menus draw a stand-in or the original sprites. Says how to get them.
    /// </summary>
    private void FontsNotice()
    {
        const string marker = "user://fonts_notice.done";
        bool forced = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("I76_FONTS_MISSING"));
        if (Hosted is not null || (!forced && Godot.FileAccess.FileExists(marker))) return;
        var missing = HdAssets.MissingFonts();
        if (missing.Count == 0) return;
        if (!forced)
        {
            using var m = Godot.FileAccess.Open(marker, Godot.FileAccess.ModeFlags.Write);
            m?.StoreString("1");
        }
        GD.Print(HdAssets.FontsAdvice(missing));
        Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Closed = () => _screen?.Repaint(), Text = HdAssets.FontsNotice(missing) });
    }

    /// <summary>
    /// Builds or drops the layers of the modern look for <see cref="Faithful"/>: the upscaled backgrounds and sprites under the frame, the
    /// text and the main menu's Impact labels over it, and the frame's own filter. The frame's pixels stay; the screen paints itself again.
    /// </summary>
    private void ApplyLook()
    {
        foreach (var n in new Node?[] { _backdrop, _sprites, Impact, _text }) if (n is not null) { RemoveChild(n); n.QueueFree(); }
        _backdrop = null; _sprites = null; Impact = null; _text = null;
        _view.TextureFilter = Faithful ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear;
        if (Faithful) { Frame.HdFonts = null; Frame.HasHdSprite = null; return; }
        AddChild(_backdrop = new ShellBackdrop { Name = "Backdrop" });
        // the strings in Lee and Arial from now on recorded, not blitted (ShellText.cs)
        ShellGlyphs.Load(Install.FindFile("Lee_____.ttf"));
        Frame.HdFonts = f => ShellGlyphs.Face(f.Record) is not null;
        // the popups' sprites at the window's resolution, over the backgrounds (the frame hides their pixels)
        AddChild(_sprites = new ShellSprites { Frame = Frame, Nitro = Install.IsNitro, Name = "Sprites" });
        MoveChild(_backdrop, 1);
        MoveChild(_sprites, 2);
        // the main menu's Impact labels at the window's resolution (the modern look; 1997 keeps the sprites)
        AddChild(Impact = new ImpactLayer { Name = "Impact" });
        AddChild(_text = new ShellTextLayer { Frame = Frame, Name = "Text" });
        MoveChild(Impact, _view.GetIndex() + 1);
        MoveChild(_text, _view.GetIndex() + 2);
    }

    /// <summary>The layer of the upscaled sprites (null in the 1997 look): a screen with a 3D view under the frame lifts it over that view.</summary>
    public Control? SpritesLayer => _sprites;
    private ShellSprites? _sprites;

    /// <summary>
    /// F12 on a screen that allows it (<see cref="ShellScreen.SwitchesLook"/>): the shell in the other look, kept with the settings as the
    /// game's own F12 keeps it. The screen is told (<see cref="ShellScreen.LookChanged"/>) and repaints.
    /// </summary>
    public void SwitchLook()
    {
        _faithful = !_faithful;
        ApplyLook();
        var ls = ShellSettings.Load();
        ls.Look1997 = Faithful;
        ls.Save();
        Settings.Look1997 = Faithful;
        _screen?.LookChanged();
        (_overlay ?? _screen)?.Repaint();
    }

    private bool _recovering;
    private int _recoveries;

    /// <summary>
    /// A screen that threw (a bug of the port's): logged, and the main menu comes up with a notice saying so, instead of
    /// the shell's black backdrop with no screen on it and nothing to press, which only closing the game got out of.
    /// </summary>
    private void Recover(Exception ex, string where)
    {
        GD.PrintErr($"shell: {where} failed: {ex}");
        // the main menu itself failing (or failing again and again): there is nothing better to go to
        if (_recovering || ++_recoveries > 5) return;
        _recovering = true;
        try
        {
            _modal = null;
            _overlay = null;
            // hosted over a mission (Control Configuration, Reconfigure): no front end to fall back to, the host closes
            if (Hosted is not null) { HostDone?.Invoke(); return; }
            Go(new MainMenuScreen());
            Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Closed = () => _screen?.Repaint(),
                Text = "SOMETHING WENT WRONG THERE (DETAILS IN THE LOG). LOAD A BOOKMARK TO GO ON." });
        }
        catch (Exception again) { GD.PrintErr($"shell: the main menu failed too: {again}"); }
        finally { _recovering = false; }
    }

    /// <summary>
    /// A pop-up that threw (ticking, or opening): it is closed and the screen under it stays, with a notice, rather than the
    /// whole front end going back to the main menu (or a pop-up left up that nothing closes).
    /// </summary>
    private void DropModal(Exception ex)
    {
        var bad = _modal;
        GD.PrintErr($"shell: the pop-up {bad?.GetType().Name} failed: {ex}");
        if (++_recoveries > 5) { Recover(ex, $"{bad?.GetType().Name}"); return; }
        _modal = null;
        try { bad?.Leave(); } catch (Exception again) { GD.PrintErr($"shell: closing {bad?.GetType().Name} failed: {again}"); }
        try
        {
            (_overlay ?? _screen)?.Repaint();
            Modal(new NoticePopup { Sprite = 17, Ok = new Rect2I(119, 61, 42, 16), Closed = () => (_overlay ?? _screen)?.Repaint(),
                Text = "SOMETHING WENT WRONG THERE (DETAILS IN THE LOG)." });
        }
        catch (Exception again) { Recover(again, "the screen under a failed pop-up"); }
    }

    /// <summary>The original front end's choice of the first screen from the last result.</summary>
    private ShellScreen FirstScreen()
    {
        // test hook: I76_SHELL_START=standings:combat|race|ctf:team|solo shows STANDINGS over made-up rows
        if (System.Environment.GetEnvironmentVariable("I76_SHELL_START") is { } st && st.StartsWith("standings:"))
        {
            var a = st.Split(':');
            var kind = a[1] switch { "race" => I76.Sim.Missions.MeleeKind.Race, "ctf" => I76.Sim.Missions.MeleeKind.Ctf, _ => I76.Sim.Missions.MeleeKind.Combat };
            StandingRow[] rows = [new("Ann", "", 2, 5, 900, 1), new("Bob", "", 1, 3, 400, 2), new("Cy", "", 4, 1, 300, 1), new("Di", "", 0, 6, 1200, 2), new("Ed", "", 3, 2, 100, 3)];
            return new StandingsScreen { Rows = rows, TeamPlay = a.Length > 2 && a[2] == "team", Kind = kind };
        }
        // test hook: I76_SHELL_START=melee:N opens that melee form (3 is the LAN host form)
        if (System.Environment.GetEnvironmentVariable("I76_SHELL_START") is { } ms && ms.StartsWith("melee:"))
            return new MeleeScreen { Mode = int.Parse(ms[6..]) };
        // test hook: I76_SHELL_START=garage:MODEL:VARIANT opens the melee chassis form (the 3D garage) on that model and variant (0 and 1 based)
        if (System.Environment.GetEnvironmentVariable("I76_SHELL_START") is { } gs && gs.StartsWith("garage:") && gs.Split(':') is [_, var gm, var gv]
            && int.TryParse(gm, out int gmi) && int.TryParse(gv, out int gvi))
            return ChassisForm.Create(this, new MeleeChassis(gmi, gvi, n => { }, () => { }));
        // test hook: I76_SHELL_START=playoptions opens the Play Options sheet
        if (System.Environment.GetEnvironmentVariable("I76_SHELL_START") == "playoptions") return new PlayOptionsScreen();
        // test hook: I76_SHELL_START=router comes up as if the trip's current mission had just been won
        if (System.Environment.GetEnvironmentVariable("I76_SHELL_START") == "router")
        {
            // with the finished mission's .fsi salvage, as a win with nothing killed hands back
            var pool = new I76.Sim.Trip.SalvagePool(Trip.Stem, (Settings.PlayOptions & 2) != 0);
            var items = pool.Finish(Vfs.TryRead, Rand);
            Trip.Won();
            if (Trip.Inventory(this) is { } inv) { inv.MissionWon(null, items, Parts, Rand); Trip.Save(); }
            return new TripRouterScreen();
        }
        // Nitro: a won campaign mission marks itself done (not with cheats: that is state 0xb, which gets the end menu instead of a win)
        // and the scenario form comes back; a lost or quit one comes back with nothing marked
        if (Install.IsNitro && LastLaunch is { Kind: GameKind.Scenario } nl0)
        {
            if (LastResult == MissionResult.Won) NitroProgress.MarkDone(nl0.Mission);
            int ch = NitroProgress.Data.Characters.FindIndex(c => c.Missions.Any(m => m.Stem.Equals(nl0.Mission, StringComparison.OrdinalIgnoreCase)));
            return new ScenarioScreen { Character = Math.Max(0, ch) };
        }
        // back from a melee (the original front end's main loop decides by kind, whatever the state): STANDINGS, or the scenario form
        if (LastLaunch is { Kind: GameKind.Scenario } sl)
        {
            if (LastMelee is { Won: true } won) Victory.Record(sl.Mission, sl.ModelIndex, won.Mass);
            return new MeleeScreen { Mode = 1 };
        }
        // a launch that could not be built (a custom map missing its terrain): back to its form, saying why
        if (LoadError is { } err && LastLaunch is { Kind: GameKind.AutoMelee or GameKind.Scenario } fl && !(Install.IsNitro && fl.Kind == GameKind.Scenario))
            return new MeleeScreen { Mode = fl.Kind == GameKind.Scenario ? 1 : 0, Notice = err };
        if (LastLaunch is { Kind: GameKind.AutoMelee }) return new StandingsScreen { Result = LastMelee };
        // a network melee: its STANDINGS, then its form; a game that never started goes straight back to the form, saying why
        if (LastLaunch is { Kind: GameKind.Multiplayer, Net: { } nl })
        {
            if (LastNet is { Rows.Count: > 0 } nr)
                return new StandingsScreen { Rows = nr.Rows, TeamPlay = nr.TeamPlay, Kind = nr.Kind, Notice = nr.Error, Then = () => new MeleeScreen { Mode = nl.Mode } };
            return new MeleeScreen { Mode = nl.Mode, Notice = LastNet?.Error };
        }
        if (!Install.IsNitro && LastLaunch is { Kind: GameKind.Trip } l && !l.Mission.StartsWith("a01", StringComparison.OrdinalIgnoreCase))
        {
            switch (LastResult)
            {
                case MissionResult.Won when Trip.Mission >= 17:
                    return new CreditsScreen { Then = () => new MainMenuScreen() };
                case MissionResult.Won:
                    // T04 is a dream: with salvage management on, the original front end's main loop reloads trip4.spc (the car as it left the garage
                    // after T03) instead of taking the mission's damage (state 1, N == 4)
                    bool dream = Trip.Mission == 4 && (Settings.PlayOptions & 2) == 0;
                    Trip.Won();
                    // The original front end's main loop / the router's entry: the car's damage, the repairs that fit the last order, the field salvage
                    if (Trip.Inventory(this) is { } inv)
                    {
                        if (dream) inv.DreamOver();
                        else inv.MissionWon(LastTrip?.State, LastTrip?.Salvage, Parts, Rand);
                        Trip.Save();
                    }
                    return new TripRouterScreen();
                case MissionResult.Quit:
                    Trip.Quit();
                    break;
                case MissionResult.Reconfigure:
                    // the escape menu's Reconfigure (state 8): back through the router to the garage, the mission to be replayed
                    return new TripRouterScreen();
            }
        }
        return new MainMenuScreen();
    }

    /// <summary>Leaves the current screen for another (the next screen's message, posted).</summary>
    public void Go(ShellScreen next)
    {
        Impact?.Clear();
        try { _screen?.Leave(); }
        catch (Exception ex) { GD.PrintErr($"shell: leaving {_screen?.GetType().Name} failed: {ex}"); }
        _screen = next;
        next.Shell = this;
        var modal = _modal;
        try { TestThrow(next.GetType().Name); next.Enter(); }
        catch (Exception ex) when (_modal is not null && _modal != modal) { DropModal(ex); }   // a pop-up the screen opened failed: only it goes
        catch (Exception ex) { Recover(ex, $"{next.GetType().Name}"); }
    }

    /// <summary>Test hook: I76_SHELL_THROW=FieldSalvageScreen (a screen's or pop-up's class, or FirstScreen) fails there, to see the recovery.</summary>
    private static void TestThrow(string where)
    {
        if (System.Environment.GetEnvironmentVariable("I76_SHELL_THROW") == where) throw new InvalidOperationException($"I76_SHELL_THROW={where}");
    }

    /// <summary>Puts an overlay (the notepad screens) over the current screen, or takes it away (null): the screen underneath repaints.</summary>
    public void Overlay(ShellScreen? overlay)
    {
        _overlay?.Leave();
        _overlay = overlay;
        if (overlay is not null) { overlay.Shell = this; overlay.Enter(); }
        else _screen?.Repaint();
    }

    /// <summary>A modal popup over everything, or none: it is ticked instead of the overlay and the screen.</summary>
    /// <summary>A pop-up is up over the screen (which must not repaint under it).</summary>
    public bool ModalOpen => _modal is not null;
    /// <summary>An overlay (the notepad screens) is up over the screen: a screen's own layers over the frame hide under it.</summary>
    public bool OverlayOpen => _overlay is not null;

    public void Modal(ShellScreen? popup)
    {
        _modal?.Leave();
        _modal = popup;
        if (popup is not null) { popup.Shell = this; TestThrow(popup.GetType().Name); popup.Enter(); }
    }

    /// <summary>A control of a screen's own over the shell: under the frame (on the upscaled backgrounds, over them) or over it.</summary>
    public void AddLayer(Control c, bool underFrame)
    {
        AddChild(c);
        if (underFrame) MoveChild(c, _view.GetIndex());
    }

    /// <summary>A layer over the frame but under the text drawn at the window's resolution (the modern look's labels stay on top of it).</summary>
    public void AddLayerOverFrame(Control c)
    {
        AddChild(c);
        MoveChild(c, _view.GetIndex() + 1);
    }

    public void MissionList() => OnMissionList?.Invoke();

    /// <summary>Hands over to the engine (0xc010: the shell returns 3).</summary>
    public void Launch(ShellLaunch l)
    {
        if (!Install.IsNitro) Trip.Save();    // Nitro has no trip
        CarryMusic();
        OnLaunch(l);
    }

    /// <summary>
    /// A trip launch (0xc010 after garage DONE, NEW TRIP, NEXT MISSION, CHOOSE
    /// YOUR VEHICLE): the car written as addon\vehscn.vcf and
    /// its state as the shell's vsf, the next mission in it.
    /// </summary>
    public void LaunchTrip(I76.Sim.Trip.TripInventory inv)
    {
        I76.Formats.Vfs.AssetVfs.Addon["vehscn.vcf"] = inv.ToCar().ToVcf();
        Launch(new ShellLaunch(Trip.Stem, GameKind.Trip, "vehscn.vcf") { State = inv.State(), NoSalvage = (Settings.PlayOptions & 2) != 0 });
    }

    /// <summary>Quit to the desktop (0xc002, the original front end's main loop returns 0xff).</summary>
    public void Quit() { Trip.Save(); GetTree().Quit(); }

    /// <summary>Options' "Switch Game" (offered when the start showed the game chooser): back to the chooser in this window, then the other game (<see cref="I76.Game.Boot.GameSwitch"/>).</summary>
    public void SwitchGame() { Trip.Save(); I76.Game.Boot.GameSwitch.ToChooser(GetTree()); }

    /// <summary>A sound record (72–82), fire and forget (the original blocks until a click has played).</summary>
    public void Sound(int record)
    {
        if (!_sounds.TryGetValue(record, out var s))
            _sounds[record] = s = I76.Formats.Audio.SoundFiles.Parse(Db.Wave(record)) is { } pcm ? Assets.AudioBank.ToStream(pcm) : null;
        if (s is null) return;
        _sfx.Stream = s;
        _sfx.Play();
    }

    /// <summary>
    /// The CD music: the GOG shell asks for track numbers two below the
    /// disc's (the 1997 shell plays disc 2 track 15 where this one asks for 13),
    /// so 13, the menu, is disc track 15 and 8, the
    /// credits, disc track 10. Track numbers here are the shell's.
    /// </summary>
    public void PlayMusic(int shellTrack, bool loop = true)
    {
        // Nitro: the menu is track 10 and the credits 4, files music/N.mp3 as they are
        int disc = Install.IsNitro ? shellTrack switch { 13 => 10, 8 => 4, _ => shellTrack } : shellTrack + 2;
        if (Assets.CdMusic.Path(Install, disc) is not { } path) { _music.Stop(); return; }
        _music.Stream = new AudioStreamMP3 { Data = File.ReadAllBytes(path), Loop = loop };
        ApplyMusicLevel();
        _music.Play();
    }

    /// <summary>The Audio Control's music level (0..10, below 2 off) on the music.</summary>
    public void ApplyMusicLevel() => _music.VolumeDb = Settings.MusicDb;

    public void StopMusic() => _music.Stop();

    /// <summary>
    /// The menu track outlives the shell: handed to the tree root, it plays on through the scene reload and the mission build
    /// (the loading card), until Main drops it as the mission's own track starts. Freed with the shell, it left the card silent.
    /// </summary>
    public const string CarriedMusicName = "CarriedMusic";
    public void CarryMusic()
    {
        if (!_music.Playing) return;
        var root = GetTree().Root;
        root.GetNodeOrNull(CarriedMusicName)?.Free();
        _music.Name = CarriedMusicName;
        _music.ProcessMode = ProcessModeEnum.Always;   // the card's effects rehearsal and fade-out pause the tree: the track plays on through them
        _music.GetParent().RemoveChild(_music);
        root.AddChild(_music);
    }

    /// <summary>Ends a carried menu track (the mission's music, a briefing, or the shell coming back has taken over).</summary>
    public static void DropCarriedMusic(SceneTree tree) => tree.Root.GetNodeOrNull(CarriedMusicName)?.QueueFree();

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouse m)
        {
            Mouse = ToShell(m.Position);
            // the mouse takes over from the pad's focus
            if (e is InputEventMouseButton || e is InputEventMouseMotion { Relative.X: not 0 } || e is InputEventMouseMotion { Relative.Y: not 0 }) PadFocusOff();
        }
        // a pad button or a lone D-pad direction too; one the virtual stick has no name for does not end the wait
        if (Capture is { } cap && (e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }
            || e is InputEventJoypadButton { Pressed: true } && I76.Game.Controls.NameOf(e) is not null))
        {
            Capture = null;
            cap(I76.Game.Controls.NameOf(e));
            return;
        }
        // Ctrl+F8 (a debug key, as in a mission): the cars' material maps and glass against the old all-over clear coat, in the 3D garage
        if (e is InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, AltPressed: false, ShiftPressed: false, Keycode: Key.F8 } && _screen?.SwitchesLook == true && _modal is null)
        {
            GetViewport().SetInputAsHandled();
            GD.Print($"car materials {(I76.Game.Assets.MaterialLibrary.ToggleCarMaterials() ? "on" : "off")}");
            return;
        }
        // F12: the other look, on the screens that can (the 3D garage)
        if (e is InputEventKey { Pressed: true, Echo: false } lk && _screen?.SwitchesLook == true && _modal is null && I76.Game.GameKeys.CommandOf(lk) == I76.Formats.Input.GameKey.ToggleVideoMode)
        {
            GetViewport().SetInputAsHandled();
            SwitchLook();
            return;
        }
        // the port's Developer panel over the main menu (Ctrl+F2 or Ctrl+`, as in a mission); it has the keyboard and mouse while up
        if (_devPanel is not null) return;
        if (e is InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, Keycode: Key.F2 or Key.Quoteleft } && Current is MainMenuScreen
            && (Developer || Settings.DeveloperTools))
        {
            GetViewport().SetInputAsHandled();
            ToggleDevPanel();
            return;
        }
        // F9 on the main menu: the debug mission list (every mission in miss16, straight into it); Esc there comes back
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F9 } && Current is MainMenuScreen)
        {
            GetViewport().SetInputAsHandled();
            MissionList();
            return;
        }
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F8 } && Current is MainMenuScreen && OnEditor is not null)
        {
            GetViewport().SetInputAsHandled();
            OpenEditor();
            return;
        }
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }) _wheelPending++;
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }) _wheelPending--;
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _clickPending = true;
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) _rightPending = true;
        if (e is InputEventKey { Pressed: true } k)
        {
            if (!k.Echo) _keys.Enqueue(k.Keycode);
            else if (k.Keycode == Key.Backspace) _keys.Enqueue(Key.Backspace);
            if (I76.Formats.Shell.TextEntry.Typed((int)k.Unicode, (int)k.KeyLabel, k.ShiftPressed, k.CtrlPressed || k.AltPressed || k.MetaPressed) is var tc and not (char)0)
                _typed.Append(tc);
        }
    }

    private Vector2I ToShell(Vector2 p)
    {
        var size = GetViewport().GetVisibleRect().Size;
        float scale = Math.Min(size.X / ShellFrame.W, size.Y / ShellFrame.H);
        var off = (size - new Vector2(ShellFrame.W, ShellFrame.H) * scale) / 2;
        var q = (p - off) / scale;
        return new Vector2I((int)q.X, (int)q.Y);
    }

    // capture hook: I76_SHELL_INPUT="move:x,y@frame;click:x,y@frame;key:escape@frame" in shell coordinates
    private readonly List<(int Frame, string Kind, string Arg)> _script = (System.Environment.GetEnvironmentVariable("I76_SHELL_INPUT") ?? "")
        .Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Split('@')).Where(p => p.Length == 2 && p[0].Contains(':'))
        .Select(p => (int.Parse(p[1]), p[0][..p[0].IndexOf(':')], p[0][(p[0].IndexOf(':') + 1)..])).ToList();
    // shell frames counted across visits: after a mission the script carries on rather than replaying
    private static int _frame;
    // "wait:games=N@frame" holds the script (and the capture's frame count) at that frame until the roster lists N games
    private static Func<bool>? _waiting;
    internal static void ResetForGameSwitch() { _frame = 0; _waiting = null; }
    /// <summary>The capture script is held on a condition: the game does not count the frames towards --quit-after.</summary>
    public static bool ScriptWaiting => _waiting is not null;

    private void Scripted()
    {
        if (_waiting is { } w) { if (!w()) return; _waiting = null; }
        _frame++;
        foreach (var (f, kind, arg) in _script.Where(e => e.Frame == _frame))
        {
            GD.Print($"shell: script {kind}:{arg}@{f} on {Current?.GetType().Name}");
            if (kind is "move" or "click" or "rclick" && arg.Split(',') is [var x, var y]) Mouse = new Vector2I(int.Parse(x), int.Parse(y));
            if (kind == "click") _clickPending = true;
            // capture hook: "hold:x,y@frame" presses and keeps the button down, "release:@frame" lets go
            if (kind == "hold" && arg.Split(',') is [var hx, var hy]) { Mouse = new Vector2I(int.Parse(hx), int.Parse(hy)); _clickPending = true; _scriptHeld = true; }
            if (kind == "release") _scriptHeld = false;
            if (kind == "rclick") _rightPending = true;
            // capture hook: "wheel:up@frame" / "wheel:down@frame", a notch of the mouse wheel
            if (kind == "wheel") _wheelPending += arg == "up" ? 1 : -1;
            // capture hook: "press:Q@frame" answers a pending capture as if that key were pressed
            if (kind == "press" && Capture is { } cap) { Capture = null; cap(arg); }
            if (kind == "key" && Enum.TryParse<Key>(arg, true, out var k))
            {
                if (k == Key.F9 && Current is MainMenuScreen) MissionList();
                else if (k == Key.F8 && Current is MainMenuScreen) OpenEditor();
                else _keys.Enqueue(k);
            }
            // capture hook: "launch:scenario,p01,CAR.vcf" (also trip, melee) hands a mission to the game as the forms would
            if (kind == "launch" && arg.Split(',') is [var lk, var lm, var lc])
            {
                var gk = lk switch { "scenario" => GameKind.Scenario, "melee" => GameKind.AutoMelee, _ => GameKind.Trip };
                // a trip launch is that mission of the trip, so the router after it goes on from there (tNN → mission NN)
                if (gk == GameKind.Trip && lm.Length >= 3 && char.ToLowerInvariant(lm[0]) == 't' && int.TryParse(lm[1..3], out int tn)) Trip.Mission = tn;
                Launch(new ShellLaunch(lm, gk, lc) { Driver = "Capture" });
            }
            // capture hook: "trip:t03,CAR.vcf" starts that trip mission as NEW TRIP does (the car the trip's, its state the launch's),
            // so the win comes back through the router, the field salvage and the garage with the trip car
            if (kind == "trip" && arg.Split(',') is [var tm, var tc] && tm.Length >= 3 && int.TryParse(tm[1..3], out int tmn))
            {
                Trip.New();
                Trip.Mission = tmn;
                LaunchTrip(Trip.StockCar(this, tc));
            }
            // capture hook: "overlay:controls@frame" opens the Control Configuration over the screen
            if (kind == "overlay" && arg == "controls") Overlay(new ControlsScreen());
            if (kind == "overlay" && arg == "mouse") Overlay(new MouseOptionsScreen());
            if (kind == "overlay" && arg == "graphics") Overlay(new GraphicsScreen());
            if (kind == "overlay" && arg == "audio") Overlay(new AudioScreen());
            if (kind == "type") _typed.Append(arg);
            // capture hook: "keyev:Semicolon,58,shift@frame" a key press as the window delivers it (keycode, Unicode, modifiers; Unicode 0 for none)
            if (kind == "keyev" && arg.Split(',') is [var kn, var ku, ..var km] && Enum.TryParse<Key>(kn, true, out var kk))
                Input.ParseInputEvent(new InputEventKey
                {
                    Keycode = kk, PhysicalKeycode = kk, KeyLabel = kk, Unicode = uint.Parse(ku), Pressed = true,
                    ShiftPressed = km.Contains("shift"), CtrlPressed = km.Contains("ctrl"), AltPressed = km.Contains("alt"),
                });
            if (kind == "wait" && arg.Split('=') is ["games", var n] && int.TryParse(n, out int games))
                _waiting = () => Current is MeleeScreen ms && ms.GamesFound >= games;
        }
    }

    public override void _Process(double delta)
    {
        using var _ = Hitch.Time(Hitch.Phase.Shell);
        if (_script.Count > 0) Scripted();
        Delta = delta;
        PadNavigate(delta);
        Typed = _typed.ToString();
        if (Typed.Length > 0 && _script.Count > 0) GD.Print($"shell: typed \"{Typed}\"");
        _typed.Clear();
        Clicked = _clickPending;
        Wheel = _wheelPending;
        _wheelPending = 0;
        _clickPending = false;
        RightClicked = _rightPending;
        _rightPending = false;
        // Esc anywhere opens the Options Menu unless an overlay is up
        if (_modal is null && _overlay is null && _keys.Contains(Key.Escape) && _screen is { EscapeOpensOptions: true })
        {
            _keys.Clear();
            // hosted over a mission there is no Options Menu: Reconfigure's chassis form cancels back to it instead
            if (Hosted is null) Overlay(new OptionsScreen());
            else if (_screen is IChassisForm g) g.CancelMelee();
        }
        var ticked = Current;
        var modal = _modal;
        try { ticked?.Tick(); }
        catch (Exception ex) when (_modal is not null && (_modal == ticked || _modal != modal)) { DropModal(ex); }
        catch (Exception ex) { Recover(ex, $"{ticked?.GetType().Name}"); }
        _keys.Clear();
        // the labels only over the screen that owns them: a pop-up or an overlay shows the sprites beneath
        if (Impact is not null) Impact.Visible = _modal is null && _overlay is null;
        if (_build is not null) _build.Visible = _screen is MainMenuScreen && _modal is null && _overlay is null;
        if (_updateLink is not null) _updateLink.Visible = _build?.Visible == true && I76.Game.Boot.UpdateNotice.Line is not null;
        // a screen over a 3D view of its own (the garage): the frame is see-through where it is still the background, and opaque where the screen says
        bool seeThrough = _screen is { TransparentFrame: true };
        Frame.Transparent = Upscaled || seeThrough;
        Frame.Keep = seeThrough ? _screen!.FrameKeep : Kept(Frame.Record, Frame.Native);
        if (Frame.Present() is { } img)
        {
            if (_tex is null) { _tex = ImageTexture.CreateFromImage(img); _view.Texture = _tex; }
            else _tex.Update(img);
            // the black present after a background load hides the upscale and the text too, beside the frame as on it
            _backdrop?.Show(Frame.ShowingBlack ? 0 : Frame.Record, Frame.ShowingBlack ? 0 : Frame.Native, Frame.Edits, Frame.EditsVersion);
            if (_text is not null) _text.Visible = !Frame.ShowingBlack;
        }
    }
}

/// <summary>The blank screen under a hosted overlay: no Esc menu of its own.</summary>
public sealed class HostScreen : ShellScreen
{
    public override bool EscapeOpensOptions => false;
    public override void Tick() { }
}

/// <summary>One shell screen: built on entry, ticked every loop (0xc004), torn down on leaving (0xc002).</summary>
public abstract class ShellScreen
{
    public Shell Shell { get; set; } = null!;
    protected ShellFrame Frame => Shell.Frame;
    /// <summary>Whether Esc on this screen opens the Options Menu (true for the screens, false for the overlays).</summary>
    public virtual bool EscapeOpensOptions => true;
    public virtual void Enter() { }
    public abstract void Tick();
    /// <summary>
    /// The regions the pad can focus now (the port's pad navigation, ShellPadNav.cs), in shell coordinates; the D-pad and the left
    /// stick move between them, A clicks the focused one's centre. Empty: the screen has no pad focus (a plain click on A).
    /// </summary>
    public virtual IReadOnlyList<PadRegion> PadRegions => [];
    /// <summary>A text field has the keyboard: the pad does not navigate (A on its region focuses it).</summary>
    public virtual bool TextEditing => false;
    /// <summary>The pad was used while a field had the keyboard: the field is done (the pad cannot type).</summary>
    public virtual void StopEditing() { }
    /// <summary>The screen reads the pad itself (a list pop-up, the controller screen): the shell's navigation stays out of it.</summary>
    public virtual bool OwnsPad => false;
    /// <summary>The screen lights what the mouse is over (so the focus is shown by moving the mouse there, with no outline of the shell's own).</summary>
    public virtual bool HoverShows => false;
    public virtual void Leave() { }
    /// <summary>The screen has a 3D view under the frame (<see cref="Shell.AddLayer"/>): the frame shows through wherever it is still the background, except where <see cref="FrameKeep"/> says it is not.</summary>
    public virtual bool TransparentFrame => false;
    /// <summary>F12 switches the look on this screen (<see cref="Shell.SwitchLook"/>), which then has to redraw its own layers in <see cref="LookChanged"/>.</summary>
    public virtual bool SwitchesLook => false;
    public virtual void LookChanged() { }
    /// <summary>With <see cref="TransparentFrame"/>: the pixels (640x480) that stay opaque as the background, the paper the screen draws on.</summary>
    public virtual bool[]? FrameKeep => null;
    /// <summary>Redraws after an overlay came off it (the repaint hook).</summary>
    public virtual void Repaint() { }
}

/// <summary>
/// A sprite widget (the sprite-widget constructor): a rectangle the size
/// of its sprite, drawn dim (its own sprite) or lit (the one before it),
/// with an optional banner shown while it is lit. The hit test is strict:
/// x0 &lt; mx &lt; x1, y0 &lt; my &lt; y1, with x1 = x + w − 1.
/// </summary>
public sealed class SpriteWidget(int id, int x, int y, SpriteBank bank, int sprite, SpriteWidget? banner = null)
{
    public int Id { get; } = id;
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Width => _bank[_normal]?.Width ?? 0;
    public int Height => _bank[_normal]?.Height ?? 0;
    private readonly SpriteBank _bank = bank;
    private readonly int _normal = sprite;
    /// <summary>The lit sprite: the one before the dim one, unless given (OTHER's pair is 46/45).</summary>
    public int Hot { get; init; } = sprite - 1;
    public bool Lit { get; set; }
    /// <summary>The bank is Nitro's main menu bank 56 (dim sprite even, lit odd), its Impact labels the Nitro table's.</summary>
    public bool NitroBank { get; init; }
    private (ImpactLabels.Spec Spec, bool Lit)? LabelOf(int sprite) => NitroBank ? ImpactLabels.OfNitro(sprite) : ImpactLabels.Of(sprite);
    /// <summary>Stays lit while its row is open.</summary>
    public bool Selected { get; set; }
    public SpriteWidget? Banner { get; } = banner;
    /// <summary>Drawn crossed out (a menu item this port does not offer).</summary>
    public bool Struck { get; init; }
    /// <summary>The banner's words instead of its sprite, in Impact (or the shell's Lee in pixels without it).</summary>
    public string? Caption { get; init; }
    /// <summary>The shell font for a Caption drawn in pixels.</summary>
    public ShellFont? CaptionFont { get; init; }
    /// <summary>The label's words in Impact instead of the sprite's (IPX shown as LAN); the 1997 look keeps the sprite.</summary>
    public string? Text { get; init; }

    public bool Hit(Vector2I m) => X < m.X && m.X < X + Width - 1 && Y < m.Y && m.Y < Y + Height - 1;

    public void Draw(ShellFrame f, ImpactLayer? impact = null)
    {
        bool lit = Lit || Selected;
        f.Restore(X, Y, Width, Height);
        f.Blit(_bank[lit ? Hot : _normal], X, Y);
        if (Struck && LabelOf(lit ? Hot : _normal) is { } label)
        {
            var colour = label.Lit ? Colors.White : Color.Color8(109, 109, 109);
            // in pixels: across the lettering's own columns (the sprite's bright pixels), through its middle
            if (_bank[lit ? Hot : _normal] is { } sp)
            {
                // the lettering's own colour: within a quarter of the sprite's brightest (not its darker shadow)
                int Sum(int i) { int p = sp.Pixels[i] * 3; return f.Palette[p] + f.Palette[p + 1] + f.Palette[p + 2]; }
                int top = 0;
                for (int i = 0; i < sp.Pixels.Length; i++) if (sp.Opaque[i]) top = Math.Max(top, Sum(i));
                int x0 = sp.Width, x1 = -1, y0 = sp.Height, y1 = -1;
                for (int yy = 0; yy < sp.Height; yy++)
                    for (int xx = 0; xx < sp.Width; xx++)
                    {
                        int i = yy * sp.Width + xx;
                        if (!sp.Opaque[i] || Sum(i) < top * 3 / 4) continue;
                        x0 = Math.Min(x0, xx); x1 = Math.Max(x1, xx); y0 = Math.Min(y0, yy); y1 = Math.Max(y1, yy);
                    }
                if (x1 >= x0)
                {
                    // through the middle of the lettering, rising a pixel or two across it, two pixels thick
                    int mid = (y0 + y1) / 2, from = x0 - 1, to = x1 + 1, rise = 1;
                    byte ink = f.Nearest(colour);
                    for (int xx = from; xx <= to; xx++)
                        f.Fill(X + xx, Y + mid + rise - (int)MathF.Round(2f * rise * (xx - from) / Math.Max(1, to - from)), 1, 2, ink);
                }
            }
            impact?.Put(X, Y, Width, Height, label.Spec with { Strike = true }, colour);
        }
        else if (Text is not null && impact is not null && LabelOf(lit ? Hot : _normal) is { } relabel)
            impact.Put(X, Y, Width, Height, relabel.Spec with { Text = Text }, relabel.Lit ? Colors.White : Color.Color8(109, 109, 109));
        else impact?.Show(X, Y, Width, Height, lit ? Hot : _normal, NitroBank);
        if (Banner is null) return;
        f.Restore(Banner.X, Banner.Y, Banner.Width, Banner.Height);
        if (Lit && !Selected)
        {
            if (Caption is { } words)
            {
                if (impact is { Available: true })
                    impact.Put(Banner.X, Banner.Y, Banner.Width, Banner.Height, new ImpactLabels.Spec(words, 12f, 1f, 0f, 14.123f, 0f, Centre: true), Colors.White);
                else if (CaptionFont is { } font)
                {
                    // every glyph value white (font 0x45 draws in 15), its key and 0 transparent
                    var white = new byte[256];
                    Array.Fill(white, f.Nearest(Colors.White));
                    white[0] = 0xff;
                    white[font.Key & 0xff] = 0xff;
                    f.Text(font, Banner.X + Banner.Width / 2, Banner.Y + (Banner.Height - font.Height) / 2, "~" + words, white);
                }
            }
            else
            {
                f.Blit(Banner._bank[Banner._normal], Banner.X, Banner.Y);
                impact?.Show(Banner.X, Banner.Y, Banner.Width, Banner.Height, Banner._normal, Banner.NitroBank);
            }
        }
        else impact?.Hide(Banner.X, Banner.Y);
    }

    public void Erase(ShellFrame f, ImpactLayer? impact = null)
    {
        f.Restore(X, Y, Width, Height);
        impact?.Hide(X, Y);
        if (Banner is not null) { f.Restore(Banner.X, Banner.Y, Banner.Width, Banner.Height); impact?.Hide(Banner.X, Banner.Y); }
    }
}

/// <summary>
/// The trip's persistent state: the player-state block's mission number
/// (+0x18) and the highest reached (+0x1c). Kept in user://, never
/// in the install.
/// </summary>
public sealed class TripState
{
    private const string Path = "user://saves/trip.cfg", InventoryPath = "user://saves/trip_car.json";
    public int Mission { get; set; } = 1;
    public int Highest { get; set; } = 1;
    private I76.Sim.Trip.TripInventory? _inventory;
    private bool _inventoryRead;
    /// <summary>The trip came from a bookmark this session: no tutorial voices or questions.</summary>
    public bool FromBookmark { get; set; }
    /// <summary>This session's first visits (the shell's own "first build" flags).</summary>
    public bool SalvageVisited { get; set; }
    public bool GarageVisited { get; set; }

    /// <summary>The trip's car and parts (user://saves/trip_car.json), or null before a trip has started.</summary>
    public I76.Sim.Trip.TripInventory? Inventory(Shell s)
    {
        if (_inventoryRead) return _inventory;
        _inventoryRead = true;
        using var f = Godot.FileAccess.Open(InventoryPath, Godot.FileAccess.ModeFlags.Read);
        if (f is not null) _inventory = I76.Sim.Trip.TripInventory.Load(f.GetAsText(), s.Parts, s.Vfs);
        return _inventory;
    }

    /// <summary>The car and parts of a loaded bookmark become the trip's.</summary>
    public void SetInventory(I76.Sim.Trip.TripInventory inv) { _inventory = inv; _inventoryRead = true; }

    /// <summary>A fresh car for the trip: the designers' stock car (vppt%02d.vcf, vppa01 for training).</summary>
    public I76.Sim.Trip.TripInventory StockCar(Shell s, string vcf)
    {
        var vfs = s.Vfs;
        var car = I76.Sim.Trip.TripCar.FromVcf(vfs.Read(vcf), n => I76.Sim.Vehicles.VehicleFactory.EngineCompId(vfs, n));
        _inventory = I76.Sim.Trip.TripInventory.FromCar(car, s.Parts, vfs);
        _inventoryRead = true;
        return _inventory;
    }

    public static TripState Load()
    {
        var t = new TripState();
        var c = new ConfigFile();
        if (c.Load(Path) != Error.Ok) return t;
        t.Mission = (int)c.GetValue("trip", "mission", 1);
        t.Highest = (int)c.GetValue("trip", "highest", 1);
        return t;
    }

    public void Save()
    {
        DirAccess.MakeDirRecursiveAbsolute("user://saves");
        var c = new ConfigFile();
        c.SetValue("trip", "mission", Mission);
        c.SetValue("trip", "highest", Highest);
        c.Save(Path);
        if (_inventory is null) return;
        using var f = Godot.FileAccess.Open(InventoryPath, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(_inventory.Save());
    }

    /// <summary>NEW TRIP: back to T01.</summary>
    public void New() { Mission = 1; FromBookmark = false; SalvageVisited = false; GarageVisited = false; Save(); }

    /// <summary>A won trip mission: the number advances at the next launch (the original front end's 0xc001 exit).</summary>
    public void Won() { Mission = Math.Min(17, Mission + 1); Highest = Math.Max(Highest, Mission); Save(); }

    /// <summary>Quit out of a mission (state 2): the trip stays where it was.</summary>
    public void Quit() => Save();

    public string Stem => $"t{Mission:00}";
}

/// <summary>
/// The rest of I76PLYR.DEF: the
/// graphics block (+0x40, 16 bytes), the audio levels (+0x50: music, sfx,
/// voice, channels), the play-option flags (+0x58) and the difficulty
/// (+0x5c). Kept in user://.
/// </summary>
public sealed class ShellSettings
{
    private const string Path = "user://shell.cfg";
    /// <summary>
    /// The game these settings are for: the Nitro Pack keeps its own Play Options flags and difficulty ([nitro] play /
    /// difficulty; the original's nitplyr.def beside I76PLYR.DEF), everything else is shared. Set when a shell or a mission
    /// starts on an install; Load and Save follow it. See I76.Formats.Shell.PlayProfile.
    /// </summary>
    public static bool Nitro { get; set; }
    /// <summary>
    /// The shipped I76PLYR.DEF's graphics block, but for the port's defaults: the
    /// rear mirror High (+0xd, full rather than objects only), the external
    /// instruments On (+0xe, the HUD readouts in the chase view), +0x8, a byte
    /// no screen of the original shows, the modern look's anti-aliasing (0 off,
    /// 1 MSAA 2x, the default, 2 MSAA 4x: 4x costs 12 % of a full-detail frame at
    /// 3440x1440, 2x 3 %, docs/perf/costs.md), and Terrain Detail Low (+0x2) and Shadow Detail Low
    /// (+0x6), the lighter settings for a first start (a saved file keeps its own,
    /// the block whole: a 4x saved before 2x was the default stays 4x).
    /// </summary>
    public static readonly byte[] DefaultGraphics = [5, 1, 0, 1, 1, 1, 1, 2, 1, 1, 1, 1, 3, 2, 1, 1];
    public byte[] Graphics { get; } = (byte[])DefaultGraphics.Clone();
    public byte[] Audio { get; } = [8, 8, 8, 8];
    /// <summary>Bit 0x01 Arcade Phys., 0x02 No Salv. Manag., 0x04 Unlim. Ammo, 0x08 Armor, 0x10 Chas. Rein., 0x20 Wiper Fluid.</summary>
    public int PlayOptions { get; set; }
    /// <summary>0 Wimp, 1 Champion, 2 Badass; Wimp unless the player chose, as the shipped I76PLYR.DEF and nitplyr.def (+0x5c = 0).</summary>
    public int Difficulty { get; set; } = 0;
    /// <summary>The port's own: the player's car on the modern physics (docs/modern-physics.md); F7 in a mission flips it.</summary>
    public bool ModernPhysics { get; set; }
    /// <summary>The port's own: the HD Menu Pack's upscaled art in the modern look ([port] hd_menu_art, no row); on unless the file says false.</summary>
    public bool HdMenuArt { get; set; } = true;
    /// <summary>The port's own: a texture pack's upscaled world, car and cockpit textures (the dash, gauges and panels among them) in the modern look ([port] hd_textures, with hd_texture_size Graphic Detail's HD Textures row; HdTextures). On unless the file says false.</summary>
    public bool HdTextures { get; set; } = true;
    /// <summary>The port's own: which size of the texture pack to draw, 4 (the HD Textures row's; the user folder's tex-world-4x) or another whole multiple the file or the Developer panel names (tex-world-3x, -2x; [port] hd_texture_size; HdTextures.Size). Applies from the next mission.</summary>
    public int HdTextureSize { get; set; } = I76.Formats.Shell.HdPackFolders.DefaultSize;
    /// <summary>The port's own: the brake key on a stopped car shifts into reverse, accelerate on a reversing one into drive (Play Options; off, as the original).</summary>
    public bool AutoReverse { get; set; }
    /// <summary>The port's own: the pad's rumble for the original's force-feedback events (Play Options; on, and without a pad it does nothing; both looks).</summary>
    public bool ForceFeedback { get; set; } = true;
    /// <summary>The port's own: Force Feedback on a DirectInput wheel or stick instead of the pad (Play Options' Wheel; Windows only, without a device the pad).</summary>
    public bool ForceFeedbackWheel { get; set; }
    /// <summary>The port's own: the wheel's force-feedback strength, 0..100 % (Play Options).</summary>
    public int FfbStrength { get; set; } = 100;
    /// <summary>The port's own: part of the force-feedback wheel's name, to pick it among several; empty, the first. Only in the file.</summary>
    public string FfbDevice { get; set; } = "";
    /// <summary>
    /// Play Options' Force Feedback row: 0 Off, 1 Pad (the original's effects on the pad's motors), 2 Pad Enhanced (and the port's pulses,
    /// <see cref="RumbleMode"/>), 3 Wheel (read as Pad where there is no DirectInput). The three fields under it are the file's keys.
    /// </summary>
    public int ForceFeedbackMode
    {
        get => !ForceFeedback ? 0 : ForceFeedbackWheel && I76.Game.DirectInputForce.Supported ? 3 : RumbleMode == I76.Formats.Input.RumbleMode.Enhanced ? 2 : 1;
        set
        {
            ForceFeedback = value != 0;
            ForceFeedbackWheel = value == 3;
            RumbleMode = value == 2 ? I76.Formats.Input.RumbleMode.Enhanced : I76.Formats.Input.RumbleMode.Original;
        }
    }
    /// <summary>
    /// The row's values; Pad Enhanced and Wheel are marked as the port's (<see cref="PortMark"/>): the original drove a force-feedback
    /// stick from its effect table (Pad plays that table), never a wheel, whose one axis takes the effects as the port adapts them.
    /// </summary>
    public static readonly string[] ForceFeedbackNames = ["Off", "Pad", "Pad Enhanced" + PortMark, "Wheel" + PortMark];
    /// <summary>
    /// What marks a value the original game does not have, on every pad row that offers the original's (docs/playing.md): the shell's bitmap fonts and the pads' handwriting have no
    /// italics or second face, and the value columns are too narrow for a word, so one glyph, explained once in the docs.
    /// </summary>
    public const string PortMark = "*";
    /// <summary>The port's own: the pad's events, Original (the game's own effects) or Enhanced (and pulses for the events they do not cover): Force Feedback's Pad Enhanced ([port] rumble, written when enhanced; a file's "off", from the Rumble row of a build before the fold, reads as Force Feedback Off unless a wheel was chosen).</summary>
    public I76.Formats.Input.RumbleMode RumbleMode { get; set; }
    /// <summary>The port's own: Play Options' Rumble Strength, percent on every level sent to the pad (25 to 150; [port] rumble_strength, written when not 100).</summary>
    public int RumbleStrength { get; set; } = I76.Formats.Input.RumbleStrength.Default;
    /// <summary>The port's own: Audio Control's Output, Stereo (the default), Headphones (the same), 5.1 or 7.1 ([port] output, written when not Stereo; BassShaker).</summary>
    public I76.Formats.Input.AudioOutput Output { get; set; }
    /// <summary>The port's own: Audio Control's LFE Level, the shaker signal's (and the stereo mix's bass boost's) level, 0 (none, the default) to 200 % ([port] lfe_level, written when not 0 or with 5.1 and 7.1).</summary>
    public int LfeLevel { get; set; } = I76.Formats.Input.AudioOutputs.DefaultLevel;
    /// <summary>The port's own, no row: each source's share of the shaker signal, percent in TactileSource order ([port] lfe_engine ... lfe_strip, 0 to 200, each written when not its default: 100, the dev-only lfe_cracks and lfe_strip 0).</summary>
    public int[] LfeSources { get; set; } = I76.Formats.Input.TactileSources.Defaults();
    /// <summary>The port's own, no row: the game's sounds' and the movies' tone, "hz:db" points or "off" ([port] game_eq, movie_eq, written when set; empty is SoundEq's default).</summary>
    public string GameEq { get; set; } = "";
    public string MovieEq { get; set; } = "";
    /// <summary>The port's own, no row: the shaker signal's correction curve, "hz:db" points ([port] tactile_eq, written when set; none by default).</summary>
    public string TactileEq { get; set; } = "";
    /// <summary>The port's own, no row: the top of the shaker signal and of the stereo mix's bass boost, Hz ([port] crossover, written when not 90).</summary>
    public float Crossover { get; set; } = I76.Formats.Input.AudioOutputs.DefaultCrossover;
    /// <summary>The port's own: the simulation rate in Hz (Play Options; 20, 24, 25, 28, 30, 40 or 60, docs/frame-rate.md), applied from the next mission start; a network game runs at the server's.</summary>
    public int SimRate { get; set; } = I76.Sim.Session.SimRate.Default.Label;
    /// <summary>The port's own: the player has been told once that a rate other than 20 Hz can break the game (helicopters, AI, physics); it is not said again.</summary>
    public bool SimRateWarned { get; set; }
    /// <summary>The port's own: the 1997 look (Graphic Detail; F2 in a mission flips it; --1997 forces it for a run without changing this). Off: the modern look.</summary>
    public bool Look1997 { get; set; }
    /// <summary>The 3D dash (else the 2D dash; F1 in the cockpit toggles it). Kept as a port key: the original's byte, Graphic Detail +0x8, is the anti-aliasing here.</summary>
    public bool Dash3D { get; set; } = true;
    /// <summary>The port's own: Checkpoints (Play Options; off, as the original): a trip mission keeps its state at the start and at each objective done, and a failure can be retried from it (docs/savestate.md).</summary>
    public bool Checkpoints { get; set; }
    /// <summary>The port's own: the Developer panel in single-player missions (docs/dev-hooks.md). No menu sets it: <c>developer=true</c> under [port] in the file (<c>developer_tools</c>, its older name, is still read), or --dev / I76_DEV=1 for one run.</summary>
    public bool DeveloperTools { get; set; }
    /// <summary>[port] dev_fps: Graphic Detail's Show FPS (Display page) and the Developer panel's Settings, the frame rate and frame time in the window's top right corner. Off by default.</summary>
    public bool DevFps { get; set; }

    /// <summary>A hand-written flag's word: true, 1, on or yes (any case) turn it on; anything else, or nothing, leaves it off.</summary>
    public static bool DeveloperWord(string? word) => word?.Trim().ToLowerInvariant() is "true" or "1" or "on" or "yes";
    /// <summary>The port's own: the 1997 look draws as the original's software renderer did rather than its 3D card (Graphic Detail's 1997 Renderer; SoftwareLook). Off unless set: a file without the key is a 3D card.</summary>
    public bool SoftwareRenderer { get; set; }
    /// <summary>The port's own: the modern look's frame matched to the mission's 8-bit palette (Graphic Detail's Palette Grade; SoftwareLook). Off unless set.</summary>
    public bool PaletteGrade { get; set; }
    /// <summary>The port's own: Graphic Detail's Crash Blur, the software renderer's double image on a big cockpit hit (<see cref="I76.Sim.Views.CrashBlurMode"/>: On by default; the original's blit in the 1997 look, a smooth one in the modern look).</summary>
    public int CrashBlur { get; set; } = I76.Sim.Views.CrashBlurMode.On;
    /// <summary>The port's own: the mouse orbits the chase view (Mouse Options' Mouse Camera; <see cref="I76.Sim.Views.MouseLook"/>: 0 Off, 1 Slow, 2 Normal, 3 Fast). Normal unless the player chose; a file without the key gets it.</summary>
    public int MouseCamera { get; set; } = I76.Sim.Views.MouseLook.Normal;
    /// <summary>The port's own: fine-grained mouse sensitivity scale (10% to 200% in 10% steps; shell.cfg [port] mouse_sensitivity; 1.0 unless chosen, migrated from mouse_camera when absent).</summary>
    public float MouseSensitivity { get; set; } = I76.Sim.Views.MouseLook.DefaultSensitivity;
    /// <summary>The port's own: Mouse Camera's vertical flipped (shell.cfg [port] mouse_invert_y; off).</summary>
    public bool MouseInvertY { get; set; }
    /// <summary>The port's own: mouse free look in the F1 3D cockpit (shell.cfg [port] mouse_cockpit; off).</summary>
    public bool MouseCockpit { get; set; }
    /// <summary>The port's own: a 3D-projected aiming crosshair in the F2 chase view (shell.cfg [port] chase_crosshair; off).</summary>
    public bool ChaseCrosshair { get; set; }
    /// <summary>The port's own: the mouse wheel cycles weapons instead of zooming the external views (shell.cfg [port] wheel_cycle_weapon; off).</summary>
    public bool WheelCycleWeapon { get; set; }
    /// <summary>The port's own: Graphic Detail's Max FPS, a choice of <see cref="I76.Sim.Views.FrameLimit"/> (shell.cfg [port] max_fps in frames a second, 0 none). Unlimited unless the player chose.</summary>
    /// <summary>The port's own: Graphic Detail's Resolution (shell.cfg [port] resolution, "WxH", written only for a window size). Empty: the desktop, as the game starts.</summary>
    public string Resolution { get; set; } = "";
    /// <summary>The port's own: Graphic Detail's Hood Gloss, a <see cref="I76.Sim.Views.PaintFinish"/> level 0 (Matte, the default) to 100 (Gloss), Satin at 50: the modern look's car paint and cockpit hood (shell.cfg [port] hood_gloss, any integer, written only when not 0; a file without it keeps its earlier hood_finish word, matte 0, satin 50, gloss 100, or its older hood_matte = true's Matte).</summary>
    public int HoodGloss { get; set; } = I76.Sim.Views.PaintFinish.Default;
    /// <summary>The port's own: Graphic Detail's Render Scale (index into <see cref="I76.Game.Boot.RenderScale.Percents"/>: 50 to 200 %; shell.cfg [port] render_scale, written only when not 100).</summary>
    public int RenderScale { get; set; } = I76.Game.Boot.RenderScale.Default;
    public int MaxFps { get; set; } = I76.Sim.Views.FrameLimit.Unlimited;
    /// <summary>The port's own: Graphic Detail's VSync (shell.cfg [port] vsync). On, as the window has always run, unless the player chose.</summary>
    public bool VSync { get; set; } = true;
    /// <summary>The port's own: in a mission the hidden mouse cursor cannot leave the window (Mouse Options' Mouse In Window; shell.cfg [port] keep_mouse_in_window; off).</summary>
    public bool KeepMouseInWindow { get; set; }
    /// <summary>The port's own: the modern look's procedural plants (Graphic Detail; 0 Off, 1 Low, 2 High; docs/modern-vegetation.md). Low unless the player chose.</summary>
    public int Vegetation { get; set; } = 1;
    /// <summary>
    /// The port's own: the modern look's ambient occlusion (SSAO; Graphic Detail's Ambient Occlusion, Look page; [port] ambient_occlusion,
    /// written only when on). Off unless the player chose: it was Shadow Detail High's until it had a row, and it is the frame's costliest
    /// feature (docs/perf/costs.md: 29 % of a full-detail frame at 3440x1440, most of it the depth pre-pass drawing every surface's normals).
    /// </summary>
    public bool AmbientOcclusion { get; set; }
    /// <summary>
    /// Graphic Detail's Visibility Range (VisibilityRange): how far the world is drawn (I76.Formats.Imaging.DrawDistance: 0 the original's 600 m clip; 1 to
    /// 20 km; shell.cfg [port] draw_distance). Null while the file has no key: it follows the look, the original's in the 1997 look, 20 km in the modern one.
    /// </summary>
    public int? DrawDistanceChoice { get; set; }
    /// <summary>
    /// The port's own: a network host's custom map this game does not have (docs/multiplayer.md "Custom maps"): 0 asks
    /// (the loading screen's Download / Always / Cancel), 1 downloads it, 2 never does (Play Options' Map Downloads).
    /// </summary>
    public int MapDownloads { get; set; }
    /// <summary>The port's own: a hosted game sends its custom map to players who lack it (the host form's Share Maps; on).</summary>
    public bool ShareMaps { get; set; } = true;
    /// <summary>The port's own: a hosted game lets its players reset their car upright (the host form's Car Reset; on).</summary>
    public bool CarReset { get; set; } = true;
    /// <summary>The port's own: a hosted game keeps every engine on, the ignition key never switching one off (the host form's IGN. LOCK; shell.cfg [port] ignition_stays_on, written only when on).</summary>
    public bool IgnitionStaysOn { get; set; }
    /// <summary>The port's own: a hosted game has the CB radio on (the host form's CB Radio; shell.cfg [port] cb_radio, written only when off).</summary>
    public bool CbRadio { get; set; } = true;
    /// <summary>The port's own: a hosted game's Handicaps rule (the host form's Hcp; shell.cfg [port] handicaps, "manual" or "auto", written only when on; an older file's true is manual).</summary>
    public I76.Sim.Missions.HandicapMode Handicaps { get; set; }
    /// <summary>The port's own: a hosted game allows the chase crosshair (the host form's Chase Crosshair; shell.cfg [port] host_crosshair, written only when on).</summary>
    public bool HostCrosshair { get; set; }
    /// <summary>The port's own: CB Voice (Play Options; shell.cfg [port] cb_voice, written only when off): push-to-talk voice in network games. Off neither sends nor plays it.</summary>
    public bool CbVoice { get; set; } = true;
    /// <summary>The port's own: the microphone CB Voice uses (shell.cfg [port] cb_mic; Play Options steps through the sound system's list). Empty is the system default.</summary>
    /// <summary>The CB voice chat's input device: empty is none, the default for anyone who has not chosen (the audio input is never opened); "Default" is the system's.</summary>
    public string CbMic { get; set; } = "";
    public static readonly string[] MapDownloadNames = ["Ask", "Always", "Never"];
    /// <summary>
    /// The port's own: Hidden Weapons &amp; Cars (Play Options; off, the stock game). The complete hidden weapons join CONFIGURE
    /// CHASSIS's pickers and the complete hidden models MAKE / MODEL, and a hosted game allows them (docs/hidden-content.md).
    /// </summary>
    public bool HiddenContent { get; set; }
    /// <summary>The port's own: Unlim. Turrets (Play Options; off, as the original): any number of turrets on a car, each on a roof mount (docs/deviations.md).</summary>
    public bool UnlimTurrets { get; set; }
    /// <summary>The port's own: Check for Updates (Play Options; shell.cfg [port] update_check, written only when off). On: each start asks GitHub, once, whether a newer release exists and says so under the version in the corner.</summary>
    public bool UpdateCheck { get; set; } = true;
    /// <summary>
    /// The port's own: Head Tracking (Play Options; shell.cfg [port] head_track = analog / digital, written only when
    /// not off): opentrack's "UDP over network" output turns the head in the car (<see cref="I76.Game.HeadLook"/>). Off, as the original.
    /// </summary>
    public I76.Game.HeadTrackMode HeadTrack { get; set; }
    /// <summary>Head tracking's sensitivity, the tracked angle's multiplier (Play Options; [port] head_track_sensitivity, written only when not 3).</summary>
    public float HeadTrackSensitivity { get; set; } = I76.Game.HeadLook.DefaultSensitivity;
    /// <summary>The UDP port opentrack sends to ([port] head_track_port, written only when not 4242; kept out of the menus).</summary>
    public int HeadTrackPort { get; set; } = I76.Game.HeadLook.DefaultPort;
    /// <summary>The address head tracking listens on ([port] head_track_bind, written only when not 127.0.0.1; 0.0.0.0 takes a tracker on another machine).</summary>
    public string HeadTrackBind { get; set; } = "127.0.0.1";
    /// <summary>The tracked x, y, z move the eye up to 10 cm each way ([port] head_track_position, written only when on; off).</summary>
    public bool HeadTrackPosition { get; set; }
    /// <summary>The port's own: Graphic Detail's Log File (user://logs/godot.log; off). Godot reads it at start, so a change shows from the next start (<see cref="I76.Game.Boot.LogFile"/>).</summary>
    public bool LogFile { get; set; }
    /// <summary>The port's own: Graphic Detail's Renderer (Auto, Vulkan, D3D12, OpenGL; shell.cfg [port] renderer, written only when not Auto). Godot reads it at start, so a change shows from the next start (<see cref="I76.Game.Boot.StartupSettings"/>).</summary>
    public I76.Game.Boot.RendererChoice Renderer { get; set; }
    /// <summary>A co-op seat is in the mission's own car (the default) rather than the player's pick on the melee forms' car panel.</summary>
    public bool CoopOwnCar { get; set; } = true;
    /// <summary>The port's own: a hosted co-op game's Co-op Scaling (the host form's; shell.cfg [port] coop_scale, written only when on): each enemy car gets a copy per player past the first, up to this many.</summary>
    public int CoopScale { get; set; }
    /// <summary>The port's own: a hosted co-op game's Co-op Damage (the host form's; shell.cfg [port] coop_damage, written only when not 1): the players' cars take this times the damage.</summary>
    public float CoopDamage { get; set; } = 1f;
    /// <summary>The port's own: a hosted co-op game's Rolling Start (the host form's; shell.cfg [port] coop_rolling_start, written only when on).</summary>
    public bool CoopRollingStart { get; set; }
    /// <summary>
    /// The port's own: Play Options' Melee A.I. (shell.cfg [port] melee_ai, written only when not Original): the skill an
    /// Instant Melee's computer cars open at (<see cref="I76.Sim.Missions.MeleeAiSkill"/>: the original's, the Difficulty Level's, easy or hard).
    /// </summary>
    public I76.Sim.Missions.MeleeAiSkill MeleeAi { get; set; }
    /// <summary>The row's values: the original's opening step is the bots' Normal tier (MeleeGame.OriginalOpeningStep).</summary>
    public static readonly string[] MeleeAiNames = ["Normal", "Difficulty" + PortMark, "Easy" + PortMark, "Hard" + PortMark];
    /// <summary>
    /// The port's own, config only (no row): the modern look's weapon effects as a percentage of their drawn size, brightness and
    /// opacity (shell.cfg [port] effects_size, effects_brightness, effects_opacity; 25 to 150, written only when not 100; <see cref="I76.Sim.Views.EffectsLook"/>).
    /// </summary>
    public int EffectsSize { get; set; } = I76.Sim.Views.EffectsLook.Default;
    public int EffectsBrightness { get; set; } = I76.Sim.Views.EffectsLook.Default;
    public int EffectsOpacity { get; set; } = I76.Sim.Views.EffectsLook.Default;
    /// <summary>The port's own, config only (no row): the modern look's scattered plants as a percentage of the Vegetation level's cover (shell.cfg [port] vegetation_density; 10 to 150, written only when not 60; <see cref="I76.Formats.Vegetation.VegetationDensity"/>).</summary>
    public int VegetationDensity { get; set; } = I76.Formats.Vegetation.VegetationDensity.Default;
    /// <summary>The port's own: CONFIGURE CHASSIS as a 3D garage (Graphic Detail's 3D Garage; shell.cfg [port] garage_3d, written only when off). On unless the player turned it off; off is the original's 2D form.</summary>
    public bool Garage3D { get; set; } = true;
    /// <summary>The port's own, without a row: the 3D garage's boombox ([port] garage_radio, written only when on). Off: the menu track goes on through the garage, as through the original's form.</summary>
    public bool GarageRadio { get; set; }
    /// <summary>The port's own, without a row: the modern look's car deaths by cause, burning wheels drawn per death (I76.Game.Sim.DeathVariety; [port] death_variety, written only when off). Off: one death for every cause, as the original's.</summary>
    public bool DeathVariety { get; set; } = true;
    /// <summary>The port's own, without a row: the modern look's briefing plays while the mission builds (I76.Game.Boot.BriefingOverlap; [port] briefing_while_loading, written only when off). Off: the build behind the loading card, then the briefing, as the original's.</summary>
    public bool BriefingWhileLoading { get; set; } = true;
    /// <summary>The port's own, without a row: the modern look's scattered plants answer the fighting (World/BrushImpacts.cs; [port] brush_impacts, written only when off).</summary>
    public bool BrushImpacts { get; set; } = true;
    /// <summary>
    /// The port's own, without a row: the modern look's lighting calibrated against the 1997 look's ([port] lighting_calibration,
    /// written only when not on; the earlier look_colour_match key is read when it is absent): each mission's sky light, exposure and
    /// ground and object tints solved so its ground and cars show the 1997 look's light (I76.Formats.Imaging.LightingCalibration),
    /// the frame shown through the 3D card's curve and the sky in its texture's colours.
    /// </summary>
    public bool LightingCalibration { get; set; } = true;

    /// <summary>[port] lighting_calibration=filmic: the calibration solved under the Filmic tonemap, the sky in the modern look's own colours (as before the card's curve).</summary>
    public bool LightingFilmic { get; set; }

    /// <summary>[port] lighting_calibration's word is "filmic".</summary>
    public static bool LightingFilmicOn(string? word) => string.Equals(word?.Trim(), "filmic", StringComparison.OrdinalIgnoreCase);

    /// <summary>[port] lighting_calibration's word, or when absent the earlier look_colour_match's: on unless "off".</summary>
    public static bool LightingCalibrationOn(string? word, string? earlier) =>
        !string.Equals((word ?? earlier)?.Trim(), "off", StringComparison.OrdinalIgnoreCase);
    /// <summary>The port's own: the modern look's cars show a cabin and driver behind their glass (Graphic Detail's Car Interiors; shell.cfg [port] car_interiors, written only when off). Off: the windows are plain reflective glass.</summary>
    public bool CarInteriors { get; set; } = true;
    /// <summary>The port's own: the chase view eases back behind the car a few seconds after the mouse or stick last moved (Mouse Options' Camera Return); off, it stays where it was left.</summary>
    public bool CameraReturn { get; set; } = true;
    /// <summary>The port's own: a switch of view, a cut-scene's hand-back and a respawn put the external views' wheel zoom back to the original's framing (Mouse Options' View Zoom Reset; shell.cfg [port] zoom_reset, written only when off). Off: each view keeps its zoom until the mission ends.</summary>
    public bool ZoomReset { get; set; } = true;
    /// <summary>The port's own: Graphic Detail's Field of View for the main 3D views (index into <see cref="I76.Sim.Views.ViewFov.Across"/>; shell.cfg [port] fov, degrees across the window, written only when not Monitor).</summary>
    public int FieldOfView { get; set; }
    /// <summary>The port's own: Ctrl+T "show all names" in a network or co-op game starts on, as the player left it (shell.cfg [port] net_names, written only when off). The original's table started off.</summary>
    public bool NetNames { get; set; } = true;
    /// <summary>The port's own: the map editor plays the game's soundtrack while you edit (the editor's MUSIC button; shell.cfg [port] editor_music, written only when off).</summary>
    public bool EditorMusic { get; set; } = true;
    /// <summary>
    /// The port's own, without a row (OriginalRecipe, the words): the rear-view mirror's far clip (I76.Game.Sim.OriginalBehavior:
    /// 0 Original, the original's 100 m; 1 Full, the whole view; shell.cfg [port] mirror_range). Null while the file has no key:
    /// it follows the look, Original in the 1997 look and Full in the modern one.
    /// </summary>
    public int? MirrorRangeChoice { get; set; }
    /// <summary>The port's own, without a row: the brake lamps, 0 Original (lit only while braking), 1 Held (0.15 s after); null follows the look ([port] brake_lamps).</summary>
    public int? BrakeLampChoice { get; set; }
    /// <summary>The port's own, without a row: a machine gun's round in the modern look, 0 Original (the one-pixel line), 1 Bolts; null follows the look ([port] mg_tracers).</summary>
    public int? MgTracerChoice { get; set; }
    /// <summary>
    /// The port's own, without a row: the 1997 sky's clouds, 0 Original (a second step a tick with the full mirror up, as the original's, which
    /// drifts them twice as fast then), 1 Steady, the default in both looks ([port] sky_drift, written when original; docs/deviations.md "Sky scroll").
    /// </summary>
    public int SkyDrift { get; set; } = 1;
    /// <summary>The port's own, without a row: the death orbit, 0 Original (8 degrees a tick), 1 Smooth (20 degrees a second) ([port] death_orbit, written when not original).</summary>
    public int DeathOrbit { get; set; }
    /// <summary>The port's own, without a row: mission music, 0 Original (on through track 14, then 2 again), 1 All (on through the disc's last) ([port] music_tracks, written when not original).</summary>
    public int MusicTracks { get; set; }
    /// <summary>The port's own: Play Options' Unlim. Ordnance, live rounds per ordnance kind, 0 Original (160, 40 online for the droppers), 1 Unlimited; offline only ([port] ordnance_limit, written when not original).</summary>
    public int OrdnanceLimit { get; set; }

    /// <summary>The port's own, kept out of the menus: [port] seed fixes every mission's rand seed, as --seed does (absent: the clock's, as the original).</summary>
    public uint? Seed { get; set; }
    /// <summary>The port's own, kept out of the menus: [port] telemetry, the telemetry feed's targets as I76_TELEMETRY_UDP takes them, and [port] telemetry_format (docs/dev-hooks.md). Absent: no feed.</summary>
    public string? Telemetry { get; set; }
    public string? TelemetryFormat { get; set; }

    /// <summary>The music level as a volume: off below 2, else −8 dB at 8 and 2 dB a step either side.</summary>
    public float MusicDb => Audio[0] < 2 ? -80f : -8f + (Audio[0] - 8) * 2f;

    /// <summary>
    /// The Play Options the simulation reads, onto a mission's combat: Unlim. Ammo (0x04), Unlim. Armor (0x08) and
    /// Unlim. Chas. Rein. (0x10), and the difficulty, the factor on the player's own rounds and flame. Ammo and the
    /// difficulty stay out of a network game, as the original keeps them (a network game plays Champion's ×1).
    /// The mission runs it at its start, on resuming from the pad and over a restored checkpoint's copies.
    /// </summary>
    public void ApplyTo(I76.Sim.Weapons.Combat c)
    {
        c.UnlimitedAmmo = (PlayOptions & 4) != 0 && !c.NetSession;
        if (c.Damage is I76.Sim.Damage.DamageModel dm)
        {
            dm.UnlimitedArmour = (PlayOptions & 8) != 0;
            dm.UnlimitedChassis = (PlayOptions & 0x10) != 0;
        }
        c.Difficulty = c.NetworkGame ? 1 : Math.Clamp(Difficulty, 0, 2);
    }

    public static ShellSettings Load()
    {
        var s = new ShellSettings();
        var c = new ConfigFile();
        if (c.Load(Path) != Error.Ok)
        {
            // a first start: the HD textures by the user folder and the adapter (HdTextures.FirstStartDefault)
            s.HdTextures = I76.Game.Assets.HdTextures.FirstStartDefault;
            I76.Game.Assets.HdTextures.Enabled = s.HdTextures;
            return s;
        }
        var g = (byte[])c.GetValue("shell", "graphics", s.Graphics);
        if (g.Length == 16) Array.Copy(g, s.Graphics, 16);
        // files from before the mirror and the external instruments did anything carry the shipped Low / Off: the port's defaults instead
        int version = (int)c.GetValue("shell", "version", 1);
        if (version < 2) { s.Graphics[0xd] = DefaultGraphics[0xd]; s.Graphics[0xe] = DefaultGraphics[0xe]; }
        // and from before +0x8 was the anti-aliasing: the shipped byte is not a choice, so the default (2x)
        if (version < 3) s.Graphics[0x8] = DefaultGraphics[0x8];
        var a = (byte[])c.GetValue("shell", "audio", s.Audio);
        if (a.Length == 4) Array.Copy(a, s.Audio, 4);
        (int, int)? Read(string section) => c.HasSectionKey(section, "play") || c.HasSectionKey(section, "difficulty")
            ? ((int)c.GetValue(section, "play", 0), (int)c.GetValue(section, "difficulty", 0)) : null;
        (s.PlayOptions, s.Difficulty) = I76.Formats.Shell.PlayProfile.Resolve(Nitro, Nitro ? Read("nitro") : Read("shell"), Read("shell"));
        s.ModernPhysics = (bool)c.GetValue("port", "modern_physics", false);
        // version 4 added the HD menu art switch: older files have none, so it is on
        s.HdMenuArt = HdAssets.FromFile(c.HasSectionKey("port", "hd_menu_art") ? (bool)c.GetValue("port", "hd_menu_art") : null, version);
        // no version step: absent, the menu art's switch, which turned these off too before they had a row of their own
        s.HdTextures = c.HasSectionKey("port", "hd_textures") ? (bool)c.GetValue("port", "hd_textures")
            : I76.Game.Assets.HdTextures.FromFile(null, s.HdMenuArt, I76.Game.Assets.HdTextures.FirstStartDefault);
        // no version step: absent, 4 (a plain tex-world from before the sizes is used whatever the size)
        s.HdTextureSize = I76.Formats.Shell.HdPackFolders.Normalize((int)c.GetValue("port", "hd_texture_size", I76.Formats.Shell.HdPackFolders.DefaultSize));
        // version 5 added Auto Reverse: older files have none, so it is off
        s.AutoReverse = version >= 5 && (bool)c.GetValue("port", "auto_reverse", false);
        // version 6 added the look: older files have none, so it is off
        s.Look1997 = version >= 6 && (bool)c.GetValue("port", "look_1997", false);
        // version 7 added Force Feedback: older files have none, so it is on
        s.ForceFeedback = version < 7 || (bool)c.GetValue("port", "force_feedback", true);
        // no version step: absent, the pad at full strength on the first device, as before the keys
        s.ForceFeedbackWheel = (bool)c.GetValue("port", "ffb_wheel", false);
        s.FfbStrength = Math.Clamp((int)c.GetValue("port", "ffb_strength", 100), 0, 100);
        s.FfbDevice = (string)c.GetValue("port", "ffb_device", "");
        // no version step: absent, the game's own effects at full strength. Before version 13 a Rumble row of its own could be Off with
        // Force Feedback on: that is Force Feedback Off now, unless the wheel was chosen (the pad was silent under a wheel anyway)
        s.RumbleMode = Enum.TryParse<I76.Formats.Input.RumbleMode>((string)c.GetValue("port", "rumble", "original"), true, out var rm) ? rm : I76.Formats.Input.RumbleMode.Original;
        if (s.RumbleMode == I76.Formats.Input.RumbleMode.Off) { s.RumbleMode = I76.Formats.Input.RumbleMode.Original; if (!s.ForceFeedbackWheel) s.ForceFeedback = false; }
        s.RumbleStrength = I76.Formats.Input.RumbleStrength.Clamp((int)c.GetValue("port", "rumble_strength", I76.Formats.Input.RumbleStrength.Default));
        // no version step: absent, Stereo. A build before the Output row had [port] tactile = lfe for the LFE path: that is 5.1 now;
        // 2.1, the stereo mix, is Stereo with the level above 0
        string? output = c.HasSectionKey("port", "output") ? c.GetValue("port", "output", "").ToString()
            : string.Equals(c.GetValue("port", "tactile", "off").ToString(), "lfe", StringComparison.OrdinalIgnoreCase) ? "5.1" : null;
        s.Output = I76.Formats.Input.AudioOutputs.Parse(output);
        // absent, 0 (none); before that default an absent level was 100, which a file that chose 2.1, 5.1 or 7.1 keeps
        s.LfeLevel = I76.Formats.Input.AudioOutputs.ClampLevel((int)c.GetValue("port", "lfe_level",
            I76.Formats.Input.AudioOutputs.HadShaker(output) ? I76.Formats.Input.AudioOutputs.OldDefaultLevel : I76.Formats.Input.AudioOutputs.DefaultLevel));
        // no version step: absent, 100 (each source as made)
        s.LfeSources = [.. I76.Formats.Input.TactileSources.Keys.Select((k, i) => I76.Formats.Input.TactileSources.Clamp((int)c.GetValue("port", k, I76.Formats.Input.TactileSources.DefaultOf(i))))];
        s.TactileEq = c.GetValue("port", "tactile_eq", "").ToString();
        // no version step: absent, the defaults (the game's sounds and the movies set to sit together)
        s.GameEq = c.GetValue("port", "game_eq", "").ToString();
        s.MovieEq = c.GetValue("port", "movie_eq", "").ToString();
        s.Crossover = I76.Formats.Input.AudioOutputs.ClampCrossover((float)c.GetValue("port", "crossover", I76.Formats.Input.AudioOutputs.DefaultCrossover));
        // version 8 added the simulation rate: older files have none, so it is the default (20)
        s.SimRate = version < 8 ? I76.Sim.Session.SimRate.Default.Label : I76.Sim.Session.SimRate.FromLabel((int)c.GetValue("port", "sim_rate", I76.Sim.Session.SimRate.Default.Label)).Label;
        // the warning flag has no version: an older file has none, so the warning is still due
        s.SimRateWarned = (bool)c.GetValue("port", "sim_rate_warned", false);
        // version 9 added Checkpoints: older files have none, so it is off
        s.Checkpoints = version >= 9 && (bool)c.GetValue("port", "checkpoints", false);
        // version 10 added Vegetation: older files have none, so it is Low (the default)
        // as 0.2.0 left it, so they are Off (High, chosen, stays)
        s.Vegetation = version < 10 ? 1 : Math.Clamp((int)c.GetValue("port", "vegetation", 1), 0, 2);
        s.Dash3D = (bool)c.GetValue("port", "dash_3d", true);
        // no version step: absent, off (Shadow Detail High turned SSAO on before the row; the row's default is off)
        s.AmbientOcclusion = (bool)c.GetValue("port", "ambient_occlusion", false);
        // a file without the key has no choice, and the look's default applies; version 12 drops the Far 0.11.0 wrote on every save
        s.DrawDistanceChoice = I76.Formats.Imaging.DrawDistance.FromFile(c.HasSectionKey("port", "draw_distance") ? (int)c.GetValue("port", "draw_distance") : null, version);
        // [port] map_edge (Hide Map Edge Cliffs, withdrawn: docs/deviations.md) is no longer read, as any key this version does not know; Save drops it
        // no version step: a file without these keys has them off, which is what they were before they existed
        s.SoftwareRenderer = (bool)c.GetValue("port", "software_renderer", false);
        s.PaletteGrade = (bool)c.GetValue("port", "palette_grade", false);
        s.CrashBlur = I76.Sim.Views.CrashBlurMode.Parse((string)c.GetValue("port", "crash_blur", "on"));
        // no version step: absent, the mouse camera is on at Normal, and mouse_sensitivity migrates from mouse_camera
        s.MouseCamera = Math.Clamp((int)c.GetValue("port", "mouse_camera", I76.Sim.Views.MouseLook.Normal), 0, I76.Sim.Views.MouseLook.Names.Length - 1);
        s.MouseSensitivity = c.HasSectionKey("port", "mouse_sensitivity")
            ? I76.Sim.Views.MouseLook.ClampSensitivity((float)c.GetValue("port", "mouse_sensitivity", I76.Sim.Views.MouseLook.DefaultSensitivity))
            : I76.Sim.Views.MouseLook.SensitivityFromLevel(s.MouseCamera);
        s.MouseInvertY = (bool)c.GetValue("port", "mouse_invert_y", false);
        s.MouseCockpit = (bool)c.GetValue("port", "mouse_cockpit", false);
        s.ChaseCrosshair = (bool)c.GetValue("port", "chase_crosshair", false);
        s.WheelCycleWeapon = (bool)c.GetValue("port", "wheel_cycle_weapon", false);
        // no version step: absent, no cap and vsync on, as before the keys
        // no version step: absent, the desktop
        s.Resolution = I76.Game.Boot.WindowSizes.Parse((string)c.GetValue("port", "resolution", "")) is { } rs ? I76.Game.Boot.WindowSizes.Format(rs.W, rs.H) : "";
        // no version step: hood_gloss, else the earlier hood_finish's word, else the older hood_matte (each read only, as look_colour_match)
        s.HoodGloss = I76.Sim.Views.PaintFinish.Parse(c.HasSectionKey("port", "hood_gloss") ? c.GetValue("port", "hood_gloss").ToString() : null,
            c.HasSectionKey("port", "hood_finish") ? c.GetValue("port", "hood_finish").ToString() : null, (bool)c.GetValue("port", "hood_matte", false));
        // no version step: absent, 100 %
        s.RenderScale = I76.Game.Boot.RenderScale.Parse((int)c.GetValue("port", "render_scale", 100));
        s.MaxFps = I76.Sim.Views.FrameLimit.Parse(c.HasSectionKey("port", "max_fps") ? (int)c.GetValue("port", "max_fps") : null);
        s.VSync = (bool)c.GetValue("port", "vsync", true);
        s.KeepMouseInWindow = (bool)c.GetValue("port", "keep_mouse_in_window", false);
        if (c.HasSectionKey("port", "seed")) s.Seed = unchecked((uint)(long)c.GetValue("port", "seed"));
        if (c.HasSectionKey("port", "telemetry")) s.Telemetry = (string)c.GetValue("port", "telemetry");
        if (c.HasSectionKey("port", "telemetry_format")) s.TelemetryFormat = (string)c.GetValue("port", "telemetry_format");
        // no version step either: absent, a host's map is asked about and a hosted one shared, as before the keys
        s.MapDownloads = Math.Clamp((int)c.GetValue("port", "map_downloads", 0), 0, 2);
        s.ShareMaps = (bool)c.GetValue("port", "share_maps", true);
        s.CarReset = (bool)c.GetValue("port", "car_reset", true);
        s.IgnitionStaysOn = (bool)c.GetValue("port", "ignition_stays_on", false);
        s.CbRadio = (bool)c.GetValue("port", "cb_radio", true);
        var hcp = c.GetValue("port", "handicaps", false);
        s.Handicaps = hcp.VariantType == Variant.Type.Bool ? ((bool)hcp ? I76.Sim.Missions.HandicapMode.Manual : I76.Sim.Missions.HandicapMode.Off)
            : hcp.VariantType == Variant.Type.String && (string)hcp == "auto" ? I76.Sim.Missions.HandicapMode.Auto
            : hcp.VariantType == Variant.Type.String && (string)hcp == "manual" ? I76.Sim.Missions.HandicapMode.Manual : I76.Sim.Missions.HandicapMode.Off;
        s.HostCrosshair = (bool)c.GetValue("port", "host_crosshair", false);
        s.CbVoice = (bool)c.GetValue("port", "cb_voice", true);
        // no version step: absent (never chosen, or the old implicit default) is none; a saved device stays
        s.CbMic = (string)c.GetValue("port", "cb_mic", "");
        // no version step: absent, it is off, the stock game
        s.HiddenContent = (bool)c.GetValue("port", "hidden_content", false);
        s.UnlimTurrets = (bool)c.GetValue("port", "unlim_turrets", false) || System.Environment.GetEnvironmentVariable("I76_UNLIM_TURRETS") == "1";
        s.UpdateCheck = (bool)c.GetValue("port", "update_check", true);
        // no version step: absent, head tracking is off at its defaults
        s.HeadTrack = Enum.TryParse<I76.Game.HeadTrackMode>((string)c.GetValue("port", "head_track", "off"), true, out var ht) ? ht : I76.Game.HeadTrackMode.Off;
        s.HeadTrackSensitivity = Math.Clamp((float)(double)c.GetValue("port", "head_track_sensitivity", (double)I76.Game.HeadLook.DefaultSensitivity), 0.1f, 10f);
        s.HeadTrackPort = Math.Clamp((int)c.GetValue("port", "head_track_port", I76.Game.HeadLook.DefaultPort), 1, 65535);
        s.HeadTrackBind = (string)c.GetValue("port", "head_track_bind", "127.0.0.1");
        s.HeadTrackPosition = (bool)c.GetValue("port", "head_track_position", false);
        s.CoopOwnCar = (bool)c.GetValue("port", "coop_own_car", true);
        // no version step: absent, off (the mission's own cars alone)
        s.CoopScale = Math.Clamp((int)c.GetValue("port", "coop_scale", 0), 0, I76.Sim.Missions.CoopSetup.MaxExtraEnemies);
        // no version step: absent, the original's damage
        s.CoopDamage = (float)(double)c.GetValue("port", "coop_damage", 1.0) is var cd && System.Array.IndexOf(I76.Sim.Missions.CoopSetup.DamageFactors, cd) >= 0 ? cd : 1f;
        // no version step: absent, off (a fresh car stands still)
        s.CoopRollingStart = (bool)c.GetValue("port", "coop_rolling_start", false);
        // no version step: absent, the original's opening skill
        s.MeleeAi = (I76.Sim.Missions.MeleeAiSkill)Math.Clamp((int)c.GetValue("port", "melee_ai", 0), 0, MeleeAiNames.Length - 1);
        // no version step: absent, the effects as drawn (100)
        string? Pct(string key) => c.HasSectionKey("port", key) ? c.GetValue("port", key).ToString() : null;
        s.EffectsSize = I76.Sim.Views.EffectsLook.Parse(Pct("effects_size"));
        s.EffectsBrightness = I76.Sim.Views.EffectsLook.Parse(Pct("effects_brightness"));
        s.EffectsOpacity = I76.Sim.Views.EffectsLook.Parse(Pct("effects_opacity"));
        // no version step: absent, 60 (the plants were placed at the full level before the key)
        s.VegetationDensity = I76.Formats.Vegetation.VegetationDensity.Parse(Pct("vegetation_density"));
        // no version step: absent, the 3D garage is on
        s.Garage3D = (bool)c.GetValue("port", "garage_3d", true);
        s.GarageRadio = string.Equals(c.GetValue("port", "garage_radio", "off").ToString(), "on", StringComparison.OrdinalIgnoreCase);
        s.DeathVariety = !string.Equals(c.GetValue("port", "death_variety", "on").ToString(), "off", StringComparison.OrdinalIgnoreCase);
        s.BriefingWhileLoading = !string.Equals(c.GetValue("port", "briefing_while_loading", "on").ToString(), "off", StringComparison.OrdinalIgnoreCase);
        s.BrushImpacts = !string.Equals(c.GetValue("port", "brush_impacts", "on").ToString(), "off", StringComparison.OrdinalIgnoreCase);
        string? lighting = c.HasSectionKey("port", "lighting_calibration") ? c.GetValue("port", "lighting_calibration").ToString() : null;
        s.LightingCalibration = LightingCalibrationOn(lighting, c.HasSectionKey("port", "look_colour_match") ? c.GetValue("port", "look_colour_match").ToString() : null);
        s.LightingFilmic = s.LightingCalibration && LightingFilmicOn(lighting);
        s.CarInteriors = (bool)c.GetValue("port", "car_interiors", true) && System.Environment.GetEnvironmentVariable("I76_CAR_INTERIORS") != "0";
        s.CameraReturn = (bool)c.GetValue("port", "camera_return", true);
        s.ZoomReset = (bool)c.GetValue("port", "zoom_reset", true);
        // no version step: absent, Monitor (the original's height, which is what a file without the key meant before; a value from
        // then, degrees across 4:3, is read as degrees across the window)
        s.FieldOfView = I76.Sim.Views.ViewFov.Parse((int)c.GetValue("port", "fov", 0));
        s.NetNames = (bool)c.GetValue("port", "net_names", true);
        s.EditorMusic = (bool)c.GetValue("port", "editor_music", true);
        // the settings without a row (docs/playing.md). No version step: absent, the original's (the look's for the three that follow it),
        // but the sky's drift, Steady: version 13 made that the default, and the key was only ever written for Steady before it
        s.MirrorRangeChoice = OriginalRecipe.ReadChoice(c, "mirror_range", OriginalRecipe.MirrorRangeNames);
        s.BrakeLampChoice = OriginalRecipe.ReadChoice(c, "brake_lamps", OriginalRecipe.BrakeLampNames);
        s.MgTracerChoice = OriginalRecipe.ReadChoice(c, "mg_tracers", OriginalRecipe.MgTracerNames);
        s.SkyDrift = OriginalRecipe.ReadChoice(c, "sky_drift", OriginalRecipe.SkyDriftNames) ?? 1;
        s.DeathOrbit = OriginalRecipe.ReadChoice(c, "death_orbit", OriginalRecipe.DeathOrbitNames) ?? 0;
        s.MusicTracks = OriginalRecipe.ReadChoice(c, "music_tracks", OriginalRecipe.MusicTrackNames) ?? 0;
        s.OrdnanceLimit = OriginalRecipe.ReadChoice(c, "ordnance_limit", OriginalRecipe.OrdnanceNames) ?? 0;

        // no version step: absent, it is off. The override file is what Godot reads; the setting is the player's word for it
        s.LogFile = (bool)c.GetValue("port", "log_file", false);
        // no version step: absent, it is off (players never see the panel); developer_tools is the key's older name, still read
        s.DeveloperTools = DeveloperWord(c.GetValue("port", "developer", false).ToString()) || DeveloperWord(c.GetValue("port", "developer_tools", false).ToString());
        s.DevFps = (bool)c.GetValue("port", "dev_fps", false);
        s.Renderer = Enum.TryParse<I76.Game.Boot.RendererChoice>((string)c.GetValue("port", "renderer", "Auto"), true, out var rc) ? rc : I76.Game.Boot.RendererChoice.Auto;
        HdAssets.Enabled = s.HdMenuArt;
        I76.Game.Assets.HdTextures.Enabled = s.HdTextures;
        I76.Game.Assets.HdTextures.Size = s.HdTextureSize;
        return s;
    }

    public void Save()
    {
        var c = new ConfigFile();
        // version 13: the sky's drift is Steady unless the file says original, and the Rumble row is folded into Force Feedback (the keys are the same)
        c.SetValue("shell", "version", 13);
        c.SetValue("port", "vegetation", Vegetation);
        if (DrawDistanceChoice is { } dd) c.SetValue("port", "draw_distance", dd);
        c.SetValue("port", "checkpoints", Checkpoints);
        c.SetValue("port", "force_feedback", ForceFeedback);
        if (ForceFeedbackWheel) c.SetValue("port", "ffb_wheel", true);
        if (FfbStrength != 100) c.SetValue("port", "ffb_strength", FfbStrength);
        if (FfbDevice.Length > 0) c.SetValue("port", "ffb_device", FfbDevice);
        if (RumbleMode != I76.Formats.Input.RumbleMode.Original) c.SetValue("port", "rumble", RumbleMode.ToString().ToLowerInvariant());
        if (Output != I76.Formats.Input.AudioOutput.Stereo) c.SetValue("port", "output", I76.Formats.Input.AudioOutputs.Key(Output));
        if (LfeLevel != I76.Formats.Input.AudioOutputs.DefaultLevel || I76.Formats.Input.AudioOutputs.Discrete(Output)) c.SetValue("port", "lfe_level", LfeLevel);
        for (int i = 0; i < I76.Formats.Input.TactileSources.Keys.Length; i++)
            if (i < LfeSources.Length && LfeSources[i] != I76.Formats.Input.TactileSources.DefaultOf(i)) c.SetValue("port", I76.Formats.Input.TactileSources.Keys[i], LfeSources[i]);
        if (TactileEq.Length > 0) c.SetValue("port", "tactile_eq", TactileEq);
        if (GameEq.Length > 0) c.SetValue("port", "game_eq", GameEq);
        if (MovieEq.Length > 0) c.SetValue("port", "movie_eq", MovieEq);
        if (Crossover != I76.Formats.Input.AudioOutputs.DefaultCrossover) c.SetValue("port", "crossover", (double)Crossover);
        if (RumbleStrength != I76.Formats.Input.RumbleStrength.Default) c.SetValue("port", "rumble_strength", RumbleStrength);
        c.SetValue("port", "sim_rate", SimRate);
        if (SimRateWarned) c.SetValue("port", "sim_rate_warned", true);
        if (AutoReverse) c.SetValue("port", "auto_reverse", true);
        if (Look1997) c.SetValue("port", "look_1997", true);
        if (!Dash3D) c.SetValue("port", "dash_3d", false);
        if (AmbientOcclusion) c.SetValue("port", "ambient_occlusion", true);
        if (SoftwareRenderer) c.SetValue("port", "software_renderer", true);
        if (PaletteGrade) c.SetValue("port", "palette_grade", true);
        if (CrashBlur != I76.Sim.Views.CrashBlurMode.On) c.SetValue("port", "crash_blur", I76.Sim.Views.CrashBlurMode.Word(CrashBlur));
        c.SetValue("port", "mouse_camera", MouseCamera);
        c.SetValue("port", "mouse_sensitivity", I76.Sim.Views.MouseLook.ClampSensitivity(MouseSensitivity));
        if (MouseInvertY) c.SetValue("port", "mouse_invert_y", true);
        if (Resolution.Length > 0) c.SetValue("port", "resolution", Resolution);
        if (I76.Sim.Views.PaintFinish.FileValue(HoodGloss) is { } hoodGloss) c.SetValue("port", "hood_gloss", hoodGloss);
        if (RenderScale != I76.Game.Boot.RenderScale.Default) c.SetValue("port", "render_scale", I76.Game.Boot.RenderScale.Percents[Math.Clamp(RenderScale, 0, I76.Game.Boot.RenderScale.Percents.Length - 1)]);
        if (MouseCockpit) c.SetValue("port", "mouse_cockpit", true);
        if (ChaseCrosshair) c.SetValue("port", "chase_crosshair", true);
        if (WheelCycleWeapon) c.SetValue("port", "wheel_cycle_weapon", true);
        c.SetValue("port", "max_fps", I76.Sim.Views.FrameLimit.Fps(MaxFps));
        c.SetValue("port", "vsync", VSync);
        if (KeepMouseInWindow) c.SetValue("port", "keep_mouse_in_window", true);
        if (DeveloperTools) c.SetValue("port", "developer", true);
        if (DevFps) c.SetValue("port", "dev_fps", true);
        c.SetValue("port", "hd_menu_art", HdMenuArt);
        c.SetValue("port", "hd_textures", HdTextures);
        c.SetValue("port", "hd_texture_size", HdTextureSize);
        c.SetValue("shell", "graphics", Graphics);
        c.SetValue("shell", "audio", Audio);
        // the other game's flags and difficulty stay as the file has them
        var old = new ConfigFile();
        if (old.Load(Path) == Error.Ok)
        {
            string other = Nitro ? "shell" : "nitro";
            foreach (var k in new[] { "play", "difficulty" })
                if (old.HasSectionKey(other, k)) c.SetValue(other, k, old.GetValue(other, k));
        }
        string mine = Nitro ? "nitro" : "shell";
        c.SetValue(mine, "play", I76.Formats.Shell.PlayProfile.Mask(Nitro, PlayOptions));
        c.SetValue(mine, "difficulty", Difficulty);
        if (ModernPhysics) c.SetValue("port", "modern_physics", true);
        if (Seed is { } seed) c.SetValue("port", "seed", (long)seed);
        if (Telemetry is { Length: > 0 } telemetry) c.SetValue("port", "telemetry", telemetry);
        if (TelemetryFormat is { Length: > 0 } telemetryFormat) c.SetValue("port", "telemetry_format", telemetryFormat);
        if (MapDownloads != 0) c.SetValue("port", "map_downloads", MapDownloads);
        if (!ShareMaps) c.SetValue("port", "share_maps", false);
        if (!CarReset) c.SetValue("port", "car_reset", false);
        if (IgnitionStaysOn) c.SetValue("port", "ignition_stays_on", true);
        if (!CbRadio) c.SetValue("port", "cb_radio", false);
        if (Handicaps != I76.Sim.Missions.HandicapMode.Off) c.SetValue("port", "handicaps", Handicaps == I76.Sim.Missions.HandicapMode.Auto ? "auto" : "manual");
        if (HostCrosshair) c.SetValue("port", "host_crosshair", true);
        if (!CbVoice) c.SetValue("port", "cb_voice", false);
        if (CbMic.Length > 0) c.SetValue("port", "cb_mic", CbMic);
        if (HiddenContent) c.SetValue("port", "hidden_content", true);
        if (UnlimTurrets) c.SetValue("port", "unlim_turrets", true);
        if (!UpdateCheck) c.SetValue("port", "update_check", false);
        if (HeadTrack != I76.Game.HeadTrackMode.Off) c.SetValue("port", "head_track", HeadTrack.ToString().ToLowerInvariant());
        if (HeadTrackSensitivity != I76.Game.HeadLook.DefaultSensitivity) c.SetValue("port", "head_track_sensitivity", (double)HeadTrackSensitivity);
        if (HeadTrackPort != I76.Game.HeadLook.DefaultPort) c.SetValue("port", "head_track_port", HeadTrackPort);
        if (HeadTrackBind != "127.0.0.1") c.SetValue("port", "head_track_bind", HeadTrackBind);
        if (HeadTrackPosition) c.SetValue("port", "head_track_position", true);
        if (!CoopOwnCar) c.SetValue("port", "coop_own_car", false);
        if (CoopScale != 0) c.SetValue("port", "coop_scale", CoopScale);
        if (CoopDamage != 1f) c.SetValue("port", "coop_damage", (double)CoopDamage);
        if (CoopRollingStart) c.SetValue("port", "coop_rolling_start", true);
        if (MeleeAi != I76.Sim.Missions.MeleeAiSkill.Original) c.SetValue("port", "melee_ai", (int)MeleeAi);
        if (EffectsSize != I76.Sim.Views.EffectsLook.Default) c.SetValue("port", "effects_size", EffectsSize);
        if (EffectsBrightness != I76.Sim.Views.EffectsLook.Default) c.SetValue("port", "effects_brightness", EffectsBrightness);
        if (EffectsOpacity != I76.Sim.Views.EffectsLook.Default) c.SetValue("port", "effects_opacity", EffectsOpacity);
        if (VegetationDensity != I76.Formats.Vegetation.VegetationDensity.Default) c.SetValue("port", "vegetation_density", VegetationDensity);
        if (!Garage3D) c.SetValue("port", "garage_3d", false);
        if (GarageRadio) c.SetValue("port", "garage_radio", "on");
        if (!DeathVariety) c.SetValue("port", "death_variety", "off");
        if (!BriefingWhileLoading) c.SetValue("port", "briefing_while_loading", "off");
        if (!BrushImpacts) c.SetValue("port", "brush_impacts", "off");
        if (!LightingCalibration) c.SetValue("port", "lighting_calibration", "off");
        else if (LightingFilmic) c.SetValue("port", "lighting_calibration", "filmic");
        if (!CarInteriors) c.SetValue("port", "car_interiors", false);
        if (!CameraReturn) c.SetValue("port", "camera_return", false);
        if (!ZoomReset) c.SetValue("port", "zoom_reset", false);
        if (FieldOfView != 0) c.SetValue("port", "fov", I76.Sim.Views.ViewFov.Across[Math.Clamp(FieldOfView, 0, I76.Sim.Views.ViewFov.Across.Length - 1)]);
        if (!NetNames) c.SetValue("port", "net_names", false);
        if (!EditorMusic) c.SetValue("port", "editor_music", false);
        OriginalRecipe.WriteChoice(c, "mirror_range", OriginalRecipe.MirrorRangeNames, MirrorRangeChoice);
        OriginalRecipe.WriteChoice(c, "brake_lamps", OriginalRecipe.BrakeLampNames, BrakeLampChoice);
        OriginalRecipe.WriteChoice(c, "mg_tracers", OriginalRecipe.MgTracerNames, MgTracerChoice);
        OriginalRecipe.WriteChoice(c, "sky_drift", OriginalRecipe.SkyDriftNames, SkyDrift == 1 ? null : SkyDrift);
        OriginalRecipe.WriteChoice(c, "death_orbit", OriginalRecipe.DeathOrbitNames, DeathOrbit == 0 ? null : DeathOrbit);
        OriginalRecipe.WriteChoice(c, "music_tracks", OriginalRecipe.MusicTrackNames, MusicTracks == 0 ? null : MusicTracks);
        OriginalRecipe.WriteChoice(c, "ordnance_limit", OriginalRecipe.OrdnanceNames, OrdnanceLimit == 0 ? null : OrdnanceLimit);

        if (LogFile) c.SetValue("port", "log_file", true);
        if (Renderer != I76.Game.Boot.RendererChoice.Auto) c.SetValue("port", "renderer", Renderer.ToString());
        c.Save(Path);
        I76.Game.Boot.StartupSettings.Write(LogFile, Renderer, MicNames.OpensInput(CbMic));
    }
}
