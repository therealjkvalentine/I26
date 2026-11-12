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

    [Fact]
    public void AChosenFieldOfViewIsKeptUnderFovWindow()
    {
        int choice = System.Array.IndexOf(ViewFov.Across, 95);
        var f = new ShellSettings { FieldOfView = choice }.Write(null);
        Assert.Equal(95L, f.GetValue("port", "fov_window").Obj);
        Assert.Equal(choice, ShellSettings.Read(f).FieldOfView);
    }
}
