using Godot;
using I76.Game.Shell;

namespace I76.Game.Ui;

/// <summary>When a change of a setting shows: at once, from the next menu screen drawn, from the next mission, or from the next start of the game.</summary>
public enum DevWhen { Live, NextScreen, NextMission, NextStart }

/// <summary>
/// One of the settings without a row (docs/playing.md): its [port] key, its values as the file writes them (the port's marked
/// with <see cref="ShellSettings.PortMark"/>; "look" for a key left to follow the look), how it reads and writes ShellSettings,
/// and when a change shows. <see cref="Apply"/> is what a change needs beyond the setting and the host's refresh.
/// </summary>
public sealed record DevSetting(string Key, string[] Values, Func<ShellSettings, int> Get, Action<ShellSettings, int> Set, DevWhen When,
    Action<ShellSettings>? Apply = null, bool Slider = false, Func<int, bool>? Offered = null);

/// <summary>
/// The Developer panels' Settings section (in a mission and on the main menu): every key of docs/playing.md's "Settings
/// without a row" but a curve set by hand (HandSetOnly), each a button that steps its value (a right click steps back). A change is saved to shell.cfg at once
/// and applied where the setting allows it; the key's colour says when it shows.
/// </summary>
public static class DevSettings
{
    private const string M = ShellSettings.PortMark;
    private static readonly string[] FalseTrue = ["false", "true" + M];
    /// <summary>A key that follows the look while the file has none: "look", then the table's words.</summary>
    private static string[] Look(string[] names) => ["look", .. Words(names)];
    private static string[] Words(string[] names) => [.. names.Select((n, i) => OriginalRecipe.Word(n) + (i > 0 ? M : ""))];
    private static int OfChoice(int? c) => c is { } v ? v + 1 : 0;
    private static int? ToChoice(int i) => i == 0 ? null : i - 1;

    /// <summary>The effects' percentages offered, in the key's 25..150 range; a value set by hand shows as the nearest.</summary>
    public static readonly int[] EffectSteps = [25, 50, 75, 100, 125, 150];
    private static readonly string[] EffectNames = [.. EffectSteps.Select(p => p == I76.Sim.Views.EffectsLook.Default ? $"{p}" : $"{p}{M}")];
    public static int NearestStep(int percent) => Array.IndexOf(EffectSteps, EffectSteps.MinBy(p => Math.Abs(p - percent)));

    /// <summary>vegetation_density's percentages offered, in the key's 10..150 range; a value set by hand shows as the nearest.</summary>
    public static readonly int[] VegetationSteps = [10, 30, 60, 100, 150];
    private static readonly string[] VegetationNames = [.. VegetationSteps.Select(p => p == I76.Formats.Vegetation.VegetationDensity.Default ? $"{p}" : $"{p}{M}")];
    public static int NearestVegetationStep(int percent) => Array.IndexOf(VegetationSteps, VegetationSteps.MinBy(p => Math.Abs(p - percent)));

    /// <summary>crossover's frequencies offered (Hz), in the key's 40..160 range; a value set by hand shows as the nearest.</summary>
    public static readonly int[] CrossoverSteps = [40, 60, 80, 90, 120, 160];
    private static readonly string[] CrossoverNames = [.. CrossoverSteps.Select(p => p == (int)I76.Formats.Input.AudioOutputs.DefaultCrossover ? $"{p}" : $"{p}{M}")];
    public static int NearestCrossoverStep(float hz) => Array.IndexOf(CrossoverSteps, CrossoverSteps.MinBy(p => Math.Abs(p - hz)));

    /// <summary>The shaker sources' percentages (lfe_engine ...), 0 to 200 in tens on a slider; a value set by hand shows as the nearest.</summary>
    public static readonly int[] SourceSteps = [.. Enumerable.Range(0, 21).Select(i => i * 10)];
    private static readonly string[] SourceNames = [.. SourceSteps.Select(p => p == I76.Formats.Input.TactileSources.Default ? $"{p}" : $"{p}{M}")];
    public static int NearestSourceStep(int percent) => Array.IndexOf(SourceSteps, SourceSteps.MinBy(p => Math.Abs(p - percent)));
    private static DevSetting Source(I76.Formats.Input.TactileSource src) => new(I76.Formats.Input.TactileSources.Keys[(int)src], SourceNames,
        s => NearestSourceStep(s.LfeSources[(int)src]), (s, v) => s.LfeSources[(int)src] = SourceSteps[v], DevWhen.Live,
        I76.Game.Boot.Main.ApplyOutput, Slider: true);

    /// <summary>Keys of the table set by hand only, not stepped here: the EQs are curves of points, not one of a few values.</summary>
    public static readonly string[] HandSetOnly = ["tactile_eq", "game_eq", "movie_eq"];

    public static readonly DevSetting[] All =
    [
        new("draw_distance", ["look", "0", "1" + M], s => s.DrawDistanceChoice is { } d ? d + 1 : 0, (s, v) => s.DrawDistanceChoice = v == 0 ? null : v - 1, DevWhen.Live),
        new("mirror_range", Look(OriginalRecipe.MirrorRangeNames), s => OfChoice(s.MirrorRangeChoice), (s, v) => s.MirrorRangeChoice = ToChoice(v), DevWhen.Live),
        new("brake_lamps", Look(OriginalRecipe.BrakeLampNames), s => OfChoice(s.BrakeLampChoice), (s, v) => s.BrakeLampChoice = ToChoice(v), DevWhen.Live),
        new("mg_tracers", Look(OriginalRecipe.MgTracerNames), s => OfChoice(s.MgTracerChoice), (s, v) => s.MgTracerChoice = ToChoice(v), DevWhen.Live),
        new("sky_drift", Words(OriginalRecipe.SkyDriftNames), s => s.SkyDrift, (s, v) => s.SkyDrift = v, DevWhen.Live),
        new("death_orbit", Words(OriginalRecipe.DeathOrbitNames), s => s.DeathOrbit, (s, v) => s.DeathOrbit = v, DevWhen.Live),
        new("music_tracks", Words(OriginalRecipe.MusicTrackNames), s => s.MusicTracks, (s, v) => s.MusicTracks = v, DevWhen.Live),
        new("garage_radio", ["off", "on" + M], s => s.GarageRadio ? 1 : 0, (s, v) => s.GarageRadio = v != 0, DevWhen.NextScreen),
        new("melee_ai", ["0", "1" + M, "2" + M, "3" + M], s => (int)s.MeleeAi, (s, v) => s.MeleeAi = (I76.Sim.Missions.MeleeAiSkill)v, DevWhen.NextMission),
        new("effects_size", EffectNames, s => NearestStep(s.EffectsSize), (s, v) => s.EffectsSize = EffectSteps[v], DevWhen.Live),
        new("effects_brightness", EffectNames, s => NearestStep(s.EffectsBrightness), (s, v) => s.EffectsBrightness = EffectSteps[v], DevWhen.Live),
        new("effects_opacity", EffectNames, s => NearestStep(s.EffectsOpacity), (s, v) => s.EffectsOpacity = EffectSteps[v], DevWhen.Live),
        new("vegetation_density", VegetationNames, s => NearestVegetationStep(s.VegetationDensity), (s, v) => s.VegetationDensity = VegetationSteps[v], DevWhen.Live),
        new("ordnance_limit", Words(OriginalRecipe.OrdnanceNames), s => s.OrdnanceLimit, (s, v) => s.OrdnanceLimit = v, DevWhen.Live),
        new("modern_physics", FalseTrue, s => s.ModernPhysics ? 1 : 0, (s, v) => s.ModernPhysics = v != 0, DevWhen.Live),
        new("rumble", ["original", "enhanced" + M], s => s.RumbleMode == I76.Formats.Input.RumbleMode.Enhanced ? 1 : 0,
            (s, v) => s.RumbleMode = v != 0 ? I76.Formats.Input.RumbleMode.Enhanced : I76.Formats.Input.RumbleMode.Original, DevWhen.Live,
            s => ForceFeedback.Mode = s.RumbleMode),
        new("hd_textures", FalseTrue, s => s.HdTextures ? 1 : 0, (s, v) => s.HdTextures = v != 0, DevWhen.Live,
            s => I76.Game.Assets.HdTextures.Enabled = s.HdTextures),
        // 4x, 3x, 2x: only the sizes whose tex-world-Nx folder exists are stepped to; none, the row is greyed. Textures already built keep their size.
        new("hd_texture_size", ["4x", "3x" + M, "2x" + M], s => Array.IndexOf(I76.Formats.Shell.HdPackFolders.Sizes, s.HdTextureSize),
            (s, v) => s.HdTextureSize = I76.Formats.Shell.HdPackFolders.Sizes[v], DevWhen.NextMission,
            s => I76.Game.Assets.HdTextures.Size = s.HdTextureSize,
            Offered: v => I76.Game.Assets.HdTextures.AvailableSizes.Contains(I76.Formats.Shell.HdPackFolders.Sizes[v])),
        new("hd_menu_art", FalseTrue, s => s.HdMenuArt ? 1 : 0, (s, v) => s.HdMenuArt = v != 0, DevWhen.NextScreen,
            s => HdAssets.Enabled = s.HdMenuArt),
        new("lighting_calibration", ["off", "filmic" + M, "on" + M], s => !s.LightingCalibration ? 0 : s.LightingFilmic ? 1 : 2,
            (s, v) => { s.LightingCalibration = v != 0; s.LightingFilmic = v == 1; }, DevWhen.Live),
        new("crossover", CrossoverNames, s => NearestCrossoverStep(s.Crossover), (s, v) => s.Crossover = CrossoverSteps[v], DevWhen.Live),
        Source(I76.Formats.Input.TactileSource.Engine),
        Source(I76.Formats.Input.TactileSource.Road),
        Source(I76.Formats.Input.TactileSource.Hits),
        Source(I76.Formats.Input.TactileSource.Shots),
        Source(I76.Formats.Input.TactileSource.Explosions),
        new("dev_fps", FalseTrue, s => s.DevFps ? 1 : 0, (s, v) => s.DevFps = v != 0, DevWhen.Live),
        new("developer", FalseTrue, s => s.DeveloperTools ? 1 : 0, (s, v) => s.DeveloperTools = v != 0, DevWhen.NextStart),
    ];

    public static string WhenText(DevWhen w) => w switch { DevWhen.Live => "live", DevWhen.NextScreen => "next screen", DevWhen.NextMission => "next mission", _ => "next start" };

    /// <summary>The key's colour for when a change shows: green at once, amber the next screen or mission, grey the next start.</summary>
    public static Color WhenColour(DevWhen w) => w switch
    {
        DevWhen.Live => new Color(0.6f, 0.9f, 0.6f),
        DevWhen.NextScreen or DevWhen.NextMission => new Color(0.95f, 0.8f, 0.45f),
        _ => new Color(0.65f, 0.68f, 0.72f),
    };

    /// <summary>
    /// The section into <paramref name="col"/>: a legend, then two keys a line. A change sets <paramref name="settings"/>,
    /// saves it, runs the entry's Apply and then <paramref name="changed"/> (the host's own refresh and message).
    /// </summary>
    public static void Build(VBoxContainer col, ShellSettings settings, Action<ShellSettings, DevSetting>? changed)
    {
        var legend = DevScale.Sep(new HBoxContainer(), 10);
        foreach (var w in new[] { DevWhen.Live, DevWhen.NextMission, DevWhen.NextStart })
        {
            var l = new Label { Text = w == DevWhen.NextMission ? "next screen / mission" : WhenText(w) };
            l.AddThemeColorOverride("font_color", WhenColour(w));
            legend.AddChild(l);
        }
        legend.AddChild(new Label { Text = "(* the port's)" });
        col.AddChild(legend);
        var grid = DevScale.Grid(new GridContainer { Columns = 4 }, 8, 4);
        foreach (var e in All)
        {
            var name = DevScale.Min(new Label { Text = e.Key, TooltipText = WhenText(e.When) }, 136);
            name.AddThemeColorOverride("font_color", WhenColour(e.When));
            if (e.Slider)
            {
                grid.AddChild(name);
                grid.AddChild(SliderFor(e, settings, changed));
                continue;
            }
            var b = DevScale.Min(new Button { FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left, TooltipText = "click: next value, right click: back" }, 84);
            void Show() => b.Text = e.Values[Math.Clamp(e.Get(settings), 0, e.Values.Length - 1)];
            if (e.Offered is not null && !Enumerable.Range(0, e.Values.Length).Any(e.Offered))
            {
                b.Disabled = true;
                b.TooltipText = "no tex-world-4x/3x/2x folder in the user folder; a change shows from the next mission";
            }
            void Step(int by)
            {
                int n = e.Values.Length, v = e.Get(settings);
                // a key with Offered steps over the values it does not offer
                for (int i = 0; i < n; i++)
                {
                    v = ((Math.Clamp(v, 0, n - 1) + by) % n + n) % n;
                    if (e.Offered is null || e.Offered(v)) break;
                }
                if (e.Offered is not null && !e.Offered(v)) return;
                e.Set(settings, v);
                settings.Save();
                e.Apply?.Invoke(settings);
                changed?.Invoke(settings, e);
                Show();
            }
            b.Pressed += () => Step(+1);
            b.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) Step(-1); };
            Show();
            grid.AddChild(name);
            grid.AddChild(b);
        }
        col.AddChild(grid);
    }

    /// <summary>A key of many steps as a slider over its values with the value beside it: dragging applies each step as it passes.</summary>
    private static Control SliderFor(DevSetting e, ShellSettings settings, Action<ShellSettings, DevSetting>? changed)
    {
        var box = new HBoxContainer();
        var slider = DevScale.Min(new HSlider { MinValue = 0, MaxValue = e.Values.Length - 1, Step = 1, FocusMode = Control.FocusModeEnum.None,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Value = Math.Clamp(e.Get(settings), 0, e.Values.Length - 1) }, 60);
        var value = DevScale.Min(new Label(), 30);
        void Show() => value.Text = e.Values[Math.Clamp(e.Get(settings), 0, e.Values.Length - 1)];
        slider.ValueChanged += v =>
        {
            if ((int)v == e.Get(settings)) return;
            e.Set(settings, (int)v);
            settings.Save();
            e.Apply?.Invoke(settings);
            changed?.Invoke(settings, e);
            Show();
        };
        Show();
        box.AddChild(slider);
        box.AddChild(value);
        return box;
    }

    /// <summary>The panel's line for a change: the key, its value as the file writes it, and when it shows.</summary>
    public static string Line(DevSetting e, ShellSettings s) =>
        $"{e.Key} = {e.Values[Math.Clamp(e.Get(s), 0, e.Values.Length - 1)].TrimEnd('*')} ({WhenText(e.When)})";
}
