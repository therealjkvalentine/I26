using System.Net;
using System.Text;

namespace I76.Formats.Input;

/// <summary>
/// The bindings of an input.map as a printable page (<c>i76 keysheet</c>): a table per group of commands, named as the Control
/// Configuration names them, each with every way it is bound (keys and mouse buttons, then the pads' inputs); the commands
/// the screen does not show follow under their file names. Every bound command is listed once; unbound ones are left out.
/// Text only, no art.
/// </summary>
public static class KeySheet
{
    /// <summary>The groups and their commands, with the Control Configuration's names.</summary>
    public static readonly (string Group, (string Command, string Label)[] Rows)[] Groups =
    [
        ("Driving", [
            ("throttle_up", "Accelerate"), ("throttle_down", "Brake"), ("steer_left", "Steer Left"), ("steer_right", "Steer Right"),
            ("throttle", "Throttle (axis)"), ("steer", "Steer (axis)"), ("e_brake", "Emergency Brake"), ("reverse_direction", "Toggle Reverse"),
            ("shift_down", "Trans. Shift Left"), ("shift_up", "Trans. Shift Right"), ("start_engine", "Ignition"), ("HONK_HORN", "Horn"),
            ("toggle_lights", "Headlights"),
        ]),
        ("Weapons", [
            ("weapon_fire", "Fire"), ("weapon_cycle", "Cycle Weapon"), ("weapon_link", "Link Like Weapon"),
            ("hardpoint1_fire", "Fire Hardpoint 1"), ("hardpoint2_fire", "Fire Hardpoint 2"), ("hardpoint3_fire", "Fire Hardpoint 3"),
            ("hardpoint4_fire", "Fire Hardpoint 4"), ("hardpoint5_fire", "Fire Hardpoint 5"),
            ("special1", "Use Special 1"), ("special2", "Use Special 2"), ("special3", "Use Special 3"),
            (PistolKeys.Left, "Pistol Left"), (PistolKeys.Right, "Pistol Right"),
        ]),
        ("Targets and radar", [
            ("frontal_target", "Target Front Enemy"), ("NEXT_TARGET", "Target Next Enemy"), ("TARGET_NEAREST_ENEMY", "Target Nearest Enemy"),
            ("RESET_TARGET", "Cancel Target"), ("RADAR_RANGE_TOGGLE", "Radar Zoom"), ("RADAR_CAMERA_TOGGLE", "Toggle Weapon Cam"),
        ]),
        ("Views", [
            ("pilot_glance_down", "Look at Dash"), ("pilot_glance_left", "Look Left"), ("pilot_glance_right", "Look Right"),
            ("pilot_glance_up", "Look Rear"), ("pilot_glance_target", "Look at Target"), ("toggle_cmbt_view", "Quick View Toggle"),
            ("SHOW_MAP", "Map"), ("SHOW_NOTEPAD", "Notepad"), ("TOGGLE_BINOCULARS", "Binoculars"), ("POETRY", "Poem"),
        ]),
    ];

    public const string OtherGroup = "Other commands";

    /// <summary>One block as text: its "+" terms joined (a chord), the device named unless it is the keyboard.</summary>
    public static string Describe(InputBlock b)
    {
        var plus = b.Terms.Where(t => t.Plus).Select(t => t.Device.Equals("keyboard", StringComparison.OrdinalIgnoreCase) ? t.Channel : $"{t.Device} {t.Channel}");
        return string.Join(" + ", plus);
    }

    /// <summary>A command's bindings: the keyboard and mouse blocks first, then the pads', each once.</summary>
    public static IReadOnlyList<string> Bindings(BindingTable t, string command) =>
        [.. t.Of(command).OrderBy(b => BindingTable.IsPad(b) ? 1 : 0).Select(Describe).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The page's groups: each with the rows of its bound commands, then every other bound command in file order.</summary>
    public static List<(string Group, List<(string Command, string Label, IReadOnlyList<string> Keys)> Rows)> Layout(BindingTable t)
    {
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = new List<(string, List<(string, string, IReadOnlyList<string>)>)>();
        foreach (var (group, rows) in Groups)
        {
            var bound = new List<(string, string, IReadOnlyList<string>)>();
            foreach (var (cmd, label) in rows)
                if (Bindings(t, cmd) is { Count: > 0 } keys && listed.Add(cmd)) bound.Add((cmd, label, keys));
            if (bound.Count > 0) page.Add((group, bound));
        }
        var other = new List<(string, string, IReadOnlyList<string>)>();
        foreach (var cmd in t.Blocks.Select(b => b.Command).Distinct(StringComparer.OrdinalIgnoreCase))
            if (Bindings(t, cmd) is { Count: > 0 } keys && listed.Add(cmd)) other.Add((cmd, cmd, keys));
        if (other.Count > 0) page.Add((OtherGroup, other));
        return page;
    }

    private static string H(string s) => WebUtility.HtmlEncode(s);

    /// <summary>The page: one HTML file, its style inline, laid out to print on one sheet.</summary>
    public static string Html(BindingTable t, string source)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><title>I26 controls</title>\n<style>\n")
          .Append("body{font:10pt/1.3 system-ui,sans-serif;margin:12mm;color:#111;background:#fff}h1{font-size:14pt;margin:0 0 2mm}")
          .Append("p.src{margin:0 0 4mm;color:#555;font-size:8pt}.groups{columns:2;column-gap:8mm}section{break-inside:avoid;margin:0 0 4mm}")
          .Append("h2{font-size:11pt;margin:0 0 1mm;border-bottom:1px solid #999}table{border-collapse:collapse;width:100%}")
          .Append("td{padding:1px 4px;vertical-align:top;border-bottom:1px solid #ddd}td.k{font-family:ui-monospace,monospace;text-align:right}")
          .Append("@page{size:auto;margin:10mm}@media print{body{margin:0}}\n</style></head><body>\n")
          .Append("<h1>I26 controls</h1>\n<p class=\"src\">").Append(H(source)).Append("</p>\n<div class=\"groups\">\n");
        foreach (var (group, rows) in Layout(t))
        {
            sb.Append("<section><h2>").Append(H(group)).Append("</h2><table>\n");
            foreach (var (_, label, keys) in rows)
                sb.Append("<tr><td>").Append(H(label)).Append("</td><td class=\"k\">").Append(string.Join(" / ", keys.Select(H))).Append("</td></tr>\n");
            sb.Append("</table></section>\n");
        }
        sb.Append("</div>\n</body></html>\n");
        return sb.ToString();
    }
}
