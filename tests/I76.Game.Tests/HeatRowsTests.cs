using System.Numerics;
using I76.Formats.Mission;
using I76.Game.Shell;
using I76.Game.Sim;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Smoke Lighting ([port] smoke_lighting) and Heat Shimmer ([port] heat_shimmer): off unless the file says otherwise, written only when
/// on, on Advanced after the rows before them; Heat Shimmer's "hot day" (HotDay) from the mission's light and palette; the smoke's light
/// (SmokeLight.Colours) warm low in the sky, dim and blue at night, as bright as today's at noon. Without the engine.
/// </summary>
public class HeatRowsTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void BothAreOffInAFreshBlockAndInAFileWithoutTheirKeys()
    {
        var s = new ShellSettings();
        Assert.False(s.SmokeLighting);
        Assert.Equal(HeatShimmerRow.Off, s.HeatShimmer);
        var file = s.Write(null);
        Assert.False(file.HasSectionKey("port", "smoke_lighting"));
        Assert.False(file.HasSectionKey("port", "heat_shimmer"));
        var old = ShellSettings.Read(ShellFileCompatTests.Parse(ShellFileCompatTests.ForkMainFile));
        Assert.False(old.SmokeLighting);
        Assert.Equal(HeatShimmerRow.Off, old.HeatShimmer);
    }

    [Theory]
    [InlineData(HeatShimmerRow.Fires, "fires")]
    [InlineData(HeatShimmerRow.Road, "road")]
    public void TheChoicesSurviveASaveAndALoad(int shimmer, string word)
    {
        var s = new ShellSettings { SmokeLighting = true, HeatShimmer = shimmer };
        var file = s.Write(null);
        Assert.Equal(word, file.GetValue("port", "heat_shimmer").ToString());
        var back = ShellSettings.Read(file);
        Assert.True(back.SmokeLighting);
        Assert.Equal(shimmer, back.HeatShimmer);
    }

    [Fact]
    public void AnUnknownWordIsOff()
    {
        Assert.Equal(HeatShimmerRow.Off, HeatShimmerRow.FromFile(null));
        Assert.Equal(HeatShimmerRow.Off, HeatShimmerRow.FromFile("lots"));
        Assert.Equal(HeatShimmerRow.Fires, HeatShimmerRow.FromFile(" Fires "));
        Assert.Equal(HeatShimmerRow.Road, HeatShimmerRow.FromFile("road"));
    }

    [Fact]
    public void TheRowsStepThroughTheirValues()
    {
        var s = new ShellSettings();
        Assert.Equal(["Off", "Fires" + ShellSettings.PortMark, "Fires + Road" + ShellSettings.PortMark], HeatShimmerRow.Names);
        HeatShimmerRow.Step(s); Assert.Equal(HeatShimmerRow.Fires, s.HeatShimmer);
        HeatShimmerRow.Step(s); Assert.Equal(HeatShimmerRow.Road, s.HeatShimmer);
        HeatShimmerRow.Step(s); Assert.Equal(HeatShimmerRow.Off, s.HeatShimmer);
        SmokeLightingRow.Step(s); Assert.True(s.SmokeLighting);
        Assert.Equal(1, SmokeLightingRow.Index(s));
        SmokeLightingRow.Put(s, 0); Assert.False(s.SmokeLighting);
    }

    [Fact]
    public void TheyAreLastOnAdvancedAndResetPutsThemOff()
    {
        var adv = GraphicsPages.Rows[GraphicsPages.Advanced];
        Assert.Equal([GraphicsRow.SmokeLighting, GraphicsRow.HeatShimmer], adv[^2..]);
        var s = new ShellSettings { SmokeLighting = true, HeatShimmer = HeatShimmerRow.Road };
        GraphicsPages.Reset(s, GraphicsPages.Advanced);
        Assert.False(s.SmokeLighting);
        Assert.Equal(HeatShimmerRow.Off, s.HeatShimmer);
        var t = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.HeatShimmer, new ShellSettings { HeatShimmer = HeatShimmerRow.Fires }, t);
        GraphicsPages.Copy(GraphicsRow.SmokeLighting, new ShellSettings { SmokeLighting = true }, t);
        Assert.Equal(HeatShimmerRow.Fires, t.HeatShimmer);
        Assert.True(t.SmokeLighting);
        foreach (bool look1997 in (bool[])[false, true])
        {
            Assert.NotEmpty(GraphicsHelp.Text(GraphicsRow.SmokeLighting, look1997));
            Assert.NotEmpty(GraphicsHelp.Text(GraphicsRow.HeatShimmer, look1997));
        }
    }

    // a desert's palette: every entry sand (warmth about 0.5, as the missions' .act files measure); a grey one
    private static byte[] Palette(byte r, byte g, byte b) => [.. Enumerable.Range(0, 256).SelectMany(_ => new[] { r, g, b })];

    /// <summary>The presets by hour: 7-9 o'clock 35 degrees, 10-12 80, 13-15 45 (hot), 16-18 20, dawn and dusk 15, the night's two (not).</summary>
    [Theory]
    [InlineData(8, true)]
    [InlineData(12, true)]
    [InlineData(14, true)]
    [InlineData(17, false)]
    [InlineData(5, false)]
    [InlineData(20, false)]
    [InlineData(23, false)]
    [InlineData(2, false)]
    public void AHotDayIsADaySunAtLeastThirtyDegreesUp(int hour, bool hot)
    {
        var light = Lighting.ForHour(hour);
        Assert.Equal(hot, HotDay.Is(light, new I76.Formats.Imaging.Palette(Palette(170, 130, 90))));
        Assert.Equal(hot, SmokeLight.Elevation(light) >= HotDay.MinElevation && light.IsDay);
    }

    [Fact]
    public void AGreyOrColdPaletteHasNoHotDay()
    {
        var noon = Lighting.ForHour(12);
        Assert.True(HotDay.Warmth(Palette(170, 130, 90)) > 0.4f);
        Assert.Equal(0f, HotDay.Warmth(Palette(128, 128, 128)), 3);
        Assert.False(HotDay.Is(noon, new I76.Formats.Imaging.Palette(Palette(128, 128, 128))));
        Assert.False(HotDay.Is(noon, new I76.Formats.Imaging.Palette(Palette(120, 130, 160))));
        Assert.True(HotDay.Is(noon, null));   // no palette read: the sun decides
        Assert.False(HotDay.Is(noon, null, "tp101sw6.map"));   // snow
        Assert.True(HotDay.Is(noon, null, "tp041db6.map"));
    }

    /// <summary>The stock missions that are hot days at their own hour (where Fires + Road shows the road's ripple and mirage), listed.</summary>
    [I76.Formats.Tests.InstallFact]
    public void TheStockHotDays()
    {
        var install = I76.Formats.Tests.TestInstall.Install;
        var vfs = I76.Formats.Tests.TestInstall.Vfs;
        var hot = new List<string>();
        foreach (var path in install.ListMissions(".msn").Order())
        {
            var m = new I76.Formats.Mission.MissionFile(path);
            if (WorldSettings.Read(m) is not { } w) continue;
            var light = Lighting.ForHour(w.Hour);
            var pn = I76.Formats.Assembly.MissionWorld.ResolvePalette(vfs, m);
            var pal = pn is not null && vfs.TryRead(pn) is { } pd ? new I76.Formats.Imaging.Palette(pd) : null;
            bool h = HotDay.Is(light, pal, w.Ground);
            output.WriteLine($"{System.IO.Path.GetFileNameWithoutExtension(path),-8} hour {w.Hour,2} sun {SmokeLight.Elevation(light),3:0} deg {(light.IsDay ? "day  " : "night")} {w.Ground,-13} warmth {(pal is null ? "none " : HotDay.Warmth(pal.Rgb).ToString("0.000"))} {(h ? "HOT" : "")}");
            if (h) hot.Add(System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant());
        }
        output.WriteLine("hot days: " + string.Join(" ", hot));
        // T05's and T01's afternoon on the plains, not M14's snow at nine, nor T02's dawn or M04's night
        Assert.Contains("t05", hot); Assert.Contains("t01", hot);
        Assert.DoesNotContain("m14", hot); Assert.DoesNotContain("t02", hot); Assert.DoesNotContain("m04", hot);
    }

    [Fact]
    public void TheDeveloperPanelsNumbersChangeTheHotDayLiveAndResetToTheShipped()
    {
        try
        {
            HeatShimmer.Know(Lighting.ForHour(9), new I76.Formats.Imaging.Palette(Palette(170, 130, 90)));
            Assert.True(HeatShimmer.HotDay);   // 35 degrees, warm
            HotDay.MinElevation = 40; HeatShimmer.Reevaluate();
            Assert.False(HeatShimmer.HotDay);
            HeatTune.ForceHot = true; HeatShimmer.Reevaluate();
            Assert.True(HeatShimmer.HotDay);
            HeatTune.RoadNear = 120; HeatTune.Fire = 3;
            Assert.Contains("road from 120 m", HeatTune.Line());
            Assert.Contains("forced hot", HeatTune.Line());
            HeatTune.Reset();
            Assert.Equal(HeatShimmer.RoadNear, HeatTune.RoadNear);
            Assert.Equal(1f, HeatTune.Fire);
            Assert.Equal(HotDay.DefaultMinElevation, HotDay.MinElevation);
            Assert.Null(HeatTune.ForceHot);
            Assert.True(HeatShimmer.HotDay);
        }
        finally { HeatTune.Reset(); }
    }

    [Fact]
    public void TheShimmerIsTwiceTheFirstVersions()
    {
        // the road's band from 75 m (the first version 150, a few pixels at the road's far end), the fires' bend out to 260 m (180)
        Assert.Equal(75f, HeatShimmer.RoadNear);
        Assert.Equal(260f, HeatShimmer.FireFar);
        Assert.True(HeatShimmer.BandDegrees >= 2.5f);
        Assert.Equal(1f, HeatShimmer.Scale);   // I76_HEAT_SCALE unset: the shipped strength
    }

    [Fact]
    public void TheSunOnSmokeIsWhiteAtNoonAndWarmLowInTheSky()
    {
        var sky = new Vector3(0.35f, 0.5f, 0.75f);
        var noon = Lighting.ForHour(12);
        var (sunNoon, ambNoon) = SmokeLight.Colours(true, noon.Intensity, SmokeLight.Elevation(noon), sky);
        Assert.Equal(1f, SmokeLight.Luminance(sunNoon), 2);   // the level smoke has today (scene_light 1)
        Assert.True(sunNoon.X / sunNoon.Z < 1.05f);
        var dusk = Lighting.ForHour(20);
        var (sunDusk, ambDusk) = SmokeLight.Colours(true, dusk.Intensity, SmokeLight.Elevation(dusk), sky);
        Assert.True(sunDusk.X / sunDusk.Z > 2.5f, $"dusk sun {sunDusk}");
        Assert.True(SmokeLight.Luminance(ambDusk) < 0.6f * SmokeLight.Luminance(ambNoon), "the far side darker at dusk");
        var night = Lighting.ForHour(23);
        var (moon, ambNight) = SmokeLight.Colours(false, night.Intensity, SmokeLight.Elevation(night), sky);
        Assert.True(moon.Z > moon.X, "the moon's blue");
        float off = SmokeLight.SceneLight(night.Intensity);
        Assert.True(SmokeLight.Luminance(moon) < off && SmokeLight.Luminance(ambNight) < off, "night smoke dimmer than the one level it has off");
    }
}
