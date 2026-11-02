namespace I76.Formats.Input;

/// <summary>
/// Presets for known wheels and sticks (docs/joystick.md), chosen by the device's USB ids or, failing those, its name, the first time it is seen
/// with no binding of its own: then they are bindings like any other. A preset is the device's input.map blocks (on the joystickN it is) and its
/// rules (<see cref="PadRules"/>). The layouts follow the ones players settled on for these devices on the original, kept where they map onto
/// I26's commands.
/// </summary>
public static class DevicePresets
{
    /// <summary>What the platform says a device is: its name and USB vendor and product ids (0 when unknown).</summary>
    public readonly record struct Device(string Name, uint Vendor, uint Product);

    public sealed class Preset
    {
        public required string Name { get; init; }
        /// <summary>USB ids (vendor, product) that are this device.</summary>
        public required (uint Vendor, uint Product)[] Ids { get; init; }
        /// <summary>Words in the device's name that are this device, when the ids are not known.</summary>
        public required string[] NameHas { get; init; }
        /// <summary>The blocks and rules on <paramref name="device"/> ("joystick1"); <paramref name="alone"/>: no other device steers.</summary>
        public required Func<string, bool, (InputBlock[] Blocks, PadRules.Rule[] Rules)> Build { get; init; }

        public bool Matches(Device d) =>
            d.Vendor != 0 && Ids.Contains((d.Vendor, d.Product)) || NameHas.Any(w => d.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static InputBlock B(string command, string device, params (bool Plus, string Channel)[] terms) =>
        new(command, [.. terms.Select(t => new InputTerm(t.Plus, device, t.Channel))]);

    /// <summary>
    /// Thrustmaster T300RS with its pedals set to Combined (one axis: the accelerator one way, the brake the other; Separate cannot work, as
    /// each pedal rests at an end of its travel). The wheel steers, the pedals are the throttle; button 6 is a shift (the second commands as
    /// input.map chords, the shift "+" and the first command's block with the shift "-"). Base / with 6 held: 1 lights / fire, 2 fire / front
    /// target, 3 cycle weapon / combat view, 4 e-brake / reverse, 5 special 1 / special 2, 7 hardpoint 3 / hardpoint 4, 8 nearest target /
    /// clear target, 9 horn / look at target, 10 horn / radar range, 11 lights / radar camera, 12 ignition / binoculars, 13 horn / poetry; the
    /// D-pad lights, map, ignition, notepad (up, right, down, left), with 6 held gear up and down. The ids: the driver's (B66E) and the
    /// wheel's other modes, and B65D, how it shows before Thrustmaster's driver is installed.
    /// </summary>
    public static readonly Preset T300RS = new()
    {
        Name = "Thrustmaster T300RS",
        Ids = [(0x044F, 0xB66E), (0x044F, 0xB66F), (0x044F, 0xB66D), (0x044F, 0xB65D)],
        NameHas = ["T300"],
        Build = (d, _) =>
        {
            const string Shift = "Button6";
            var o = new List<InputBlock> { B("steer", d, (false, "Left/Right")), B("throttle", d, (false, "Down/Up")) };
            (string Ch, string Base, string? Shifted)[] map =
            [
                ("Button1", "toggle_lights", "weapon_fire"), ("Button2", "weapon_fire", "frontal_target"), ("Button3", "weapon_cycle", "toggle_cmbt_view"),
                ("Button4", "e_brake", "reverse_direction"), ("Button5", "special1", "special2"), ("Button7", "hardpoint3_fire", "hardpoint4_fire"),
                ("Button8", "TARGET_NEAREST_ENEMY", "RESET_TARGET"), ("Button9", "HONK_HORN", "pilot_glance_target"), ("Button10", "HONK_HORN", "RADAR_RANGE_TOGGLE"),
                ("Button11", "toggle_lights", "RADAR_CAMERA_TOGGLE"), ("Button12", "start_engine", "TOGGLE_BINOCULARS"), ("Button13", "HONK_HORN", "POETRY"),
                ("HatUp", "toggle_lights", "shift_up"), ("HatRight", "SHOW_MAP", null), ("HatDown", "start_engine", "shift_down"), ("HatLeft", "SHOW_NOTEPAD", null),
            ];
            foreach (var (ch, first, second) in map)
            {
                o.Add(second is null ? B(first, d, (true, ch)) : B(first, d, (true, ch), (false, Shift)));
                if (second is not null) o.Add(B(second, d, (true, ch), (true, Shift)));
            }
            return ([.. o], []);
        },
    };

    /// <summary>
    /// CH Products Fighterstick. Beside a wheel (not joystick1, or not alone) the stick is the gearbox: pulled back the e-brake (held), pushed
    /// forward reverse while pushed (a toggle pressed going in and coming out), left and right a gear down and up (once a movement), engaging at
    /// 45 % fore and aft and 55 % sideways and letting go at 30 % and 40 % (the wheel is turned with the other hand, and the stick knocked sideways
    /// more easily than pulled). Alone it steers sideways and accelerates or brakes on its base's throttle wheel (axis 3), and keeps the e-brake
    /// and reverse fore and aft. Buttons: the trigger fires, the pickle (2) hardpoint 2, 3 special 1, the pinky (4) looks at the target; the
    /// hats are the hardpoints (5 to 8: 2, 3, 4, 5), targeting (9 to 12: front, next, nearest, radar range) and 13 to 16 special 1, notepad,
    /// combat view, map; the cone hat glances, held, its up and down the other way round as in a flight stick.
    /// </summary>
    public static readonly Preset Fighterstick = new()
    {
        Name = "CH Fighterstick",
        Ids = [(0x068E, 0x00F3)],
        NameHas = ["Fighterstick"],
        Build = (d, alone) =>
        {
            var o = new List<InputBlock>();
            if (alone) { o.Add(B("steer", d, (false, "Left/Right"))); o.Add(B("throttle", d, (false, "Axis3"))); }
            string[] buttons =
            [
                "weapon_fire", "hardpoint2_fire", "special1", "pilot_glance_target",
                "hardpoint2_fire", "hardpoint3_fire", "hardpoint4_fire", "hardpoint5_fire",
                "frontal_target", "NEXT_TARGET", "TARGET_NEAREST_ENEMY", "RADAR_RANGE_TOGGLE",
                "special1", "SHOW_NOTEPAD", "toggle_cmbt_view", "SHOW_MAP",
            ];
            for (int i = 0; i < buttons.Length; i++) o.Add(B(buttons[i], d, (true, $"Button{i + 1}")));
            foreach (var (hat, glance) in new[] { ("HatUp", "pilot_glance_down"), ("HatRight", "pilot_glance_right"), ("HatDown", "pilot_glance_up"), ("HatLeft", "pilot_glance_left") })
                o.Add(B(glance, d, (true, hat)));
            var y = new InputTerm(true, d, "Down/Up");
            var x = new InputTerm(true, d, "Left/Right");
            var rules = new List<PadRules.Rule>
            {
                new PadRules.AxisThreshold(y, true, 0.45f, 0.30f, PadRules.AxisThreshold.Mode.Level, "e_brake"),
                new PadRules.AxisThreshold(y, false, 0.45f, 0.30f, PadRules.AxisThreshold.Mode.Momentary, "reverse_direction"),
            };
            if (!alone)
            {
                rules.Add(new PadRules.AxisThreshold(x, false, 0.55f, 0.40f, PadRules.AxisThreshold.Mode.Edge, "shift_down"));
                rules.Add(new PadRules.AxisThreshold(x, true, 0.55f, 0.40f, PadRules.AxisThreshold.Mode.Edge, "shift_up"));
            }
            return ([.. o], [.. rules]);
        },
    };

    public static readonly Preset[] All = [T300RS, Fighterstick];

    /// <summary>The preset for a device, or null.</summary>
    public static Preset? Match(Device d) => All.FirstOrDefault(p => p.Matches(d));

    /// <summary>A device's preset put on a copy of the bindings: every block and rule on that joystickN replaced by the preset's, the rest kept.</summary>
    public static BindingTable Apply(BindingTable table, Preset p, string device, bool alone)
    {
        var (blocks, rules) = p.Build(device, alone);
        var keep = table.Blocks.Where(b => !(BindingTable.IsPad(b) && b.Terms[0].Device.Equals(device, StringComparison.OrdinalIgnoreCase)));
        var t = BindingTable.FromBlocks([.. keep, .. blocks]);
        t.Rules = table.Rules.Clone();
        foreach (var r in t.Rules.Rules.Where(r => r.Inputs.Any(i => i.Device.Equals(device, StringComparison.OrdinalIgnoreCase))).ToList()) t.Rules.Remove(r);
        foreach (var r in rules) t.Rules.Add(r);
        return t;
    }

    /// <summary>Whether the bindings have nothing on a device (no block, no rule): a preset may be put there.</summary>
    public static bool Unbound(BindingTable table, string device) =>
        !table.Blocks.Any(b => BindingTable.IsPad(b) && b.Terms.Any(t => t.Device.Equals(device, StringComparison.OrdinalIgnoreCase)))
        && !table.Rules.Rules.Any(r => r.Inputs.Any(i => i.Device.Equals(device, StringComparison.OrdinalIgnoreCase)));
}
