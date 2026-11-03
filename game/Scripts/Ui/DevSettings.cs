using Godot;
using I76.Game.Shell;

namespace I76.Game.Ui;

/// <summary>When a change of a setting shows: at once, from the next menu screen drawn, from the next mission, or from the next start of the game.</summary>
public enum DevWhen { Live, NextScreen, NextMission, NextStart }

/// <summary>
/// One of the settings without a row (docs/playing.md): its [port] key, its values (or a numeric range), how it reads and
/// writes ShellSettings, and when a change shows. <see cref="Apply"/> is what a live change needs beyond the setting itself.
/// </summary>
public sealed record DevSetting(string Key, string[]? Values, Func<ShellSettings, int> Get, Action<ShellSettings, int> Set, DevWhen When,
    Action<ShellSettings>? Apply = null, int Min = 0, int Max = 0, int Step = 1);

/// <summary>
/// The Developer panels' Settings section (in a mission and on the main menu): every key a player can set by hand, as a
/// toggle or a stepper. A change is saved to shell.cfg at once and applied where the setting allows it.
/// </summary>
public static class DevSettings
{
    private static readonly string[] OffOn = ["false", "true*"];

    public static readonly DevSetting[] All =
    [
        new("hd_textures", OffOn, s => s.HdTextures ? 1 : 0, (s, v) => s.HdTextures = v != 0, DevWhen.Live,
            s => I76.Game.Assets.HdTextures.Enabled = s.HdTextures),
        new("hd_menu_art", OffOn, s => s.HdMenuArt ? 1 : 0, (s, v) => s.HdMenuArt = v != 0, DevWhen.NextScreen,
            s => HdAssets.Enabled = s.HdMenuArt),
    ];

    public static string WhenText(DevWhen w) => w switch { DevWhen.Live => "live", DevWhen.NextScreen => "next screen", DevWhen.NextMission => "next mission", _ => "next start" };

    /// <summary>
    /// The section's rows into <paramref name="col"/>: a change sets <paramref name="settings"/>, saves it, runs the entry's
    /// Apply and then <paramref name="changed"/> (the host's own refresh, such as the mission's textures).
    /// </summary>
    public static void Build(VBoxContainer col, ShellSettings settings, Action<ShellSettings, DevSetting>? changed)
    {
        foreach (var e in All)
        {
            var name = DevScale.Min(new Label { Text = e.Key }, 150);
            var when = new Label { Text = WhenText(e.When), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            when.AddThemeColorOverride("font_color", new Color(0.6f, 0.65f, 0.7f));
            Control pick;
            void Changed(int v)
            {
                e.Set(settings, v);
                settings.Save();
                e.Apply?.Invoke(settings);
                changed?.Invoke(settings, e);
            }
            if (e.Values is { } values)
            {
                var o = DevScale.Min(new OptionButton { FitToLongestItem = false }, 120);
                foreach (var v in values) o.AddItem(v);
                o.Selected = Math.Clamp(e.Get(settings), 0, values.Length - 1);
                o.ItemSelected += i => Changed((int)i);
                pick = o;
            }
            else
            {
                var sb = DevScale.Min(new SpinBox { MinValue = e.Min, MaxValue = e.Max, Step = e.Step, Value = e.Get(settings) }, 120);
                sb.ValueChanged += v => Changed((int)v);
                pick = sb;
            }
            var r = DevScale.Sep(new HBoxContainer(), 6);
            r.AddChild(name);
            r.AddChild(pick);
            r.AddChild(when);
            col.AddChild(r);
        }
    }
}
