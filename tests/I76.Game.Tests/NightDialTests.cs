using I76.Formats.Imaging;
using I76.Game.Shell;
using I76.Game.Sim;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's night under every lighting_calibration (NightDial), the Developer panel's Night keys ([port] night_moon ...)
/// and the needle sheets' ruled lines (the bar under the external speedometer and tachometer).
/// </summary>
public class NightDialTests
{
    private static string RepoFile(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "I76.sln"))) return Path.Combine(d.FullName, rel);
        throw new FileNotFoundException(rel);
    }

    /// <summary>shell.cfg's [port] lines as the file holds them (the engine's ConfigFile is not loadable in the tests).</summary>
    private static Dictionary<string, string> Cfg(ShellSettings s)
    {
        var keys = new Dictionary<string, string>();
        s.WriteNight((k, v) => keys[k] = v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return keys;
    }

    [Fact]
    public void TheNightKeysRoundTripThroughShellCfgAndOnlyWhenNotTheDefault()
    {
        Assert.Empty(Cfg(new ShellSettings()));
        var s = new ShellSettings { Night = new NightDial.Dial(0.5, 0.2, 3, 0.4, 0) };
        var text = Cfg(s);
        Assert.Equal(["night_chroma", "night_moon", "night_road", "night_sky", "night_warm"], text.Keys.Order());
        var back = new ShellSettings();
        back.ReadNight(k => text.GetValueOrDefault(k));
        Assert.Equal(s.Night, back.Night);
        // one key moved writes that key alone; an absent key, or one out of range, reads as the default or the slider's end
        Assert.Equal(["night_sky"], Cfg(new ShellSettings { Night = new NightDial.Dial() with { Sky = 0.3 } }).Keys);
        var e = new ShellSettings { Night = new NightDial.Dial(7, 7, 7, 0, 0) };
        e.ReadNight(k => k == "night_road" ? "500" : k == "night_warm" ? "-1" : k == "night_moon" ? "dark" : null);
        Assert.Equal(new NightDial.Dial(1, 10, 1, LightingCalibration.NightChroma, 0), e.Night);
    }

    [Fact]
    public void EverySliderValueReadsBackAndOneIsTheDefault()
    {
        foreach (var e in I76.Game.Ui.DevSettings.Night)
        {
            Assert.True(e.Slider);
            Assert.Equal(I76.Game.Ui.DevWhen.Live, e.When);
            var s = new ShellSettings();
            Assert.Equal("1", e.Values[e.Get(s)]);
            for (int i = 0; i < e.Values.Length; i++) { e.Set(s, i); Assert.Equal(i, e.Get(s)); }
        }
    }

    private static float? NoHook(string _) => null;

    /// <summary>A night ramp that lights a road under the ground, as T06's: the road's share below 1 through either curve.</summary>
    private static double Road(double moon, LightingCalibration.ToneCurve curve) => LightingCalibration.NightRoad(null, 0.1, 0.8, 0.02, moon * 0.3, curve);

    [Fact]
    public void WithTheCalibrationOffANightTakesTheNightLevels()
    {
        double filmicRoad = Road(1, LightingCalibration.ToneCurve.Filmic);
        Assert.InRange(filmicRoad, 1e-4, 0.5);
        var off = NightDial.Night(card: false, solvedMoon: 0.37, m => Road(m, LightingCalibration.ToneCurve.Filmic), new NightDial.Dial(), NoHook);
        // the whole moon through Filmic (the level the card's share is solved to show), the road at its share, the sky darkened, the ground's colour warm
        Assert.Equal(new NightDial.Levels(1, (float)LightingCalibration.NightGround, (float)filmicRoad, (float)LightingCalibration.NightSkyFilmic,
            (float)LightingCalibration.NightChroma, (float)LightingCalibration.NightWarm), off);
        Assert.NotEqual(NightDial.Levels.Neutral, off);
        // through the card's curve the solved share, as before
        var on = NightDial.Night(card: true, solvedMoon: 0.37, m => Road(m, LightingCalibration.ToneCurve.Card), new NightDial.Dial(), NoHook);
        Assert.Equal(0.37f, on.Moon);
        Assert.Equal((float)LightingCalibration.NightSky, on.Sky);
        // the globals the night terrain shader reads: declared in the project, and given the night's values
        var project = File.ReadAllText(RepoFile(Path.Combine("game", "project.godot")));
        var globals = NightDial.Globals(off);
        var declared = project.ReplaceLineEndings("\n").Split('\n');
        foreach (var (name, _) in globals) Assert.Contains(name + "={", declared);
        Assert.Equal([0f, (float)filmicRoad, 1f, 1f], globals.Select(g => g.Value));
        Assert.Equal([0f, 1f, 0f, 0f], NightDial.Globals(NightDial.Levels.Neutral).Select(g => g.Value));
    }

    [Fact]
    public void TheDialScalesTheSolvedLevelsAndAHookSetsOne()
    {
        var dial = new NightDial.Dial(0.5, 3, 0.5, 0.4, 0.2);
        var l = NightDial.Night(card: true, solvedMoon: 0.4, m => 0.02 / m, dial, NoHook);
        Assert.Equal(0.2f, l.Moon, 5);
        Assert.Equal(0.3f, l.Road, 5);   // the share at that moon (0.1), times 3
        Assert.Equal((float)(LightingCalibration.NightSky * 0.5), l.Sky, 6);
        Assert.Equal((0.4f, 0.2f), (l.Chroma, l.Warm));
        Assert.Equal(1f, NightDial.Night(true, 0.4, _ => 0.5, dial with { Road = 10 }, NoHook).Road);   // a share is at most 1
        var hooked = NightDial.Night(false, 0.4, _ => 0.5, dial, k => k == "I76_NIGHT_ROAD" ? 0.07f : k == "I76_NIGHT_MOON" ? 2f : null);
        Assert.Equal((2f, 0.07f), (hooked.Moon, hooked.Road));
    }

    [Fact]
    public void ANeedleSheetsRuledRowsAreKeyedAndItsArtIsNot()
    {
        // 40 x 4: row 0 a ruled line (light grey across) with a needle's dark tip crossing it, row 1 a needle's dark texels on a
        // keyed ground, row 2 light where an empty cell's fill runs beside frames (half the width)
        const int w = 40, h = 4;
        var px = new byte[w * h * 4];
        void Set(int x, int y, byte v, byte a) { int o = (y * w + x) * 4; px[o] = px[o + 1] = px[o + 2] = v; px[o + 3] = a; }
        for (int x = 0; x < w; x++)
        {
            Set(x, 0, x == 7 ? (byte)60 : (byte)240, 255);
            Set(x, 1, x == 2 ? (byte)90 : (byte)0, x == 2 ? (byte)255 : (byte)0);
            Set(x, 2, 250, x < w / 2 ? (byte)255 : (byte)0);
        }
        Assert.Equal(1, CockpitPanels.KeyRuledRows(px, w, h));
        Assert.All(Enumerable.Range(0, w).Where(x => x != 7), x => Assert.Equal(0, px[x * 4 + 3]));
        Assert.Equal(255, px[7 * 4 + 3]);                 // the needle's tip on the line
        Assert.Equal(255, px[(1 * w + 2) * 4 + 3]);
        Assert.Equal(255, px[(2 * w + 0) * 4 + 3]);
    }
}
