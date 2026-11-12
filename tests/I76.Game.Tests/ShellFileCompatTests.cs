using System.Globalization;
using System.Text;
using I76.Formats.Imaging;
using I76.Game.Shell;
using I76.Game.Sim;
using I76.Sim.Views;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// shell.cfg between this build and the fork's builds from before the upstream merge, which share the user folder: a file the
/// fork's main wrote keeps every setting here, and a file this build writes reads in the older build's meaning for the keys
/// both understand (the [shell] version, Extra Enemies, the Field of View at Monitor).
/// </summary>
public class ShellFileCompatTests
{
    /// <summary>A shell.cfg's text as the engine's ConfigFile writes it (ints, floats, true / false, quoted strings, PackedByteArray), engine-free.</summary>
    internal static SettingsFile Parse(string text)
    {
        var f = new SettingsFile();
        string section = "";
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0 || l[0] == ';') continue;
            if (l[0] == '[') { section = l.Trim('[', ']'); continue; }
            int eq = l.IndexOf('=');
            string key = l[..eq], v = l[(eq + 1)..];
            f.SetValue(section, key, v switch
            {
                "true" => true,
                "false" => false,
                _ when v.StartsWith("PackedByteArray(") => v["PackedByteArray(".Length..^1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(b => byte.Parse(b.Trim())).ToArray(),
                _ when v.StartsWith('"') => Unquote(v),
                _ when v.Contains('.') || v.Contains('e') => double.Parse(v, CultureInfo.InvariantCulture),
                _ => long.Parse(v, CultureInfo.InvariantCulture),
            });
        }
        return f;
    }

    private static string Unquote(string v)
    {
        var sb = new StringBuilder();
        for (int i = 1; i < v.Length - 1; i++) sb.Append(v[i] == '\\' ? v[++i] : v[i]);
        return sb.ToString();
    }

    /// <summary>
    /// A shell.cfg as the fork's main before the merge saved it (its Save: [shell] version 14 and no fork_version, fov in degrees
    /// across the window, coop_scale left out only at the cap, cabins_behind_glass), every setting below away from its default.
    /// </summary>
    internal const string ForkMainFile = """
        [shell]

        version=14
        graphics=PackedByteArray(5, 1, 0, 1, 1, 1, 1, 2, 1, 1, 1, 1, 3, 2, 1, 1)
        audio=PackedByteArray(8, 8, 8, 8)
        play=0
        difficulty=1

        [port]

        vegetation=1
        draw_distance=1
        checkpoints=false
        force_feedback=true
        sim_rate=20
        mouse_camera=1
        mouse_sensitivity=1.0
        max_fps=0
        vsync=true
        hd_menu_art=true
        world_textures="2x"
        car_textures="4x"
        texture_packs="4x"
        coop_scale=0
        cabins_behind_glass=false
        fov=100
        side_mirrors="left"
        mirror_rate="full"
        mirror_round=8
        mirror_fov=70
        side_mirror_share=60
        side_mirror_angle=30
        side_mirror_drop=200
        side_mirror_overlap=12
        side_mirror_render="shared"
        """;

    [Fact]
    public void AFileTheForksMainWroteKeepsEverySetting()
    {
        var s = ShellSettings.Read(Parse(ForkMainFile));
        // fov in the fork's meaning: 100 degrees across the window, not upstream's 100 across 4:3
        Assert.Equal(System.Array.IndexOf(ViewFov.Across, 100), s.FieldOfView);
        // version 14: Terrain Detail Low is the player's (not raised to High) and draw_distance Far a choice (not 0.11.0's every-save Far)
        Assert.Equal(0, s.Graphics[0x2]);
        Assert.Equal(DrawDistance.Far, s.DrawDistanceChoice);
        Assert.Equal(0, s.CoopScale);
        Assert.False(s.CabinsBehindGlass);
        Assert.Equal(SideMirrorMode.Left, s.SideMirrors);
        Assert.Equal(MirrorRate.Full, s.MirrorRate);
        Assert.Equal(8, s.MirrorRound);
        Assert.Equal(70, s.MirrorFov);
        Assert.Equal(60, s.SideMirrorShare);
        Assert.Equal(30, s.SideMirrorAngle);
        Assert.Equal(200, s.SideMirrorDrop);
        Assert.Equal(12, s.SideMirrorOverlap);
        Assert.Equal(SideMirrorRender.Shared, s.SideMirrorRender);
        Assert.Equal(2, s.WorldTextures);
        Assert.Equal(4, s.CarTextures);
        // and saved again, every one of them is still there for either build
        var again = ShellSettings.Read(s.Write(null));
        Assert.Equal(s.FieldOfView, again.FieldOfView);
        Assert.Equal(0, again.Graphics[0x2]);
        Assert.Equal(DrawDistance.Far, again.DrawDistanceChoice);
        Assert.Equal(0, again.CoopScale);
        Assert.Equal((2, 4), (again.WorldTextures, again.CarTextures));
        Assert.Equal((SideMirrorMode.Left, MirrorRate.Full, 8, 70, SideMirrorRender.Shared), (again.SideMirrors, again.MirrorRate, again.MirrorRound, again.MirrorFov, again.SideMirrorRender));
    }

    [Fact]
    public void AnUntouchedForkFileWithoutCoopScaleReadsAsOff()
    {
        // the fork's main left the key out at its default, the cap: upstream's default (Off) applies now, as decided
        var s = ShellSettings.Read(Parse(ForkMainFile.Replace("coop_scale=0\n", "")));
        Assert.Equal(0, s.CoopScale);
    }

    /// <summary>
    /// The keys a fork build from before the merge reads, as it reads them (its Load: Terrain Detail raised and draw_distance
    /// dropped below version 14, fov as degrees across the window with absent Monitor, an absent coop_scale the cap).
    /// </summary>
    [Fact]
    public void AFileThisBuildWritesReadsInTheOlderForkBuildsMeaning()
    {
        var s = new ShellSettings { FieldOfView = ViewFov.Monitor, CoopScale = 0, DrawDistanceChoice = DrawDistance.Far };
        s.Graphics[0x2] = 0;
        var f = s.Write(null);
        Assert.Equal(14L, f.GetValue("shell", "version").Obj);
        Assert.Equal(14L, f.GetValue("port", "fork_version").Obj);
        Assert.False(f.HasSectionKey("port", "fov"));
        Assert.False(f.HasSectionKey("port", "fov_window"));
        Assert.Equal(0L, f.GetValue("port", "coop_scale").Obj);
        int version = (int)f.GetValue("shell", "version");
        Assert.Equal(ViewFov.Monitor, ViewFov.Parse(f.HasSectionKey("port", "fov") ? (int)f.GetValue("port", "fov") : 0));
        var g = (byte[])ShellSettings.DefaultGraphics.Clone();
        ShellSettings.ReadGraphics(g, (byte[])f.GetValue("shell", "graphics"), version);
        Assert.Equal(0, g[0x2]);
        Assert.Equal(DrawDistance.Far, DrawDistance.FromFile((int)f.GetValue("port", "draw_distance"), version));
        // upstream's build reads the same file: fov absent is its Original (Monitor's height), coop_scale 0 Off
        Assert.Equal(ViewFov.Monitor, ViewFov.ParseFourByThree(null));
        // and this build reads its own file back
        var back = ShellSettings.Read(f);
        Assert.Equal((ViewFov.Monitor, 0, (byte)0, (int?)DrawDistance.Far), (back.FieldOfView, back.CoopScale, back.Graphics[0x2], back.DrawDistanceChoice));
    }

    /// <summary>The fork's main kept the turret rule in build-rules.cfg: a 1997 there becomes Play Options' Turrets once, then shell.cfg's key rules.</summary>
    [Fact]
    public void TheTurretRuleOfBuildRulesIsTakenOnceIntoPlayOptions()
    {
        const string rules = "; my notes\n[rules]\nturret_slew = 1997\nai_tactics=1998\n";
        var s = ShellSettings.Read(Parse(ForkMainFile), rules);
        Assert.True(s.Turrets1997);
        var f = s.Write(null);
        Assert.Equal(1997L, f.GetValue("port", "turret_slew").Obj);
        Assert.True(ShellSettings.Read(f, rules).Turrets1997);
        // set back to 1998 in Play Options, it stays 1998 though build-rules.cfg still says 1997
        s.Turrets1997 = false;
        var again = s.Write(null);
        Assert.Equal(1998L, again.GetValue("port", "turret_slew").Obj);
        Assert.False(ShellSettings.Read(again, rules).Turrets1997);
        // no line, a 1998 line or no file: the 1998 build's
        Assert.False(ShellSettings.Read(Parse(ForkMainFile), "[rules]\nai_tactics=1997\n").Turrets1997);
        Assert.False(ShellSettings.Read(Parse(ForkMainFile), "turret_slew=1998").Turrets1997);
        Assert.False(ShellSettings.Read(Parse(ForkMainFile)).Turrets1997);
        Assert.Null(BuildRules.OldTurrets1997("[rules]\nai_tactics=1997\n"));
    }

    /// <summary>
    /// A real player's shell.cfg as the fork's main saved it (settings only; [shell] version 14, no fork_version), every key in it
    /// read back with the file's value, then written back unchanged.
    /// </summary>
    internal const string PlayerFile = """
        [shell]

        version=14
        graphics=PackedByteArray(5, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 1, 3, 2, 1, 1)
        audio=PackedByteArray(8, 8, 8, 8)
        play=0
        difficulty=1

        [port]

        vegetation=1
        draw_distance=1
        checkpoints=false
        force_feedback=false
        lfe_level=100
        tactile_eq="13:4, 18:4, 20:3.7, 22:1.6, 25:0, 28:-1.3, 31:-2.5, 35:-3.5, 40:-2.5, 45:-1.3, 50:0, 80:0, 90:1.6, 100:3.7"
        sim_rate=24
        crash_blur="off"
        mouse_camera=2
        mouse_sensitivity=1.0
        hood_gloss=10
        max_fps=0
        vsync=true
        developer=true
        dev_fps=true
        hd_menu_art=true
        world_textures="4x"
        car_textures="8x"
        handicaps="auto"
        effects_size=80
        effects_brightness=75
        effects_opacity=70
        lighting_calibration="off"
        mirror_range="full"
        brake_lamps="original"
        mg_tracers="original"
        """;

    [Fact]
    public void EveryKeyOfAPlayersFileComesBackWithItsValue()
    {
        var file = Parse(PlayerFile);
        var s = ShellSettings.Read(file);
        Assert.Equal(1, s.Difficulty);
        Assert.Equal(0, s.PlayOptions);
        Assert.Equal(new byte[] { 5, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 1, 3, 2, 1, 1 }, s.Graphics);
        Assert.Equal(new byte[] { 8, 8, 8, 8 }, s.Audio);
        Assert.Equal(1, s.Vegetation);
        Assert.Equal(DrawDistance.Far, s.DrawDistanceChoice);
        Assert.False(s.Checkpoints);
        Assert.False(s.ForceFeedback);
        Assert.Equal(100, s.LfeLevel);
        Assert.Equal("13:4, 18:4, 20:3.7, 22:1.6, 25:0, 28:-1.3, 31:-2.5, 35:-3.5, 40:-2.5, 45:-1.3, 50:0, 80:0, 90:1.6, 100:3.7", s.TactileEq);
        Assert.Equal(24, s.SimRate);
        Assert.Equal(CrashBlurMode.Parse("off"), s.CrashBlur);
        Assert.NotEqual(CrashBlurMode.On, s.CrashBlur);
        Assert.Equal(2, s.MouseCamera);
        Assert.Equal(1f, s.MouseSensitivity);
        Assert.Equal(I76.Sim.Views.PaintFinish.Parse("10"), s.HoodGloss);
        Assert.Equal(FrameLimit.Parse(0), s.MaxFps);
        Assert.True(s.VSync);
        Assert.True(s.DeveloperTools);
        Assert.True(s.DevFps);
        Assert.True(s.HdMenuArt);
        Assert.Equal((4, 8), (s.WorldTextures, s.CarTextures));
        Assert.Equal(I76.Sim.Missions.HandicapMode.Auto, s.Handicaps);
        Assert.Equal((80, 75, 70), (s.EffectsSize, s.EffectsBrightness, s.EffectsOpacity));
        Assert.False(s.LightingCalibration);
        Assert.Equal(1, s.MirrorRangeChoice);
        Assert.Equal(0, s.BrakeLampChoice);
        Assert.Equal(0, s.MgTracerChoice);
        // saved again, every key the file had is there with the same value
        var back = s.Write(file);
        foreach (var (section, key, value) in file.Entries)
        {
            if (section == "shell" && key == "version") continue;   // WrittenVersion, 14 as well
            Assert.True(back.HasSectionKey(section, key), $"[{section}] {key} not written");
            var was = new SettingsValue(value);
            var now = back.GetValue(section, key);
            if (value is byte[] bytes) Assert.Equal(bytes, (byte[])now);
            else Assert.True(was.ToString() == now.ToString(), $"[{section}] {key}: {was} written as {now}");
        }
        Assert.Equal(14L, back.GetValue("shell", "version").Obj);
    }

    [Fact]
    public void AChosenFieldOfViewIsKeptUnderFovWindow()
    {
        int choice = System.Array.IndexOf(ViewFov.Across, 95);
        var f = new ShellSettings { FieldOfView = choice }.Write(null);
        Assert.Equal(95L, f.GetValue("port", "fov_window").Obj);
        Assert.Equal(choice, ShellSettings.Read(f).FieldOfView);
    }
}
