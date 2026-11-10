using System.Text.RegularExpressions;
using I76.Formats.Mission;
using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's dash lamps (DashLight): the speedometer's and tachometer's needles are lit faces in the original, near black
/// under a night preset, while the dial faces are painted art shown as painted. At night the needles show their own colour, warmed,
/// as light as by day at [port] dash_light 100 (the default); none by day, where the sun lights them; the 1997 look untouched.
/// </summary>
public class DashLightTests
{
    private static string Repo(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, .. path])).ReplaceLineEndings("\n");
    }

    private static Dictionary<string, string> Cfg(ShellSettings s)
    {
        var keys = new Dictionary<string, string>();
        s.WriteDashLight((k, v) => keys[k] = v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return keys;
    }

    [Fact]
    public void TheKeyRoundTripsThroughShellCfgAndOnlyWhenNotTheDefault()
    {
        Assert.Equal(100, DashLight.Default);
        Assert.Equal(100, new ShellSettings().DashLight);
        Assert.Empty(Cfg(new ShellSettings()));
        var text = Cfg(new ShellSettings { DashLight = 40 });
        Assert.Equal(new Dictionary<string, string> { ["dash_light"] = "40" }, text);
        var back = new ShellSettings();
        back.ReadDashLight(k => text.GetValueOrDefault(k));
        Assert.Equal(40, back.DashLight);
        // absent or not a number: the default; out of range: the end
        Assert.Equal(100, DashLight.Parse(null));
        Assert.Equal(100, DashLight.Parse("bright"));
        Assert.Equal(200, DashLight.Parse("999"));
        Assert.Equal(0, DashLight.Parse("-3"));
        int was = DashLight.Percent;
        try
        {
            OriginalBehavior.Apply(new ShellSettings { DashLight = 0 });
            Assert.Equal(0, DashLight.Percent);
            OriginalBehavior.Apply(new ShellSettings());
            Assert.Equal(100, DashLight.Percent);
        }
        finally { DashLight.Percent = was; }
    }

    [Fact]
    public void TheLampsAreFullAtNightFadeAtDuskAndAreOffInTheSun()
    {
        foreach (var p in Lighting.Presets)
        {
            float level = DashLight.Level(LampStrength.Daylight(p.IsDay, p.Intensity), 100);
            if (!p.IsDay) Assert.Equal(DashLight.DayMatch, level);
            else Assert.Equal(DashLight.DayMatch * (1f - Math.Clamp(p.Intensity, 0f, 1f)), level, 5);
        }
        // 100 at night: the share of the needle's colour that shows it as light as by day (a fifth: the whole colour is a pink no daylight gives)
        Assert.InRange(DashLight.DayMatch, 0.1f, 0.4f);
        Assert.Equal(0f, DashLight.Level(1f, 100));
        Assert.Equal(0.375f * DashLight.DayMatch, DashLight.Level(0.625f, 100), 5);   // dusk
        Assert.Equal(1.5f * DashLight.DayMatch, DashLight.Level(0f, 150), 5);
        Assert.Equal(0f, DashLight.Level(0f, 0));
    }

    [Fact]
    public void TheLevelReachesTheNeedlesMaterialInTheModernLookOnly()
    {
        var shader = Repo("game", "Shaders", "object.gdshaderinc");
        // the global, declared in the project, and the material flag
        Assert.Matches(@"(?m)^global uniform float " + DashLight.Global + ";", shader);
        Assert.Matches(@"(?m)^uniform bool " + DashLight.Uniform + " = false;", shader);
        Assert.Contains("\n" + DashLight.Global + "={", Repo("game", "project.godot"));
        // the emission: the face's colour times the lamp's level, in the modern look's half of the fragment (after #else, before #endif)
        int lamp = shader.IndexOf("if (" + DashLight.Uniform + ") EMISSION += c.rgb * DASH_LAMP * " + DashLight.Global, StringComparison.Ordinal);
        Assert.True(lamp > 0);
        int frag = shader.IndexOf("void fragment()", StringComparison.Ordinal);
        int modern = shader.IndexOf("#else", shader.IndexOf("#ifdef FAITHFUL\n\tALBEDO = i76_out", frag, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.InRange(lamp, modern, shader.IndexOf("#endif", modern, StringComparison.Ordinal));
        // the needles' material takes the flag, the lamp is warm white at unit luminance, and the level is set with the daylight and the setting
        Assert.Contains("own.SetShaderParameter(I76.Game.Sim.DashLight.Uniform, true);", Repo("game", "Scripts", "Boot", "Main.cs"));
        var c = Regex.Match(shader, @"const vec3 DASH_LAMP = vec3\(([\d.]+), ([\d.]+), ([\d.]+)\);").Groups;
        float r = float.Parse(c[1].Value, System.Globalization.CultureInfo.InvariantCulture), g = float.Parse(c[2].Value, System.Globalization.CultureInfo.InvariantCulture), b = float.Parse(c[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(0.2126f * r + 0.7152f * g + 0.0722f * b, 0.99f, 1.01f);
        Assert.True(r > g && g > b);
        Assert.Contains("DashLight.Refresh();", Repo("game", "Scripts", "Sim", "LampStrength.cs"));
        Assert.Contains(I76.Game.Ui.DevSettings.All, e => e.Key == "dash_light" && e.When == I76.Game.Ui.DevWhen.Live);
    }
}
