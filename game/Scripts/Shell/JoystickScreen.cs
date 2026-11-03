using Godot;
using I76.Formats.Input;
using I76.Formats.Shell;

namespace I76.Game.Shell;

/// <summary>
/// The controller screen for a device that has no standard gamepad layout, a flight stick or a wheel (docs/joystick.md): Calibrate opens
/// this instead of <see cref="ControllerScreen"/>'s drawn pad when joystick1 is such a device. Godot does not say how many axes and
/// buttons it has, so the lists grow as inputs are used: move an axis or press a button and its row appears, is marked while it is held and is
/// selected. A click on a row (or Enter) opens the shell's list of what it can do; an axis can also be inverted. The dead zone is one
/// setting for the stick. It edits only the joystick blocks of its own copy of the bindings; Done hands the copy to Control Configuration, Cancel
/// and Esc hand back what it was given. Drawn in the shell's pixels in both looks, on the controller sheet's paper and rules. With more than one
/// such device (a wheel and a stick together) a row in the middle (or Tab) steps through them, and the lists, the edits and Restore are that device's.
/// </summary>
public sealed class JoystickScreen : OverlayScreen
{
    protected override int Record => 16;
    private readonly BindingTable _given, _map;
    private ShellFont _f = null!;
    private bool _loading;
    private string? _sel;
    private readonly HashSet<int> _axes = [], _buttons = [];
    private readonly HashSet<string> _hats = [];
    private readonly bool[] _axisHeld = new bool[GenericStick.MaxAxes];
    private uint _lastButtons;
    private byte _lastHat;
    private string _lastLive = "";
    /// <summary>The device edited: an index into <see cref="Controls.GenericDevices"/> (0 when there is one).</summary>
    private int _dev;
    private int DeviceIndex => Controls.GenericDevices is { Count: > 0 } g ? g[Math.Min(_dev, g.Count - 1)] : 0;
    /// <summary>The device's name in input.map: joystickN.</summary>
    private string Dev => $"joystick{DeviceIndex + 1}";

    private const string Title = "JOYSTICK CONFIGURATION";
    private static readonly string[] SlotText = ["", "RESTORE DEFAULTS", "DONE", "CANCEL"];
    private static readonly string[] AxisItems = ["Accelerate / Brake", "Steer", "Look left / right", "Look up / down", "Invert this axis"];
    /// <summary>An axis's further items: a command past half its travel one way or the other (PadRules.AxisThreshold).</summary>
    private static readonly string[] ThresholdItems = ["Pushed + : a command...", "Pushed - : a command..."];
    /// <summary>The commands a threshold holds while past (the rest it presses once a push).</summary>
    private static readonly HashSet<string> HeldCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "e_brake", "weapon_fire", "hardpoint1_fire", "hardpoint2_fire", "hardpoint3_fire", "hardpoint4_fire", "hardpoint5_fire", "HONK_HORN",
        "pilot_glance_up", "pilot_glance_down", "pilot_glance_left", "pilot_glance_right", "pilot_glance_target", "throttle_up", "throttle_down", "steer_left", "steer_right",
    };
    private static string CommandName(string command) => Array.FindIndex(Controls.Commands, c => c.Equals(command, StringComparison.OrdinalIgnoreCase)) is var i && Entries.Contains(i) ? NameOf(i) : command;
    private static readonly string[] AxisRows = ["throttle", "steer", "track_yaw_delta", "track_pitch_delta"];
    private static readonly int[] Entries = [.. ControlsScreen.LeftRows, .. ControlsScreen.RightRows];
    private static string NameOf(int entry) => ControlsScreen.Names[Array.IndexOf(Entries, entry)].Text;

    public JoystickScreen(BindingTable pending)
    {
        _given = pending;
        _map = pending.Clone();
        _map.AddPadSideIfNone(Controls.PadDefaults());
    }

    public override void Enter()
    {
        Label = Shell.Db.Font(0x46);
        Small = Shell.Db.Font(0x47);
        _f = Shell.Db.Font(0x44);
        _loading = true;
        Build();
    }

    protected override void Build()
    {
        // the dead zone, in the middle column; the sheet's buttons are on the original's slots
        Add(175, ControllerScreen.RowY(3) + 3, () => "", () =>
        {
            Shell.Sound(82);
            GodotPads.DeadZone = GenericStick.NextDeadZone(GodotPads.DeadZone);
        }, _f, w: 290, h: ControllerScreen.RowH - 4);
        // row 4: whether buttons 12 to 15 are the hat (Godot cannot tell them apart)
        Add(175, ControllerScreen.RowY(4) + 3, () => "", () => { if (GodotPads.HatsKnown()) return; Shell.Sound(82); GodotPads.HatOnButtons = !GodotPads.HatOnButtons; }, _f, w: 290, h: ControllerScreen.RowH - 4);
        // row 13: the device edited, with two or more
        Add(175, ControllerScreen.RowY(13) + 3, () => "", NextDevice, _f, w: 290, h: ControllerScreen.RowH - 4);
        var slots = ControllerScreen.Slots;
        Add(slots[1].Position.X, slots[1].Position.Y, () => "", Restore, w: slots[1].Size.X, h: slots[1].Size.Y);
        Add(slots[2].Position.X, slots[2].Position.Y, () => "", Done, w: slots[2].Size.X, h: slots[2].Size.Y);
        Add(slots[3].Position.X, slots[3].Position.Y, () => "", Back, w: slots[3].Size.X, h: slots[3].Size.Y);
    }

    /// <summary>The next device, with more than one: its rows from scratch.</summary>
    private void NextDevice()
    {
        if (Controls.GenericDevices.Count < 2) return;
        Shell.Sound(82);
        _dev = (_dev + 1) % Controls.GenericDevices.Count;
        _axes.Clear(); _buttons.Clear(); _hats.Clear(); Array.Clear(_axisHeld); _lastButtons = 0; _lastHat = 0; _sel = null;
        Redraw();
    }

    /// <summary>Restore Defaults: joystick1's preset on joystick1 (the other devices kept); a second device its own preset if it is a known one, else none (cleared).</summary>
    private void Restore()
    {
        Shell.Sound(82);
        _map.ResetDevice(DeviceIndex == 0 ? Controls.PadDefaults() : Controls.PresetOf(DeviceIndex) is { } p ? DevicePresets.Apply(new BindingTable(), p, Dev, false) : new BindingTable(), Dev);
        Redraw();
    }

    private void Back() => Shell.Overlay(new ControlsScreen(_given));
    private void Done() => Shell.Overlay(new ControlsScreen(_map));
    protected override void Escape() => Back();

    // ---- the rows ---------------------------------------------------------------------------------------

    private sealed record Row(string Channel, bool Left, string[] Lines, bool Live)
    {
        public int Slot;
        public Rect2I Rect => new(Left ? ControllerScreen.LeftRule - 145 : ControllerScreen.RightRule + 1, ControllerScreen.RowY(Slot), 144, Lines.Length * ControllerScreen.RowH);
    }

    private readonly List<Row> _rows = [];
    private PadRaw _pad;

    private string[] Functions(string channel)
    {
        var o = new List<string>();
        if (GenericStick.AxisNumber(channel) is > 0 and var n && IsAxisName(channel))
        {
            foreach (var row in AxisRows)
                if (_map.AxisOf(row) is { } t && GenericStick.AxisNumber(t.Channel) == n && t.Device.Equals(Dev, StringComparison.OrdinalIgnoreCase))
                    o.Add(AxisItems[Array.IndexOf(AxisRows, row)] + (BindingTable.IsInverted(row, t) ? " (inverted)" : ""));
            foreach (var th in Thresholds(channel))
                o.Add(FormattableString.Invariant($"{(th.Positive ? '+' : '-')}{th.Engage * 100:0}% {CommandName(th.Command)}"));
            return [.. o];
        }
        int id = VirtualStick.DiscreteId(channel);
        foreach (var entry in Entries)
            foreach (var b in _map.Of(Controls.Commands[entry]))
                if (BindingTable.IsPad(b) && b.Terms[0].Device.Equals(Dev, StringComparison.OrdinalIgnoreCase) && b.Terms[0].Plus && VirtualStick.DiscreteId(b.Terms[0].Channel) == id) { o.Add(NameOf(entry)); break; }
        return [.. o.Distinct()];
    }

    /// <summary>The threshold rules on an axis of the device shown (either way).</summary>
    private IEnumerable<PadRules.AxisThreshold> Thresholds(string channel) =>
        _map.Rules.Rules.OfType<PadRules.AxisThreshold>().Where(t => t.Axis.Device.Equals(Dev, StringComparison.OrdinalIgnoreCase) && GenericStick.AxisNumber(t.Axis.Channel) == GenericStick.AxisNumber(channel) && GenericStick.AxisNumber(channel) > 0);

    /// <summary>The device shown's input off every command, and an axis's threshold rules with it.</summary>
    private void Clear(string channel)
    {
        _map.ClearInputOn(Dev, channel);
        foreach (var t in Thresholds(channel).ToList()) _map.Rules.Remove(t);
    }

    private static bool IsAxisName(string channel) => VirtualStick.AnalogNames.Any(a => a.Equals(channel, StringComparison.OrdinalIgnoreCase));

    private static string Shown(string channel)
    {
        if (GenericStick.AxisNumber(channel) is > 0 and var n && IsAxisName(channel)) return $"Axis {n}";
        int id = VirtualStick.DiscreteId(channel);
        return id >= VirtualStick.ButtonCount ? "Hat " + new[] { "Up", "Right", "Down", "Left" }[id - VirtualStick.ButtonCount] : $"Button {id + 1}";
    }

    private IEnumerable<string> Wrap(string text, int room)
    {
        var line = "";
        foreach (var w in text.Split(' '))
        {
            var next = line.Length == 0 ? w : line + " " + w;
            if (line.Length > 0 && _f.Measure(next) > room) { yield return line; line = "  " + w; }
            else line = next;
        }
        if (line.Length > 0) yield return line;
    }

    private void Layout()
    {
        _rows.Clear();
        var vs = VirtualStick.From(_pad);
        // what is listed: the first few the stick always has, what has been used, and what is bound
        var axes = new SortedSet<int>([1, 2, .. _axes]);
        var buttons = new SortedSet<int>([0, 1, 2, 3, .. _buttons]);
        var hats = new HashSet<string>(_hats);
        foreach (var b in _map.Blocks.Where(b => BindingTable.IsPad(b) && b.Terms[0].Device.Equals(Dev, StringComparison.OrdinalIgnoreCase)))
        {
            if (b.Terms[0].Channel is not { } ch) continue;
            if (IsAxisName(ch)) { if (GenericStick.AxisNumber(ch) is > 0 and var n) axes.Add(n); }
            else if (VirtualStick.DiscreteId(ch) is >= 0 and var id) { if (id >= VirtualStick.ButtonCount) hats.Add(VirtualStick.HatNames[id - VirtualStick.ButtonCount]); else buttons.Add(id); }
        }
        foreach (var th in _map.Rules.Rules.OfType<PadRules.AxisThreshold>())
            if (th.Axis.Device.Equals(Dev, StringComparison.OrdinalIgnoreCase) && GenericStick.AxisNumber(th.Axis.Channel) is > 0 and var tn) axes.Add(tn);
        const int LeftRoom = ControllerScreen.LeftRule - 29 - 3, RightRoom = 620 - ControllerScreen.Inset - 2 - 476 - 3;
        void Add(string channel, bool left, bool live)
        {
            var fns = Functions(channel);
            var text = fns.Length == 0 ? Shown(channel) : Shown(channel) + ": " + string.Join(", ", fns);
            _rows.Add(new Row(channel, left, [.. Wrap(text, left ? LeftRoom : RightRoom)], live));
        }
        foreach (int n in axes) Add(GenericStick.AxisName(n), true, n <= GenericStick.MaxAxes && Math.Abs(vs.Axes[VirtualStick.RawFirst + n - 1]) > 0.3f);
        foreach (var h in VirtualStick.HatNames.Where(hats.Contains)) Add(h, true, (vs.Hat >> Array.IndexOf(VirtualStick.HatNames, h) & 1) != 0);
        foreach (int b in buttons) Add($"Button{b + 1}", false, (vs.Buttons >> b & 1) != 0);
        foreach (bool left in new[] { true, false })
        {
            int slot = 0;
            foreach (var r in _rows.Where(r => r.Left == left).ToList())
            {
                if (slot + r.Lines.Length > ControllerScreen.Rows) { _rows.Remove(r); continue; }
                r.Slot = slot;
                slot += r.Lines.Length;
            }
        }
    }

    private string Live() => string.Join(",", _rows.Where(r => r.Live).Select(r => r.Channel)) + "|" + _sel + "|" + (int)(GodotPads.DeadZone * 100) + GodotPads.HatOnButtons + GodotPads.HatsKnown() + Dev + Controls.GenericDevices.Count;

    protected override void DrawExtra()
    {
        _pad = ReadPad();
        Layout();
        var title = Shell.Db.Font(0x43);
        var ink = Solid(title, Frame.Nearest(ControllerScreen.TitleInk));
        int x = 320 - title.Measure(Title) / 2;
        Frame.Text(title, x, 12, Title, ink);
        Frame.Text(title, x + 1, 12, Title, ink);
        var small = Shell.Db.Font(0x47);
        var dark = Solid(small, Frame.Nearest(ControllerScreen.TitleInk));
        for (int i = 1; i < ControllerScreen.Slots.Length; i++)
        {
            var s = ControllerScreen.Slots[i];
            string text = ControllerScreen.Fit(SlotText[i], t => small.Measure(t) + 1, s.Size.X - 8);
            int tx = s.Position.X + (s.Size.X - small.Measure(text)) / 2, ty = s.Position.Y + (s.Size.Y - small.Height) / 2 + 1;
            Frame.Text(small, tx, ty, text, dark);
            Frame.Text(small, tx + 1, ty, text, dark);
        }
        int mid = (ControllerScreen.LeftRule + ControllerScreen.RightRule) / 2;
        void Center(string s, int row) => Frame.Text(_f, mid - _f.Measure(s) / 2, ControllerScreen.RowY(row) + 3, s, ShellFrame.OverlayInk);
        string[] notice = Controls.HasPad ? ["Move an axis or press a button", "to find it, then click it to choose", "what it does"] : ["No joystick connected"];
        // the device and, for a known one, its preset (what Restore Defaults loads)
        if (Controls.HasPad)
        {
            // the device shown (row 13 is the device switch when there are two or more: the name goes above it)
            var name = Controls.DeviceName(DeviceIndex);
            if (name.Length > 0) Center(name.Length > 40 ? name[..40] : name, Controls.GenericDevices.Count > 1 ? 12 : 13);
            Center(Controls.PresetOf(DeviceIndex) is { } preset ? $"Preset: {preset.Name}" : DeviceIndex == 0 ? "Preset: generic stick" : "Preset: none", 14);
        }
        for (int k = 0; k < notice.Length; k++) Center(notice[k], k);
        if (Controls.HasPad)
        {
            Center($"Dead zone: {(int)MathF.Round(GodotPads.DeadZone * 100)}%  (click to change)", 3);
            Center(GodotPads.HatsKnown() ? "The hat and buttons 12+ are told apart" : GodotPads.HatOnButtons ? "Buttons 12-15 are Button 12-15 (click)" : "Buttons 12-15 are the hat (click)", 4);
            // the dead zone and the hat switch (items) are rows 3 and 4; below them, what the rest is for
            string[] help = ["Axis 1 and 2 are the stick: forward", "accelerates, sideways steers. A throttle", "lever or a twist is bound the same way;", "Invert turns an axis the other way.", "", "Right-click or Delete clears the", "selected input. Esc cancels."];
            for (int k = 0; k < help.Length; k++) Center(help[k], 5 + k);
            var g = Controls.GenericDevices;
            if (g.Count > 1) Center($"Device {Math.Min(_dev, g.Count - 1) + 1} of {g.Count}: {Dev}  (click or Tab: next)", 13);
        }
        byte marker = Frame.Nearest(ControllerScreen.Marker), pencil = Frame.Nearest(ControllerScreen.RedPencil);
        foreach (var r in _rows)
        {
            var rect = r.Rect;
            int bottom = rect.Position.Y + rect.Size.Y;
            if (r.Live) Frame.Fill(rect.Position.X + 3, bottom - 4, rect.Size.X - 6, 2, marker);
            if (r.Channel == _sel) Frame.Fill(rect.Position.X + 2, bottom - 2, rect.Size.X - 6, 1, pencil);
            for (int k = 0; k < r.Lines.Length; k++)
                Frame.Text(_f, r.Left ? 29 : 476, ControllerScreen.RowY(r.Slot + k) + 3, r.Lines[k], ShellFrame.OverlayInk);
        }
        _lastLive = Live();
    }

    private static byte[] Solid(ShellFont f, byte ink)
    {
        var r = new byte[256];
        Array.Fill(r, ink);
        r[f.Key & 0xff] = 0xff;
        return r;
    }

    // ---- editing ----------------------------------------------------------------------------------------

    internal PadRaw ReadPad() => Controls.Focused() && Controls.PadSource.Read() is { } l && DeviceIndex < l.Count ? l[DeviceIndex] : default;

    private Row? RowAt(Vector2I m) => _rows.FirstOrDefault(r => r.Rect.HasPoint(m));

    private void Open(string channel)
    {
        Shell.Sound(82);
        _sel = channel;
        bool axis = IsAxisName(channel);
        var items = new List<string> { "(none)" };
        if (axis) items.AddRange([.. AxisItems, .. ThresholdItems]);
        else foreach (var n in Entries) items.Add(NameOf(n));
        var s = Shell.Db.Bank(0x24)[16];
        int w = s?.Width ?? 172, h = s?.Height ?? 235;
        Shell.Modal(new ListPopup
        {
            Sprite = 16, X = 320 - w / 2, Y = 240 - h / 2, TextX = 0x16, TextY = 0x30, Visible = 9, Items = items, Font = _f,
            Up = (18, 36, 12, 25), Down = (18, 36, 204, 217), List = (16, 214, 47, 198), Cancel = (88, 159, 209, 225),
            PadPicks = false,
            Picked = i =>
            {
                if (axis && i > AxisItems.Length) { ThresholdList(channel, i == AxisItems.Length + 1); return; }
                if (i == 0) Clear(channel);
                else if (axis && i == AxisItems.Length) _map.InvertAxis(GenericStick.AxisNumber(channel), Dev);
                else if (axis && i > 0) _map.AssignAxis(AxisRows[i - 1], Dev, channel);
                else if (i > 0) _map.Assign(Controls.Commands[Entries[i - 1]], Dev, channel);
                Redraw();
            },
        });
    }

    /// <summary>
    /// A command for the axis pushed one way past half its travel (letting go under a third): held while past for a command that is held (the
    /// e-brake, fire, a hardpoint...), else pressed once a push. The axis keeps what else it does.
    /// </summary>
    private void ThresholdList(string channel, bool positive)
    {
        var s = Shell.Db.Bank(0x24)[16];
        int w = s?.Width ?? 172, h = s?.Height ?? 235;
        Shell.Modal(new ListPopup
        {
            Sprite = 16, X = 320 - w / 2, Y = 240 - h / 2, TextX = 0x16, TextY = 0x30, Visible = 9, Items = [.. Entries.Select(n => (positive ? "+ " : "- ") + NameOf(n))], Font = _f,
            Up = (18, 36, 12, 25), Down = (18, 36, 204, 217), List = (16, 214, 47, 198), Cancel = (88, 159, 209, 225),
            PadPicks = false,
            Picked = i =>
            {
                if (i >= 0)
                {
                    var cmd = Controls.Commands[Entries[i]];
                    var mode = HeldCommands.Contains(cmd) ? PadRules.AxisThreshold.Mode.Level : PadRules.AxisThreshold.Mode.Edge;
                    _map.Rules.Add(new PadRules.AxisThreshold(new InputTerm(true, Dev, channel), positive, 0.5f, 0.33f, mode, cmd));
                }
                Redraw();
            },
        });
    }

    /// <summary>Move an axis a good way or press a button: its row is in the list and selected.</summary>
    private void Watch()
    {
        if (_pad.Axes is { } ax)
            for (int i = 0; i < GenericStick.MaxAxes && i < ax.Length; i++)
            {
                bool held = Math.Abs(ax[i]) > 0.6f;
                if (held && !_axisHeld[i]) { _axes.Add(i + 1); _sel = GenericStick.AxisName(i + 1); }
                _axisHeld[i] = held;
            }
        var vs = VirtualStick.From(_pad);
        uint fresh = vs.Buttons & ~_lastButtons;
        for (int b = 0; b < VirtualStick.ButtonCount; b++)
            if ((fresh >> b & 1) != 0) { _buttons.Add(b); _sel = $"Button{b + 1}"; }
        int newHat = vs.Hat & ~_lastHat;
        for (int h = 0; h < 4; h++)
            if ((newHat >> h & 1) != 0) { _hats.Add(VirtualStick.HatNames[h]); _sel = VirtualStick.HatNames[h]; }
        _lastButtons = vs.Buttons;
        _lastHat = vs.Hat;
    }

    protected override void ClickElsewhere(Vector2I m)
    {
        if (RowAt(m) is { } r) { _sel = r.Channel; Open(r.Channel); }
    }

    private void Step(int d)
    {
        if (_rows.Count == 0) return;
        int i = _rows.FindIndex(r => r.Channel == _sel);
        _sel = _rows[((i < 0 ? (d > 0 ? -1 : 0) : i) + d + _rows.Count) % _rows.Count].Channel;
        Shell.Sound(82);
        Redraw();
    }

    public override void Tick()
    {
        if (_loading)
        {
            _loading = false;
            Frame.LoadBackground(PaperSheet.Make(Shell.Db.Background(Record), true), 0, 0, black: false);
            Redraw();
            return;
        }
        _pad = ReadPad();
        Watch();
        foreach (var k in Shell.Keys)
        {
            if (k is Key.Up or Key.Left) Step(-1);
            else if (k is Key.Down or Key.Right) Step(1);
            else if (k is Key.Enter or Key.Space or Key.KpEnter) { if (_sel is not null) { Open(_sel); return; } }
            else if (k is Key.Delete or Key.Backspace) { if (_sel is not null) { Clear(_sel); Redraw(); } }
            else if (k is Key.Tab) NextDevice();
        }
        if (Shell.RightClicked && RowAt(Shell.Mouse) is { } r) { _sel = r.Channel; Clear(r.Channel); Shell.Sound(82); Redraw(); return; }
        // the marks follow the stick: redrawn when a row lights or goes out
        Layout();
        if (Live() != _lastLive) Redraw();
        base.Tick();
    }
}
