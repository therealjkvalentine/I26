using Godot;
using I76.Game.Shell;
using I76.Game.Sim;

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
    /// <summary>Marks a value the original game does not have (ShellSettings.PortMark).</summary>
    public const string PortMark = ShellSettings.PortMark;
    private const string M = PortMark;
    /// <summary>briefing_card_hold's steps on the panel, seconds (the file takes any from 0 to 10; 0, no hold of its own, is the one unmarked).</summary>
    private static readonly double[] CardHolds = [0, 0.7, 1, 1.5, 2, 3, 4, 5, 6, 8, 10];
    private static int NearestCardHold(double v) => System.Array.IndexOf(CardHolds, CardHolds.MinBy(h => System.Math.Abs(h - v)));
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
    /// <summary>The death orbit's percentages offered: slow ones added for the owner's "slower still"; 100 is the original's 8 degrees a tick.</summary>
    public static readonly int[] OrbitSpeedSteps = [0, 10, 15, 25, 35, 50, 75, 100, 150, 200];
    private static readonly string[] OrbitSpeedNames = [.. OrbitSpeedSteps.Select(p => p == I76.Sim.Views.PresentationSpeed.DefaultOrbit ? $"{p}" : $"{p}{M}")];
    public static readonly int[] EffectSteps = [25, 50, 75, 100, 125, 150];
    private static readonly string[] EffectNames = [.. EffectSteps.Select(p => p == I76.Sim.Views.EffectsLook.Default ? $"{p}" : $"{p}{M}")];
    public static int NearestStep(int percent) => Array.IndexOf(EffectSteps, EffectSteps.MinBy(p => Math.Abs(p - percent)));
    public static readonly int[] HaloSteps = [25, 50, 65, 75, 100, 125, 150];
    private static readonly string[] HaloNames = [.. HaloSteps.Select(p => p == I76.Game.Sim.LampHalo.Default ? $"{p}" : $"{p}{M}")];
    public static int NearestHaloStep(int percent) => Array.IndexOf(HaloSteps, HaloSteps.MinBy(p => Math.Abs(p - percent)));
    /// <summary>The dash lamps' values offered (I76.Game.Sim.DashLight): 0 the original's needles, unlit at night.</summary>
    public static readonly int[] DashLightSteps = [0, 25, 50, 75, 100, 125, 150];
    private static readonly string[] DashLightNames = [.. DashLightSteps.Select(p => p == I76.Game.Sim.DashLight.Default ? $"{p}" : $"{p}{M}")];
    /// <summary>The headlamps' throws offered (I76.Game.Sim.HeadlampThrow): 75 the reach before the key.</summary>
    public static readonly int[] HeadlampThrowSteps = [50, 75, 90, 100, 110, 125, 150];
    private static readonly string[] HeadlampThrowNames = [.. HeadlampThrowSteps.Select(p => p == I76.Game.Sim.HeadlampThrow.Default ? $"{p}" : $"{p}{M}")];
    /// <summary>The muzzle light's reaches offered (I76.Game.Sim.MuzzleReach): 0 the light before the key, on the firing car alone.</summary>
    public static readonly int[] MuzzleReachSteps = [0, 25, 50, 75, 100, 125, 150, 200];
    private static readonly string[] MuzzleReachNames = [.. MuzzleReachSteps.Select(p => p == I76.Game.Sim.MuzzleReach.Default ? $"{p}" : $"{p}{M}")];
    /// <summary>SmokeStart's steps on the panel's sliders (each includes its default and the value before the keys; a value set by hand shows as the nearest).</summary>
    public static readonly int[] SmokeStartDarkSteps = [0, 10, 20, 30, 40, 50, 60, 70, 80, 85, 90];
    public static readonly int[] SmokeStartDensitySteps = [100, 120, 135, 150, 165, 180, 200, 225, 250];
    public static readonly int[] SmokeStartWidthSteps = [30, 40, 50, 55, 60, 70, 80, 90, 100];
    public static readonly int[] SmokeFadeSteps = [60, 90, 120, 150, 180, 240, 300, 450, 600];
    private static DevSetting StartStep(string key, int[] steps, int before, Func<ShellSettings, int> get, Action<ShellSettings, int> set) =>
        new(key, [.. steps.Select(p => p == before ? $"{p}" : $"{p}{M}")], s => Nearest(steps, get(s)), (s, v) => set(s, steps[v]), DevWhen.Live,
            s => s.ApplySmokeStart(), Slider: true);
    /// <summary>The smoke's lingers offered (I76.Game.Sim.SmokeLinger.Steps): 100 the lives as they were, the one unmarked.</summary>
    private static readonly string[] SmokeLingerNames = [.. I76.Game.Sim.SmokeLinger.Steps.Select(p => p == 100 ? $"{p}" : $"{p}{M}")];

    /// <summary>The side-view mirrors' keys' values offered (I76.Game.Sim.MirrorBands): the one render's field across (45 is the rear mirror alone, the original's), each side mirror's share of the band outside the rear's, and where it sits; a value set by hand shows as the nearest.</summary>
    public static readonly int[] MirrorFovSteps = [45, 60, 75, 90, 105, 120], MirrorShareSteps = [25, 50, 75, 100], MirrorYawSteps = [30, 35, 40, 45, 50, 55, 60], MirrorDropSteps = [0, 50, 100, 125, 150, 200, 300], MirrorOverlapSteps = [0, 3, 6, 9, 12, 15, 20];
    private static readonly string[] MirrorFovNames = [.. MirrorFovSteps.Select(d => d == 45 ? $"{d}" : $"{d}{M}")];
    /// <summary>[port] mirror_round's values offered: the rear mirror's corner radius in its art's pixels, 0 the art's own (the original's).</summary>
    public static readonly int[] MirrorRoundSteps = [0, 6, 10, 16, 24];
    private static readonly string[] MirrorRoundNames = [.. MirrorRoundSteps.Select(d => d == 0 ? "0" : $"{d}{M}")];
    /// <summary>[port] side_mirror_round's values offered: the door mirrors' glass corners, percent of its shorter side (0 the glass's own, the original's; 50 round ends).</summary>
    public static readonly int[] GlassRoundSteps = [0, 15, 30, 50];
    private static readonly string[] GlassRoundNames = [.. GlassRoundSteps.Select(d => d == 0 ? "0" : $"{d}{M}")];
    /// <summary>[port] side_mirror_rim_out's values offered: the door mirrors' moulding's outer edge, in its own widths past the housing's outline (0 the housing's straight outline).</summary>
    public static readonly int[] RimOutSteps = [0, 1, 2, 3, 4];
    /// <summary>[port] side_mirror_tilt_right / _left's values offered: degrees the door mirror's glass turns toward the driver inside its housing (MirrorAim.Tilt).</summary>
    public static readonly int[] TiltSteps = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45];
    private static string[] TiltNames(int dflt) => [.. TiltSteps.Select(d => d == dflt ? $"{d}" : $"{d}{M}")];
    /// <summary>[port] side_mirror_face_over_left / _right's values offered: percent of the proud face's height its sides stand past the least that covers the housing end's corners.</summary>
    public static readonly int[] FaceOverSteps = [0, 3, 6, 10, 15, 20, 25, 30, 40, 50, 60];
    private static string[] FaceOverNames(int dflt) => [.. FaceOverSteps.Select(d => d == dflt ? $"{d}" : $"{d}{M}")];
    private static readonly string[] RimOutNames = [.. RimOutSteps.Select(d => d == 0 ? "0" : $"{d}{M}")];
    private static readonly string[] MirrorShareNames = [.. MirrorShareSteps.Select(p => p == 100 ? $"{p}" : $"{p}{M}")];
    private static readonly string[] MirrorYawNames = [.. MirrorYawSteps.Select(d => d == 45 ? $"{d}" : $"{d}{M}")];
    private static readonly string[] MirrorDropNames = [.. MirrorDropSteps.Select(p => p == 125 ? $"{p}" : $"{p}{M}")];
    private static readonly string[] MirrorOverlapNames = [.. MirrorOverlapSteps.Select(d => d == 6 ? $"{d}" : $"{d}{M}")];
    /// <summary>[port] bush_knock's values: off (no knock, as the original, which has no plants a car goes through), then the reviews' variants A to H.</summary>
    public static readonly string[] BushKnockNames = ["off", .. I76.Formats.Vegetation.BrushKnock.Variants.Select(v => v.Letter + M)];
    private static string BushKnockName(string value) => value == "off" ? value : value + M;
    public static int Nearest(int[] steps, int value) => Array.IndexOf(steps, steps.MinBy(p => Math.Abs(p - value)));

    /// <summary>target_fps's frame rates offered (0: off, the default); a value set by hand shows as the nearest.</summary>
    public static readonly int[] TargetSteps = [0, 60, 90, 120, 144];
    private static readonly string[] TargetNames = [.. TargetSteps.Select(f => f == 0 ? "0" : $"{f}{M}")];
    public static int NearestTargetStep(int fps) => Array.IndexOf(TargetSteps, TargetSteps.MinBy(f => Math.Abs(f - fps)));

    /// <summary>render_scale_cap's step: auto, off, then 512 to 4096 MiB (a number set by hand shows as the nearest of them).</summary>
    public static int RenderScaleCapStep(int? cap) => cap switch
    {
        null => 0,
        <= 0 => 1,
        _ => 2 + Math.Clamp((int)Math.Round(Math.Log2(cap.Value / 512.0)), 0, 3),
    };

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

    /// <summary>Keys of the table set by hand only, not stepped here: the EQs are curves of points, opentrack_path a path, not one of a few values.</summary>
    public static readonly string[] HandSetOnly = ["tactile_eq", "game_eq", "movie_eq", "opentrack_path", "fov_window", "fork_version", "host_bot_skill_difficulty", "turret_slew"];   // from fov_window: rows' keys (Field of View, the host form's Bot Skill, Play Options' Turrets) and the file's own step count

    public static readonly DevSetting[] All =
    [
        new("mirror_range", Look(OriginalRecipe.MirrorRangeNames), s => OfChoice(s.MirrorRangeChoice), (s, v) => s.MirrorRangeChoice = ToChoice(v), DevWhen.Live),
        new("side_mirrors", ["off", "on" + M, "auto" + M, "left" + M], s => (int)s.SideMirrors, (s, v) => s.SideMirrors = (I76.Game.Sim.SideMirrorMode)v, DevWhen.Live),
        new("mirror_round", MirrorRoundNames, s => Nearest(MirrorRoundSteps, s.MirrorRound), (s, v) => s.MirrorRound = MirrorRoundSteps[v], DevWhen.Live),
        new("side_mirror_round", GlassRoundNames, s => Nearest(GlassRoundSteps, s.SideMirrorRound), (s, v) => s.SideMirrorRound = GlassRoundSteps[v], DevWhen.Live),
        new("side_mirror_head", ["art", "turned" + M], s => s.SideMirrorHeadTurned ? 1 : 0, (s, v) => s.SideMirrorHeadTurned = v == 1, DevWhen.NextMission),
        new("side_mirror_tilt_right", TiltNames(I76.Game.Sim.MirrorAim.DefaultTiltRight), s => Nearest(TiltSteps, s.SideMirrorTiltRight), (s, v) => s.SideMirrorTiltRight = TiltSteps[v], DevWhen.NextMission),
        new("side_mirror_tilt_left", TiltNames(I76.Game.Sim.MirrorAim.DefaultTiltLeft), s => Nearest(TiltSteps, s.SideMirrorTiltLeft), (s, v) => s.SideMirrorTiltLeft = TiltSteps[v], DevWhen.NextMission),
        new("side_mirror_face", ["turned", "proud" + M, "inset" + M], s => s.SideMirrorFaceTurned ? 0 : s.SideMirrorFaceProud ? 1 : 2, (s, v) => { s.SideMirrorFaceTurned = v == 0; s.SideMirrorFaceProud = v == 1; }, DevWhen.NextMission),
        new("cockpit_trim", [.. I76.Game.Sim.CockpitTrim.Words.Select((w, i) => i == 0 ? w : w + M)], s => (int)s.CockpitTrim, (s, v) => s.CockpitTrim = (I76.Game.Sim.CockpitTrim.Style)v, DevWhen.NextMission),
        new("side_mirror_face_over_right", FaceOverNames(I76.Game.Sim.DoorFace.DefaultOverRight), s => Nearest(FaceOverSteps, s.SideMirrorFaceOverRight), (s, v) => s.SideMirrorFaceOverRight = FaceOverSteps[v], DevWhen.Live),
        new("side_mirror_face_over_left", FaceOverNames(I76.Game.Sim.DoorFace.DefaultOverLeft), s => Nearest(FaceOverSteps, s.SideMirrorFaceOverLeft), (s, v) => s.SideMirrorFaceOverLeft = FaceOverSteps[v], DevWhen.Live),
        new("side_mirror_rim_out", RimOutNames, s => Nearest(RimOutSteps, s.SideMirrorRimOut), (s, v) => s.SideMirrorRimOut = RimOutSteps[v], DevWhen.Live),
        new("mirror_fov", MirrorFovNames, s => Nearest(MirrorFovSteps, s.MirrorFov), (s, v) => s.MirrorFov = MirrorFovSteps[v], DevWhen.Live),
        new("side_mirror_share", MirrorShareNames, s => Nearest(MirrorShareSteps, s.SideMirrorShare), (s, v) => s.SideMirrorShare = MirrorShareSteps[v], DevWhen.Live),
        new("side_mirror_angle", MirrorYawNames, s => Nearest(MirrorYawSteps, s.SideMirrorAngle), (s, v) => s.SideMirrorAngle = MirrorYawSteps[v], DevWhen.Live),
        new("side_mirror_drop", MirrorDropNames, s => Nearest(MirrorDropSteps, s.SideMirrorDrop), (s, v) => s.SideMirrorDrop = MirrorDropSteps[v], DevWhen.Live),
        new("side_mirror_overlap", MirrorOverlapNames, s => Nearest(MirrorOverlapSteps, s.SideMirrorOverlap), (s, v) => s.SideMirrorOverlap = MirrorOverlapSteps[v], DevWhen.Live),
        new("side_mirror_render", ["shared", "own" + M], s => (int)s.SideMirrorRender, (s, v) => s.SideMirrorRender = (I76.Game.Sim.SideMirrorRender)v, DevWhen.Live),
        new("brake_lamps", Look(OriginalRecipe.BrakeLampNames), s => OfChoice(s.BrakeLampChoice), (s, v) => s.BrakeLampChoice = ToChoice(v), DevWhen.Live),
        new("mg_tracers", Look(OriginalRecipe.MgTracerNames), s => OfChoice(s.MgTracerChoice), (s, v) => s.MgTracerChoice = ToChoice(v), DevWhen.Live),
        new("specials_panel", Look(OriginalRecipe.SpecialsPanelNames), s => OfChoice(s.SpecialsPanelChoice), (s, v) => s.SpecialsPanelChoice = ToChoice(v), DevWhen.Live),
        new("sky_drift", Words(OriginalRecipe.SkyDriftNames), s => s.SkyDrift, (s, v) => s.SkyDrift = v, DevWhen.Live),
        new("death_orbit", Words(OriginalRecipe.DeathOrbitNames), s => s.DeathOrbit, (s, v) => s.DeathOrbit = v, DevWhen.Live),
        new("sky_speed", SpeedNames, s => NearestSpeedStep(s.SkySpeed), (s, v) => s.SkySpeed = SpeedSteps[v], DevWhen.NextMission),
        new("death_orbit_speed", OrbitSpeedNames, s => Nearest(OrbitSpeedSteps, s.DeathOrbitSpeed), (s, v) => s.DeathOrbitSpeed = OrbitSpeedSteps[v], DevWhen.Live),
        new("helicopter_hearing", Look(OriginalRecipe.HelicopterHearingNames), s => OfChoice(s.HelicopterHearingChoice), (s, v) => s.HelicopterHearingChoice = ToChoice(v), DevWhen.Live),
        new("wreck_solid", Look(OriginalRecipe.WreckSolidNames), s => OfChoice(s.WreckSolidChoice), (s, v) => s.WreckSolidChoice = ToChoice(v), DevWhen.Live),
        new("music_tracks", Words(OriginalRecipe.MusicTrackNames), s => s.MusicTracks, (s, v) => s.MusicTracks = v, DevWhen.Live),
        new("garage_radio", ["off", "on" + M], s => s.GarageRadio ? 1 : 0, (s, v) => s.GarageRadio = v != 0, DevWhen.NextScreen),
        new("menu_warmup", ["off", "on" + M], s => s.MenuWarmup ? 1 : 0, (s, v) => s.MenuWarmup = v != 0, DevWhen.NextScreen),
        new("movie_ignition", ["off", "on" + M], s => s.MovieIgnition ? 1 : 0, (s, v) => s.MovieIgnition = v != 0, DevWhen.Live),
        new("briefing_while_loading", ["off", "on" + M], s => s.BriefingWhileLoading ? 1 : 0, (s, v) => s.BriefingWhileLoading = v != 0, DevWhen.NextMission),
        new("load_under_briefing", ["off", "on" + M], s => s.LoadUnderBriefing ? 1 : 0, (s, v) => s.LoadUnderBriefing = v != 0, DevWhen.NextMission),
        new("movie_decode", ["thread" + M, "engine"], s => s.MovieDecode == I76.Formats.Video.MovieDecodeMode.Engine ? 1 : 0,
            (s, v) => s.MovieDecode = v != 0 ? I76.Formats.Video.MovieDecodeMode.Engine : I76.Formats.Video.MovieDecodeMode.Thread, DevWhen.NextScreen),
        new("briefing_card_hold", [.. CardHolds.Select(h => h.ToString(System.Globalization.CultureInfo.InvariantCulture) + (h == 0 ? "" : M))],
            s => NearestCardHold(s.BriefingCardHold), (s, v) => s.BriefingCardHold = CardHolds[v], DevWhen.NextMission),
        new("melee_ai", ["0", "1" + M, "2" + M, "3" + M], s => (int)s.MeleeAi, (s, v) => s.MeleeAi = (I76.Sim.Missions.MeleeAiSkill)v, DevWhen.NextMission),
        new("effects_size", EffectNames, s => NearestStep(s.EffectsSize), (s, v) => s.EffectsSize = EffectSteps[v], DevWhen.Live),
        new("effects_brightness", EffectNames, s => NearestStep(s.EffectsBrightness), (s, v) => s.EffectsBrightness = EffectSteps[v], DevWhen.Live),
        new("effects_opacity", EffectNames, s => NearestStep(s.EffectsOpacity), (s, v) => s.EffectsOpacity = EffectSteps[v], DevWhen.Live),
        new("vegetation_density", VegetationNames, s => NearestVegetationStep(s.VegetationDensity), (s, v) => s.VegetationDensity = VegetationSteps[v], DevWhen.Live),
        new("brush_impacts", ["off", "on" + M], s => s.BrushImpacts ? 1 : 0, (s, v) => s.BrushImpacts = v != 0, DevWhen.Live),
        new("bush_knock", BushKnockNames, s => Array.IndexOf(BushKnockNames, BushKnockName(ShellSettings.ParseBushKnock(s.BushKnock))),
            (s, v) => s.BushKnock = BushKnockNames[v].Replace(M, ""), DevWhen.Live),
        new("ordnance_limit", Words(OriginalRecipe.OrdnanceNames), s => s.OrdnanceLimit, (s, v) => s.OrdnanceLimit = v, DevWhen.Live),
        new("modern_physics", FalseTrue, s => s.ModernPhysics ? 1 : 0, (s, v) => s.ModernPhysics = v != 0, DevWhen.Live),
        // a co-op game hosted from the shell (false, a melee respawn's standing start, is the unmarked one: the original has no co-op)
        new("coop_rolling_start", FalseTrue, s => s.CoopRollingStart ? 1 : 0, (s, v) => s.CoopRollingStart = v != 0, DevWhen.NextMission),
        new("coop_copy_key_cars", FalseTrue, s => s.CoopCopyKeyCars ? 1 : 0, (s, v) => s.CoopCopyKeyCars = v != 0, DevWhen.NextMission),
        new("early_tick", FalseTrue, s => s.EarlyTick ? 1 : 0, (s, v) => s.EarlyTick = v != 0, DevWhen.Live),
        new("draw_ahead", FalseTrue, s => s.DrawAhead ? 1 : 0, (s, v) => s.DrawAhead = v != 0, DevWhen.Live),
        new("road_grain", FalseTrue, s => s.RoadGrain ? 1 : 0, (s, v) => s.RoadGrain = v != 0, DevWhen.NextMission),
        new("sway_snap", ["false" + M, "true"], s => s.SwaySnap ? 1 : 0, (s, v) => s.SwaySnap = v != 0, DevWhen.Live),
        new("low_latency_present", FalseTrue, s => s.LowLatencyPresent ? 1 : 0, (s, v) => s.LowLatencyPresent = v != 0, DevWhen.Live,
            s => I76.Game.FrameLimiter.Apply(s)),
        new("low_latency_audio", FalseTrue, s => s.LowLatencyAudio ? 1 : 0, (s, v) => s.LowLatencyAudio = v != 0, DevWhen.NextStart),
        new("target_fps", TargetNames, s => NearestTargetStep(s.TargetFps), (s, v) => s.TargetFps = TargetSteps[v], DevWhen.Live),
        // Render Scale's video memory cap: auto, none, or the view's buffers in MiB (a number set by hand shows as the nearest step)
        new("render_scale_cap", ["auto" + M, "off", "512" + M, "1024" + M, "2048" + M, "4096" + M], s => RenderScaleCapStep(s.RenderScaleCap),
            (s, v) => s.RenderScaleCap = v switch { 0 => null, 1 => 0, _ => 128 << v }, DevWhen.Live, s => I76.Game.Boot.RenderScale.Apply(s)),
        new("menu_fps", ["false", "true" + M], s => s.MenuFps ? 1 : 0, (s, v) => s.MenuFps = v != 0, DevWhen.NextScreen),
        new("aa_1997", FalseTrue, s => s.Antialias1997 ? 1 : 0, (s, v) => s.Antialias1997 = v != 0, DevWhen.Live),
        new("even_flash", FalseTrue, s => s.EvenFlash ? 1 : 0, (s, v) => s.EvenFlash = v != 0, DevWhen.Live),
        new("fast_look_switch", FalseTrue, s => s.FastLookSwitch ? 1 : 0, (s, v) => s.FastLookSwitch = v != 0, DevWhen.Live,
            s => I76.Game.Assets.TextureFactory.FastLookSwitch = s.FastLookSwitch),
        new("lamp_ramp", ["off", "on" + M], s => s.LampRamp ? 1 : 0, (s, v) => s.LampRamp = v != 0, DevWhen.Live),
        new("lamp_halo", HaloNames, s => NearestHaloStep(s.LampHalo), (s, v) => s.LampHalo = HaloSteps[v], DevWhen.Live),
        new("dash_light", DashLightNames, s => Nearest(DashLightSteps, s.DashLight), (s, v) => s.DashLight = DashLightSteps[v], DevWhen.Live,
            s => { I76.Game.Sim.DashLight.Percent = s.DashLight; I76.Game.Sim.DashLight.Refresh(); }),
        new("headlamp_throw", HeadlampThrowNames, s => Nearest(HeadlampThrowSteps, s.HeadlampThrow), (s, v) => s.HeadlampThrow = HeadlampThrowSteps[v], DevWhen.Live,
            s => I76.Game.Sim.HeadlampThrow.Percent = s.HeadlampThrow),
        new("muzzle_reach", MuzzleReachNames, s => Nearest(MuzzleReachSteps, s.MuzzleReach), (s, v) => s.MuzzleReach = MuzzleReachSteps[v], DevWhen.Live,
            s => I76.Game.Sim.MuzzleReach.Percent = s.MuzzleReach),
        new("smoke_linger", SmokeLingerNames, s => Nearest(I76.Game.Sim.SmokeLinger.Steps, s.SmokeLinger), (s, v) => s.SmokeLinger = I76.Game.Sim.SmokeLinger.Steps[v], DevWhen.Live,
            s => I76.Game.Sim.SmokeLinger.Percent = s.SmokeLinger),
        // Smoke Linger Long's fresh smoke (SmokeStart): live, so the start can be tuned while a wreck burns; the unmarked value is the one before the keys
        StartStep("smoke_start_dark", SmokeStartDarkSteps, SmokeStart.BeforeDark, s => s.SmokeStartDark, (s, v) => s.SmokeStartDark = v),
        StartStep("smoke_start_density", SmokeStartDensitySteps, SmokeStart.BeforeDensity, s => s.SmokeStartDensity, (s, v) => s.SmokeStartDensity = v),
        StartStep("smoke_start_width", SmokeStartWidthSteps, SmokeStart.BeforeWidth, s => s.SmokeStartWidth, (s, v) => s.SmokeStartWidth = v),
        StartStep("smoke_fade", SmokeFadeSteps, SmokeStart.BeforeFade, s => s.SmokeFade, (s, v) => s.SmokeFade = v),
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
        Source(I76.Formats.Input.TactileSource.Helicopters),
        Source(I76.Formats.Input.TactileSource.Cracks, "rec. 100"),
        Source(I76.Formats.Input.TactileSource.Strip, "rec. 100"),
        // the modern look's dim cabin and driver behind the other cars' windows (Car Interiors' row until round 5)
        new("car_interiors", ["false", "true" + M], s => s.CabinsBehindGlass ? 1 : 0, (s, v) => s.CabinsBehindGlass = v != 0, DevWhen.Live,
            s => I76.Game.Assets.MaterialLibrary.ApplyCabinsBehindGlass(s.CabinsBehindGlass)),
        // the log file (Graphic Detail's Log File row until round 5): Godot reads it at start
        new("log_file", ["false", "true" + M], s => s.LogFile ? 1 : 0, (s, v) => s.LogFile = v != 0, DevWhen.NextStart),
        // a bug report's inputs.csv: the ring is made at a mission's start
        new("bug_report_inputs", ["false", "true" + M], s => s.BugReportInputs ? 1 : 0, (s, v) => s.BugReportInputs = v != 0, DevWhen.NextMission),
        new("developer", FalseTrue, s => s.DeveloperTools ? 1 : 0, (s, v) => s.DeveloperTools = v != 0, DevWhen.NextStart),
    ];

    /// <summary>
    /// Keys that have a menu row but are offered here too: Graphic Detail's Visibility Range (draw_distance, with "look" to go back to
    /// following the look), World Textures and Car &amp; Dash Textures (with the 3x and 2x the rows do not offer), Show FPS (dev_fps), Hood Gloss level by level (the rows step in fives), the
    /// file's 0..100 on a slider, and Play Options' Solid Cliffs. Not in docs/playing.md's table of settings without a row. Play Options'
    /// Smart A.I. is the Smart A.I. group's check boxes (<see cref="SmartAiGroup"/>), one a behaviour.
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
        // Play Options' Solid Cliffs
        new("solid_cliffs", ["off", "on" + M], s => s.SolidCliffs ? 1 : 0, (s, v) => s.SolidCliffs = v != 0, DevWhen.NextMission),
        new("hood_gloss", [.. Enumerable.Range(I76.Sim.Views.PaintFinish.Min, I76.Sim.Views.PaintFinish.Max + 1).Select(v => v == I76.Sim.Views.PaintFinish.Original ? $"{v}" : $"{v}{M}")],
            s => I76.Sim.Views.PaintFinish.Clamp(s.HoodGloss), (s, v) => s.HoodGloss = v, DevWhen.Live,
            s => I76.Game.Assets.MaterialLibrary.ApplyHoodGloss(s.HoodGloss), Slider: true),
    ];

    /// <summary>
    /// Keys set elsewhere that the panel steps too, not in docs/playing.md's table: net_names (Ctrl+T in a network game keeps its
    /// last choice there; docs/deviations.md) and telemetry_format (with [port] telemetry or I76_TELEMETRY_UDP; docs/dev-hooks.md).
    /// </summary>
    public static readonly DevSetting[] Elsewhere =
    [
        // every name shown at a network game's start (Ctrl+T toggles it in the game; the original starts each game with it off)
        new("net_names", FalseTrue, s => s.NetNames ? 1 : 0, (s, v) => s.NetNames = v != 0, DevWhen.NextMission),
        // the telemetry feed's packet for a target that names none ([port] telemetry or I76_TELEMETRY_UDP sets the targets)
        new("telemetry_format", ["i26", "outsim" + M, "outgauge" + M],
            s => (int)(TelemetryPackets.ParseFormat(s.TelemetryFormat) ?? TelemetryFormat.I26),
            (s, v) => s.TelemetryFormat = v == 0 ? null : ((TelemetryFormat)v).ToString().ToLowerInvariant(), DevWhen.NextMission),
    ];

    /// <summary>
    /// The Night group: the modern look's night levels on sliders, live in a night mission (I76.Game.World.NightDial; docs/playing.md):
    /// night_moon, night_road and night_sky scale the moon's light on the ground, the road's share of it and the sky's gain as solved
    /// for the mission (1; the moon's and the sky's default is 1.4), night_chroma and night_warm set the moon light's colour on the ground (1, 1), night_station_light the
    /// scenery's own night lights and lit faces in percent (100, StationLight). The I76_NIGHT_ hooks win.
    /// </summary>
    public static readonly DevSetting[] Night =
    [
        Scale("night_moon", I76.Game.World.NightDial.MoonDefault, d => d.Moon, (d, v) => d with { Moon = v }),
        Scale("night_road", 1, d => d.Road, (d, v) => d with { Road = v }),
        Scale("night_sky", I76.Game.World.NightDial.SkyDefault, d => d.Sky, (d, v) => d with { Sky = v }),
        Mix("night_chroma", I76.Formats.Imaging.LightingCalibration.NightChroma, d => d.Chroma, (d, v) => d with { Chroma = v }),
        Mix("night_warm", I76.Formats.Imaging.LightingCalibration.NightWarm, d => d.Warm, (d, v) => d with { Warm = v }),
        new("night_station_light", [.. I76.Game.World.StationLight.Steps.Select(p => p == I76.Game.World.StationLight.Default ? $"{p}" : $"{p}{M}")],
            s => I76.Game.World.StationLight.Nearest(s.NightStationLight), (s, v) => s.NightStationLight = I76.Game.World.StationLight.Steps[v], DevWhen.Live, Slider: true),
    ];

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static DevSetting Scale(string key, double dflt, Func<I76.Game.World.NightDial.Dial, double> get, Func<I76.Game.World.NightDial.Dial, double, I76.Game.World.NightDial.Dial> set) =>
        new(key, [.. I76.Game.World.NightDial.ScaleSteps.Select(v => v == dflt ? Num(v) : Num(v) + M)],
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
        // Smart A.I.'s behaviours first, a check box each (the keys' grid below is long)
        SmartAiGroup(col, settings, changed);
        var grid = DevScale.Grid(new GridContainer { Columns = 4 }, 8, 4);
        foreach (var e in All.Concat(WithRow).Concat(Elsewhere)) Add(grid, e);
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

    /// <summary>The smart_ai key as the Smart A.I. group's line on the panel: the key's own and how it reads now (the panel's message for a change).</summary>
    public static readonly DevSetting SmartAiKey = new("smart_ai", ["off", "on" + M, "custom" + M], s => ShellSettings.SmartAiRowIndex(s.SmartAi), (_, _) => { }, DevWhen.NextMission);

    /// <summary>
    /// The Smart A.I. group (Play Options' Smart A.I., [port] smart_ai; docs/deviations.md "Smarter opponents"): a check box a behaviour
    /// (I76.Sim.Missions.SmartAiNames.Bits), so one can be tried alone. A tick writes the key as the row does (all of them "all", none
    /// leaves it out, some their names: the row shows Custom); from the next mission start.
    /// </summary>
    private static void SmartAiGroup(VBoxContainer col, ShellSettings settings, Action<ShellSettings, DevSetting>? changed)
    {
        var head = new Label { Text = "Smart A.I. (Play Options' row; the fork's; next mission)", TooltipText = I76.Game.Shell.MenuTips.Of(PlayOptionsScreen.SmartAiRow) ?? "" };
        head.AddThemeColorOverride("font_color", WhenColour(DevWhen.NextMission));
        col.AddChild(head);
        var grid = DevScale.Grid(new GridContainer { Columns = 3 }, 8, 4);
        foreach (var (name, bit) in I76.Sim.Missions.SmartAiNames.Bits)
        {
            var box = new CheckBox { Text = name, FocusMode = Control.FocusModeEnum.None, TooltipText = DevTips.SmartAiBit(name) ?? name,
                ButtonPressed = (settings.SmartAi & bit) != 0 };
            box.Toggled += on =>
            {
                var bits = settings.SmartAi;
                settings.SetSmartAi(on ? bits | bit : bits & ~bit);
                settings.Save();
                changed?.Invoke(settings, SmartAiKey);
            };
            grid.AddChild(box);
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
    public static string Line(DevSetting e, ShellSettings s) => e == SmartAiKey
        ? $"{e.Key} = {(s.SmartAiText.Length > 0 ? s.SmartAiText : "off")} ({WhenText(e.When)})"
        : $"{e.Key} = {e.Values[Math.Clamp(e.Get(s), 0, e.Values.Length - 1)].TrimEnd('*')} ({WhenText(e.When)})";
}
