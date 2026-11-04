using System.Text.RegularExpressions;
using I76.Formats.Input;
using I76.Game.Shell;
using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The Developer panel's Settings (every key of docs/playing.md's "Settings without a row", each value reading back, the
/// defaults the file's) and its Signals (the traces' window, the wheel's forces from the effects on it).
/// </summary>
public class DevSettingsSignalsTests
{
    private static string RepoFile(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "I76.sln"))) return Path.Combine(d.FullName, rel);
        throw new FileNotFoundException(rel);
    }

    [Fact]
    public void EveryKeyOfTheTableIsOnThePanel()
    {
        var doc = File.ReadAllText(RepoFile(Path.Combine("docs", "playing.md")));
        int at = doc.IndexOf("### Settings without a row", StringComparison.Ordinal);
        var table = doc[at..doc.IndexOf("\n### ", at + 10, StringComparison.Ordinal)];
        var keys = Regex.Matches(table, @"^\| `([a-z_0-9]+)` \|", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("sky_drift", keys);
        Assert.Contains("hd_textures", keys);
        Assert.Contains("crossover", keys);
        Assert.Contains("lfe_road", keys);
        Assert.True(DevSettings.All.Single(e => e.Key == "lfe_explosions").Slider);
        Assert.Equal(keys.Order(), DevSettings.All.Select(e => e.Key).Concat(DevSettings.HandSetOnly).Order());
    }

    [Fact]
    public void EveryValueReadsBack()
    {
        foreach (var e in DevSettings.All)
        {
            var s = new ShellSettings();
            for (int i = 0; i < e.Values.Length; i++)
            {
                e.Set(s, i);
                Assert.Equal(i, e.Get(s));
            }
        }
    }

    [Fact]
    public void TheDefaultsAreTheFilesAndTheOriginalsAreUnmarked()
    {
        var s = new ShellSettings();
        string Shown(string key) { var e = DevSettings.All.Single(d => d.Key == key); return e.Values[e.Get(s)]; }
        Assert.Equal("look", Shown("draw_distance"));
        Assert.Equal("look", Shown("mirror_range"));
        Assert.Equal("steady*", Shown("sky_drift"));
        Assert.Equal("original", Shown("death_orbit"));
        Assert.Equal("100", Shown("effects_size"));
        Assert.Equal("60", Shown("vegetation_density"));
        Assert.Equal("true*", Shown("hd_textures"));
        Assert.Equal("true*", Shown("hd_menu_art"));
        Assert.Equal("on*", Shown("lighting_calibration"));
        Assert.Equal("90", Shown("crossover"));
        foreach (var key in TactileSources.Keys) Assert.Equal("100", Shown(key));
        Assert.Equal("false", Shown("developer"));
        Assert.Equal("0", Shown("melee_ai"));
        foreach (var e in DevSettings.All)
            Assert.Single(e.Values, v => !v.EndsWith(ShellSettings.PortMark, StringComparison.Ordinal) && v != "look");
    }

    [Theory]
    [InlineData(110, 3)]
    [InlineData(25, 0)]
    [InlineData(140, 5)]
    public void AHandSetPercentShowsAsTheNearestStep(int percent, int step) => Assert.Equal(step, DevSettings.NearestStep(percent));

    [Fact]
    public void ATraceKeepsItsNewestAndShowsItsWindow()
    {
        var t = new SignalTrace("x", 0, 1, capacity: 4);
        for (int i = 0; i < 6; i++) t.Add(i, i / 10f);
        Assert.Equal(4, t.Count);
        Assert.Equal(0.5f, t.Last);
        Assert.Equal([3.0, 4.0, 5.0], t.Since(3).Select(s => s.T));
        Assert.Equal(0.5f, t.Peak(0));
    }

    [Fact]
    public void TheWheelsForceIsTheSumOfWhatPlaysAndTheSpringItsCoefficient()
    {
        DevSignals.Reset();
        var f = new WheelFrame();
        f.Slots["kick"] = new(new WheelEffect { Kind = WheelKind.Constant, Magnitude = 5000, DurationUs = 200_000 }, 1);
        f.Slots["spring"] = new(WheelMap.Spring(30), 1);
        DevSignals.Wheel(f, 10.0);
        DevSignals.Sample(10.0);
        Assert.Equal(0.5f, DevSignals.WheelForce.Last, 3);
        Assert.Equal(0.3f, DevSignals.WheelSpring.Last, 3);   // 1500 + 50 a mph
        Assert.Equal(2, DevSignals.WheelEffects.Last);
        // the kick is over 0.2 s after its slot last changed; the spring plays on
        DevSignals.Wheel(f, 10.5);
        DevSignals.Sample(10.5);
        Assert.Equal(0f, DevSignals.WheelForce.Last);
        Assert.Equal(1, DevSignals.WheelEffects.Last);
        // a new generation starts the effect again
        f.Slots["kick"] = f.Slots["kick"] with { Generation = 2 };
        DevSignals.Wheel(f, 11.0);
        DevSignals.Sample(11.0);
        Assert.Equal(0.5f, DevSignals.WheelForce.Last, 3);
        DevSignals.Reset();
    }

    [Fact]
    public void ThePadsMotorsAndTheTelemetryRateAreSampled()
    {
        DevSignals.Reset();
        DevSignals.Pad(0.25f, 0.75f);
        for (int i = 0; i < 30; i++) DevSignals.Telemetry(i / 60.0, 0.1f, -0.2f, 1, 2, 3);
        DevSignals.Sample(0.5);
        Assert.Equal(0.75f, DevSignals.PadLow.Last);
        Assert.Equal(0.25f, DevSignals.PadHigh.Last);
        Assert.Equal(30, DevSignals.TelRate.Last);     // packets in the last second
        Assert.Equal(-0.2f, DevSignals.TelRoll.Last);
        Assert.Equal(0f, DevSignals.Lfe.Last);         // no bass shaker output on this build
        DevSignals.Reset();
    }
}
