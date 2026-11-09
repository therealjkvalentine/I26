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
    Action<ShellSettings>? Apply = null, bool Slider = false, Func<int, bool>? Offered = null, string? Note = null);

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
    public static readonly int[] SpeedSteps = [0, 25, 50, 75, 100, 125, 150, 200];
    private static readonly string[] SpeedNames = [.. SpeedSteps.Select(p => p == I76.Sim.Views.PresentationSpeed.Default ? $"{p}" : $"{p}{M}")];
    public static int NearestSpeedStep(int percent) => Array.IndexOf(SpeedSteps, SpeedSteps.MinBy(p => Math.Abs(p - percent)));
    public static readonly int[] EffectSteps = [25, 50, 75, 100, 125, 150];
    private static readonly string[] EffectNames = [.. EffectSteps.Select(p => p == I76.Sim.Views.EffectsLook.Default ? $"{p}" : $"{p}{M}")];
    public static int NearestStep(int percent) => Array.IndexOf(EffectSteps, EffectSteps.MinBy(p => Math.Abs(p - percent)));

    /// <summary>target_fps's frame rates offered (0: off, the default); a value set by hand shows as the nearest.</summary>
    public static readonly int[] TargetSteps = [0, 60, 90, 120, 144];
    private static readonly string[] TargetNames = [.. TargetSteps.Select(f => f == 0 ? "0" : $"{f}{M}")];
    public static int NearestTargetStep(int fps) => Array.IndexOf(TargetSteps, TargetSteps.MinBy(f => Math.Abs(f - fps)));

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
    private static string[] SourceNames(int dflt) => [.. SourceSteps.Select(p => p == dflt ? $"{p}" : $"{p}{M}")];
    public static int NearestSourceStep(int percent) => Array.IndexOf(SourceSteps, SourceSteps.MinBy(p => Math.Abs(p - percent)));
    private static DevSetting Source(I76.Formats.Input.TactileSource src, string? note = null) => new(I76.Formats.Input.TactileSources.Keys[(int)src],
        SourceNames(I76.Formats.Input.TactileSources.DefaultOf((int)src)),
        s => NearestSourceStep(s.LfeSources[(int)src]), (s, v) => s.LfeSources[(int)src] = SourceSteps[v], DevWhen.Live,
        I76.Game.Boot.Main.ApplyOutput, Slider: true, Note: note);

    /// <summary>Keys on the panel for the developer's listening only, not in docs/playing.md's table (docs/dev-hooks.md has them): the shaker's dev-only sources.</summary>
    public static readonly string[] DevOnly = [.. I76.Formats.Input.TactileSources.DevOnly.Select(s => I76.Formats.Input.TactileSources.Keys[(int)s])];

    /// <summary>Keys of the table set by hand only, not stepped here: the EQs are curves of points, not one of a few values.</summary>
    public static readonly string[] HandSetOnly = ["tactile_eq", "game_eq", "movie_eq"];

    public static readonly DevSetting[] All =
    [
        new("mirror_range", Look(OriginalRecipe.MirrorRangeNames), s => OfChoice(s.MirrorRangeChoice), (s, v) => s.MirrorRangeChoice = ToChoice(v), DevWhen.Live),
        new("brake_lamps", Look(OriginalRecipe.BrakeLampNames), s => OfChoice(s.BrakeLampChoice), (s, v) => s.BrakeLampChoice = ToChoice(v), DevWhen.Live),
        new("mg_tracers", Look(OriginalRecipe.MgTracerNames), s => OfChoice(s.MgTracerChoice), (s, v) => s.MgTracerChoice = ToChoice(v), DevWhen.Live),
        new("sky_drift", Words(OriginalRecipe.SkyDriftNames), s => s.SkyDrift, (s, v) => s.SkyDrift = v, DevWhen.Live),
        new("death_orbit", Words(OriginalRecipe.DeathOrbitNames), s => s.DeathOrbit, (s, v) => s.DeathOrbit = v, DevWhen.Live),
        new("sky_speed", SpeedNames, s => NearestSpeedStep(s.SkySpeed), (s, v) => s.SkySpeed = SpeedSteps[v], DevWhen.NextMission),
        new("death_orbit_speed", SpeedNames, s => NearestSpeedStep(s.DeathOrbitSpeed), (s, v) => s.DeathOrbitSpeed = SpeedSteps[v], DevWhen.Live),
        new("music_tracks", Words(OriginalRecipe.MusicTrackNames), s => s.MusicTracks, (s, v) => s.MusicTracks = v, DevWhen.Live),
        new("garage_radio", ["off", "on" + M], s => s.GarageRadio ? 1 : 0, (s, v) => s.GarageRadio = v != 0, DevWhen.NextScreen),
        new("menu_warmup", ["off", "on" + M], s => s.MenuWarmup ? 1 : 0, (s, v) => s.MenuWarmup = v != 0, DevWhen.NextScreen),
        new("movie_ignition", ["off", "on" + M], s => s.MovieIgnition ? 1 : 0, (s, v) => s.MovieIgnition = v != 0, DevWhen.Live),
        new("briefing_while_loading", ["card_first", "movie_first" + M], s => (int)s.CardOrder, (s, v) => s.CardOrder = (I76.Game.Boot.CardOrder)v, DevWhen.NextMission),
        new("melee_ai", ["0", "1" + M, "2" + M, "3" + M], s => (int)s.MeleeAi, (s, v) => s.MeleeAi = (I76.Sim.Missions.MeleeAiSkill)v, DevWhen.NextMission),
        new("effects_size", EffectNames, s => NearestStep(s.EffectsSize), (s, v) => s.EffectsSize = EffectSteps[v], DevWhen.Live),
        new("effects_brightness", EffectNames, s => NearestStep(s.EffectsBrightness), (s, v) => s.EffectsBrightness = EffectSteps[v], DevWhen.Live),
        new("effects_opacity", EffectNames, s => NearestStep(s.EffectsOpacity), (s, v) => s.EffectsOpacity = EffectSteps[v], DevWhen.Live),
        new("vegetation_density", VegetationNames, s => NearestVegetationStep(s.VegetationDensity), (s, v) => s.VegetationDensity = VegetationSteps[v], DevWhen.Live),
        new("brush_impacts", ["off", "on" + M], s => s.BrushImpacts ? 1 : 0, (s, v) => s.BrushImpacts = v != 0, DevWhen.Live),
        new("ordnance_limit", Words(OriginalRecipe.OrdnanceNames), s => s.OrdnanceLimit, (s, v) => s.OrdnanceLimit = v, DevWhen.Live),
        new("modern_physics", FalseTrue, s => s.ModernPhysics ? 1 : 0, (s, v) => s.ModernPhysics = v != 0, DevWhen.Live),
        // a co-op game hosted from the shell (false, a melee respawn's standing start, is the unmarked one: the original has no co-op)
        new("coop_rolling_start", FalseTrue, s => s.CoopRollingStart ? 1 : 0, (s, v) => s.CoopRollingStart = v != 0, DevWhen.NextMission),
        new("early_tick", FalseTrue, s => s.EarlyTick ? 1 : 0, (s, v) => s.EarlyTick = v != 0, DevWhen.Live),
        new("draw_ahead", FalseTrue, s => s.DrawAhead ? 1 : 0, (s, v) => s.DrawAhead = v != 0, DevWhen.Live),
        new("sway_snap", ["false" + M, "true"], s => s.SwaySnap ? 1 : 0, (s, v) => s.SwaySnap = v != 0, DevWhen.Live),
        new("low_latency_present", FalseTrue, s => s.LowLatencyPresent ? 1 : 0, (s, v) => s.LowLatencyPresent = v != 0, DevWhen.Live,
            s => I76.Game.FrameLimiter.Apply(s)),
        new("low_latency_audio", FalseTrue, s => s.LowLatencyAudio ? 1 : 0, (s, v) => s.LowLatencyAudio = v != 0, DevWhen.NextStart),
        new("target_fps", TargetNames, s => NearestTargetStep(s.TargetFps), (s, v) => s.TargetFps = TargetSteps[v], DevWhen.Live),
        new("menu_fps", ["false", "true" + M], s => s.MenuFps ? 1 : 0, (s, v) => s.MenuFps = v != 0, DevWhen.NextScreen),
        new("aa_1997", FalseTrue, s => s.Antialias1997 ? 1 : 0, (s, v) => s.Antialias1997 = v != 0, DevWhen.Live),
        new("even_flash", FalseTrue, s => s.EvenFlash ? 1 : 0, (s, v) => s.EvenFlash = v != 0, DevWhen.Live),
        new("rumble", ["original", "enhanced" + M], s => s.RumbleMode == I76.Formats.Input.RumbleMode.Enhanced ? 1 : 0,
            (s, v) => s.RumbleMode = v != 0 ? I76.Formats.Input.RumbleMode.Enhanced : I76.Formats.Input.RumbleMode.Original, DevWhen.Live,
            s => ForceFeedback.Mode = s.RumbleMode),
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
        Source(I76.Formats.Input.TactileSource.Offroad),
        Source(I76.Formats.Input.TactileSource.Bumps),
        Source(I76.Formats.Input.TactileSource.Cracks, "rec. 100"),
        Source(I76.Formats.Input.TactileSource.Strip, "rec. 100"),
        // the modern look's dim cabin and driver behind the other cars' windows (Car Interiors' row until round 5)
        new("cabins_behind_glass", ["false", "true" + M], s => s.CabinsBehindGlass ? 1 : 0, (s, v) => s.CabinsBehindGlass = v != 0, DevWhen.Live,
            s => I76.Game.Assets.MaterialLibrary.ApplyCabinsBehindGlass(s.CabinsBehindGlass)),
        // the log file (Graphic Detail's Log File row until round 5): Godot reads it at start
        new("log_file", ["false", "true" + M], s => s.LogFile ? 1 : 0, (s, v) => s.LogFile = v != 0, DevWhen.NextStart),
        // a bug report's inputs.csv: the ring is made at a mission's start
        new("bug_report_inputs", ["false", "true" + M], s => s.BugReportInputs ? 1 : 0, (s, v) => s.BugReportInputs = v != 0, DevWhen.NextMission),
        new("developer", FalseTrue, s => s.DeveloperTools ? 1 : 0, (s, v) => s.DeveloperTools = v != 0, DevWhen.NextStart),
    ];

    /// <summary>
    /// Keys that have a menu row but are offered here too: Graphic Detail's Visibility Range (draw_distance, with "look" to go back to
    /// following the look), World Textures and Car &amp; Dash Textures (with the 3x and 2x the rows do not offer), Show FPS (dev_fps), and Hood Gloss level by level (the rows step in fives), the
    /// file's 0..100 on a slider. Not in docs/playing.md's table of settings without a row.
    /// </summary>
    private static readonly int[] WorldSizes = [0, 4, 3, 2], CarSizes = [0, 8, 4, 3, 2];
    private static readonly string[] WorldWords = ["1997", "4x" + M, "3x" + M, "2x" + M], CarWords = ["1997", "8x" + M, "4x" + M, "3x" + M, "2x" + M];

    public static readonly DevSetting[] WithRow =
    [
        new("draw_distance", ["look", "0", "1" + M, "2" + M], s => s.DrawDistanceChoice is { } d ? d + 1 : 0, (s, v) => s.DrawDistanceChoice = v == 0 ? null : v - 1, DevWhen.Live),
        // 1997 (the install's own) and 4x, 3x, 2x for the world; 1997, 8x, 4x, 3x, 2x for the cars and the dash (HdPackFolders.Sizes): only the
        // sizes whose tex-world-Nx folder exists are stepped to. Textures already built keep their size.
        new("world_textures", WorldWords, s => Math.Max(0, Array.IndexOf(WorldSizes, s.WorldTextures)), (s, v) => s.WorldTextures = WorldSizes[v], DevWhen.NextMission,
            s => I76.Game.Assets.HdTextures.WorldSize = s.WorldTextures,
            Offered: v => v == 0 || I76.Game.Assets.HdTextures.AvailableSizes.Contains(WorldSizes[v])),
        new("car_textures", CarWords, s => Math.Max(0, Array.IndexOf(CarSizes, s.CarTextures)), (s, v) => s.CarTextures = CarSizes[v], DevWhen.NextMission,
            s => I76.Game.Assets.HdTextures.CarSize = s.CarTextures,
            Offered: v => v == 0 || I76.Game.Assets.HdTextures.AvailableSizes.Contains(CarSizes[v])),
        new("dev_fps", FalseTrue, s => s.DevFps ? 1 : 0, (s, v) => s.DevFps = v != 0, DevWhen.Live),
        new("hood_gloss", [.. Enumerable.Range(I76.Sim.Views.PaintFinish.Min, I76.Sim.Views.PaintFinish.Max + 1).Select(v => v == I76.Sim.Views.PaintFinish.Original ? $"{v}" : $"{v}{M}")],
            s => I76.Sim.Views.PaintFinish.Clamp(s.HoodGloss), (s, v) => s.HoodGloss = v, DevWhen.Live,
            s => I76.Game.Assets.MaterialLibrary.ApplyHoodGloss(s.HoodGloss), Slider: true),
    ];

    /// <summary>
    /// The Night group: the modern look's night levels on sliders, live in a night mission (I76.Game.World.NightDial; docs/playing.md):
    /// night_moon, night_road and night_sky scale the moon's light on the ground, the road's share of it and the sky's gain as solved
    /// for the mission (1), night_chroma and night_warm set the moon light's colour on the ground (1, 1). The I76_NIGHT_ hooks win.
    /// </summary>
    public static readonly DevSetting[] Night =
    [
        Scale("night_moon", d => d.Moon, (d, v) => d with { Moon = v }),
        Scale("night_road", d => d.Road, (d, v) => d with { Road = v }),
        Scale("night_sky", d => d.Sky, (d, v) => d with { Sky = v }),
        Mix("night_chroma", I76.Formats.Imaging.LightingCalibration.NightChroma, d => d.Chroma, (d, v) => d with { Chroma = v }),
        Mix("night_warm", I76.Formats.Imaging.LightingCalibration.NightWarm, d => d.Warm, (d, v) => d with { Warm = v }),
    ];

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static DevSetting Scale(string key, Func<I76.Game.World.NightDial.Dial, double> get, Func<I76.Game.World.NightDial.Dial, double, I76.Game.World.NightDial.Dial> set) =>
        new(key, [.. I76.Game.World.NightDial.ScaleSteps.Select(v => v == 1 ? Num(v) : Num(v) + M)],
            s => I76.Game.World.NightDial.Nearest(I76.Game.World.NightDial.ScaleSteps, get(s.Night)), (s, i) => s.Night = set(s.Night, I76.Game.World.NightDial.ScaleSteps[i]),
            DevWhen.Live, Slider: true);
    private static DevSetting Mix(string key, double dflt, Func<I76.Game.World.NightDial.Dial, double> get, Func<I76.Game.World.NightDial.Dial, double, I76.Game.World.NightDial.Dial> set) =>
        new(key, [.. I76.Game.World.NightDial.MixSteps.Select(v => v == dflt ? Num(v) : Num(v) + M)],
            s => I76.Game.World.NightDial.NearestMix(get(s.Night)), (s, i) => s.Night = set(s.Night, I76.Game.World.NightDial.MixSteps[i]),
            DevWhen.Live, Slider: true);

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
        foreach (var e in All.Concat(WithRow)) Add(grid, e);
        col.AddChild(grid);
        // the Night group: the modern look's night levels, live in a night mission
        col.AddChild(new Label { Text = "Night (the modern look; 1 as solved for the mission)" });
        var night = DevScale.Grid(new GridContainer { Columns = 4 }, 8, 4);
        foreach (var e in Night) Add(night, e);
        col.AddChild(night);

        void Add(GridContainer grid, DevSetting e)
        {
            var name = DevScale.Min(new Label { Text = e.Note is null ? e.Key : $"{e.Key} ({e.Note})", TooltipText = WhenText(e.When) }, 136);
            name.AddThemeColorOverride("font_color", WhenColour(e.When));
            if (e.Slider)
            {
                grid.AddChild(name);
                grid.AddChild(SliderFor(e, settings, changed));
                return;
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
