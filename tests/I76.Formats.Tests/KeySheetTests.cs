using System.Text.RegularExpressions;
using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary><c>i76 keysheet</c>'s page (KeySheet).</summary>
public class KeySheetTests
{
    // a map written for the test: two blocks for one command, a chord, a pad, a command the screen does not name, an empty block
    private const string Map = "# Interstate '76 Input mapping file\n"
        + "throttle_up {\n   + keyboard   Numpad8\n\t- Keyboard\tShift\n}\nthrottle_up {\n   + keyboard W\n}\n"
        + "weapon_fire {\n   + keyboard Space\n}\nweapon_fire {\n   + Mouse LeftBtn\n}\nweapon_fire {\n   + joystick1 Button1\n}\n"
        + "SELF_DESTRUCT {\n   + keyboard Control\n   + keyboard Delete\n}\n"
        + "track_yaw_minus {\n   + keyboard GreyLeftArrow\n}\n"
        + "pilot_glance_left {\n   + keyboard GreyLeftArrow\n}\n"
        + "steer {\n   + joystick1 X\n}\n"
        + "HONK_HORN {\n}\n";

    [Fact]
    public void TheSheetListsEveryBoundCommandOnce()
    {
        var t = BindingTable.Parse(Map);
        var layout = KeySheet.Layout(t);
        var commands = layout.SelectMany(g => g.Rows.Select(r => r.Command)).ToList();
        var bound = t.Blocks.Where(b => b.Terms.Any(x => x.Plus)).Select(b => b.Command).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(bound.Order(StringComparer.OrdinalIgnoreCase), commands.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(commands.Count, commands.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("HONK_HORN", commands);   // a block without a key binds nothing

        var html = KeySheet.Html(t, "input.map");
        Assert.StartsWith("<!doctype html>", html);
        // each command one row: the screen's names, or the file's name in the last group
        foreach (var label in new[] { "Accelerate", "Fire", "Look Left", "Steer (axis)", "SELF_DESTRUCT", "track_yaw_minus" })
            Assert.Single(Regex.Matches(html, $"<tr><td>{Regex.Escape(label)}</td>"));
        Assert.Contains("<td class=\"k\">Numpad8 / W</td>", html);
        Assert.Contains("<td class=\"k\">Space / Mouse LeftBtn / joystick1 Button1</td>", html);
        Assert.Contains("Control + Delete", html);
        Assert.Equal(["Driving", "Weapons", "Views", KeySheet.OtherGroup], layout.Select(g => g.Group));
    }

    [Fact]
    public void EveryCommandOfTheScreenHasItsGroup()
    {
        string[] screen =
        [
            "throttle_up", "throttle_down", "steer_left", "steer_right", "shift_down", "shift_up", "frontal_target", "NEXT_TARGET",
            "TARGET_NEAREST_ENEMY", "RESET_TARGET", "RADAR_RANGE_TOGGLE", "RADAR_CAMERA_TOGGLE", "start_engine", "SHOW_MAP", "SHOW_NOTEPAD",
            "TOGGLE_BINOCULARS", "HONK_HORN", "e_brake", "toggle_lights", "pilot_glance_down", "pilot_glance_left", "pilot_glance_right",
            "pilot_glance_up", "pilot_glance_target", "weapon_fire", "weapon_cycle", "weapon_link", "hardpoint1_fire", "hardpoint2_fire",
            "hardpoint3_fire", "hardpoint4_fire", "hardpoint5_fire", "special1", "special2", "special3", "POETRY", "toggle_cmbt_view", "reverse_direction",
        ];
        var grouped = KeySheet.Groups.SelectMany(g => g.Rows.Select(r => r.Command)).ToList();
        Assert.All(screen, c => Assert.Contains(c, grouped));
        Assert.Equal(grouped.Count, grouped.Distinct().Count());
    }

    [Fact]
    public void TextIsEscaped()
    {
        var html = KeySheet.Html(BindingTable.Parse("odd<cmd> {\n   + keyboard A&B\n}\n"), "<file>");
        Assert.Contains("odd&lt;cmd&gt;", html);
        Assert.Contains("A&amp;B", html);
        Assert.Contains("&lt;file&gt;", html);
    }
}
