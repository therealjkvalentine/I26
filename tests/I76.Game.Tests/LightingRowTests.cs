using Godot;
using I76.Game.Boot;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Lighting ([port] lighting_calibration, Graphic Detail &gt; Effects): its four words through a save and a load, the row's steps,
/// an earlier build's reading of the new word; the sky's light at its energy (Main.SkyContribution); the capture hooks' settings
/// (I76_SETTINGS) and sun camera. Without the engine.
/// </summary>
public class LightingRowTests
{
    [Theory]
    [InlineData(LightingRow.Matched, null)]
    [InlineData(LightingRow.Sunlit, "sunlit")]
    [InlineData(LightingRow.Filmic, "filmic")]
    [InlineData(LightingRow.Off, "off")]
    public void EachChoiceSurvivesASaveAndALoad(int choice, string? word)
    {
        var s = new ShellSettings();
        LightingRow.Put(s, choice);
        var file = s.Write(null);
        Assert.Equal(word is not null, file.HasSectionKey("port", "lighting_calibration"));
        if (word is not null) Assert.Equal(word, file.GetValue("port", "lighting_calibration").ToString());
        var back = ShellSettings.Read(file);
        Assert.Equal(choice, LightingRow.Index(back));
        Assert.Equal(word ?? "on", LightingRow.Word(choice));
    }

    [Fact]
    public void TheDefaultIs1997AndTheRowStepsThroughEveryChoice()
    {
        var s = new ShellSettings();
        Assert.Equal(LightingRow.Matched, LightingRow.Index(s));
        Assert.Equal(["1997*", "1997 Sunlit*", "Filmic*", "Off"], LightingRow.Names);
        LightingRow.Step(s); Assert.True(s.LightingSunlit && s.LightingCalibration && !s.LightingFilmic);
        LightingRow.Step(s); Assert.True(s.LightingFilmic && !s.LightingSunlit);
        LightingRow.Step(s); Assert.False(s.LightingCalibration);
        LightingRow.Step(s); Assert.Equal(LightingRow.Matched, LightingRow.Index(s));
    }

    [Fact]
    public void TheOwnersFileWithItsWordsReadsAsBefore()
    {
        // a file of today (on: no key; filmic; off) and an earlier build's reading of the new word: on (not off, not filmic)
        Assert.True(ShellSettings.LightingCalibrationOn("sunlit", null));
        Assert.False(ShellSettings.LightingFilmicOn("sunlit"));
        Assert.True(ShellSettings.LightingSunlitOn(" Sunlit "));
        Assert.False(new ShellSettings().LightingSunlit);
        var old = ShellSettings.Read(ShellFileCompatTests.Parse(ShellFileCompatTests.ForkMainFile));
        Assert.False(old.LightingSunlit);
    }

    [Fact]
    public void GraphicDetailsEffectsPageLeadsWithTheRowAndCopiesItsKeys()
    {
        Assert.Equal(GraphicsRow.Lighting, GraphicsPages.Rows[GraphicsPages.Effects][0]);
        var from = new ShellSettings();
        LightingRow.Put(from, LightingRow.Sunlit);
        var to = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.Lighting, from, to);
        Assert.Equal(LightingRow.Sunlit, LightingRow.Index(to));
        GraphicsPages.Reset(to, GraphicsRow.Lighting);
        Assert.Equal(LightingRow.Matched, LightingRow.Index(to));
        Assert.Contains("1997 Sunlit", GraphicsHelp.Text(GraphicsRow.Lighting, false));
    }

    [Theory]
    [InlineData(0.1f, 0.1f)]    // T10's dusk: a tenth of the radiance
    [InlineData(0.35f, 0.35f)]  // T16's dawn
    [InlineData(4.6f, 1f)]      // T06's night asks for more than the radiance: the radiance, as before
    [InlineData(-1f, 0f)]
    [InlineData(float.NaN, 1f)]
    public void TheSkysContributionIsTheEnergyAskedForUpToTheWholeRadiance(float energy, float contribution) =>
        Assert.Equal(contribution, Main.SkyContribution(energy));

    [Fact]
    public void CaptureSettingsReadAsTheFileWouldHoldThem()
    {
        var o = ShellSettings.ParseOverrides("port.lighting_calibration=sunlit, port.palette_grade=true,port.haze=2,port.x=0.5,port.terrain_shadows=\"fine\",bad,=1,port.=2");
        Assert.Equal(5, o.Count);
        Assert.Equal(("port", "lighting_calibration", (object)"sunlit"), o[0]);
        Assert.Equal(true, o[1].Value);
        Assert.Equal(2L, o[2].Value);
        Assert.Equal(0.5, o[3].Value);
        Assert.Equal("fine", o[4].Value);
        Assert.Empty(ShellSettings.ParseOverrides(null));
    }

    [Fact]
    public void TheSunCameraLooksFromBehindTheCarAtTheBearingAskedFor()
    {
        // the sun due -X (Godot frame) and low: looking into it is yaw 90, the camera 20 m on the far side (+X) of the car
        var light = new Vector3(-0.97f, 0.26f, 0f);
        var v = Main.SunCam("0,8,20,-5", light)!.Split(',').Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(20f, v[0], 2);
        Assert.Equal(8f, v[1]);
        Assert.Equal(0f, v[2], 2);
        Assert.Equal(90f, v[3], 2);
        Assert.Equal(-5f, v[4]);
        Assert.Null(Main.SunCam("1,2", light));
    }
}
