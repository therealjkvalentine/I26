using I76.Game.Shell;
using I76.Game.Ui;
using I76.Sim.Views;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Graphic Detail's Hood Gloss: shell.cfg's level and the earlier Hood Finish's words, the rows' steps, the Developer panel's slider.</summary>
public class HoodGlossTests
{
    /// <summary>What Save writes reads back as the same level, for every level; the default leaves the key out.</summary>
    [Fact]
    public void EveryLevelWritesAndReadsBack()
    {
        Assert.Null(PaintFinish.FileValue(PaintFinish.Default));
        for (int v = PaintFinish.Min; v <= PaintFinish.Max; v++)
            Assert.Equal(v, PaintFinish.Parse(PaintFinish.FileValue(v)?.ToString(), null, false));
        Assert.Equal(100, PaintFinish.FileValue(130));
    }

    /// <summary>A file from before the level keeps its finish: matte 0, satin 50, gloss 100 (and the older hood_matte's Matte).</summary>
    [Theory]
    [InlineData("matte", 0)]
    [InlineData("satin", 50)]
    [InlineData("gloss", 100)]
    public void TheEarlierFinishCarriesForward(string word, int level)
    {
        Assert.Equal(level, PaintFinish.Parse(null, word, false));
        // once saved it is the level: the earlier key is not written again
        Assert.Equal(level, PaintFinish.Parse(PaintFinish.FileValue(level)?.ToString(), null, false));
    }

    /// <summary>A fresh settings file starts at 5 (a hint of sheen); the original's flat paint, Matte (0), stays the unmarked value, and a file with no key (from when Matte was the default) starts at 5.</summary>
    [Fact]
    public void AFreshSettingsIsFive()
    {
        Assert.Equal(5, new ShellSettings().HoodGloss);
        Assert.Equal(5, PaintFinish.Parse(null, null, false));
        Assert.Equal(PaintFinish.Matte, PaintFinish.Original);
        Assert.Equal("5*", PaintFinish.Name(PaintFinish.Default));
        Assert.Equal(0, PaintFinish.Matte);
        Assert.Equal(50, PaintFinish.Satin);
        Assert.Equal(100, PaintFinish.Gloss);
    }

    /// <summary>The Developer panel sweeps every level live on a slider; 0 (Matte) is the original's and unmarked.</summary>
    [Fact]
    public void TheDeveloperPanelHasASliderOverEveryLevel()
    {
        var e = Assert.Single(DevSettings.WithRow, d => d.Key == "hood_gloss");
        Assert.True(e.Slider);
        Assert.Equal(DevWhen.Live, e.When);
        Assert.Equal(101, e.Values.Length);
        Assert.Single(e.Values, v => !v.EndsWith(ShellSettings.PortMark, StringComparison.Ordinal));
        var s = new ShellSettings();
        Assert.Equal("5*", e.Values[e.Get(s)]);
        Assert.Equal("0", e.Values[0]);
        for (int i = 0; i < e.Values.Length; i++) { e.Set(s, i); Assert.Equal(i, e.Get(s)); Assert.Equal(i, s.HoodGloss); }
        // a key with a row is not one of the settings without a row
        Assert.DoesNotContain(DevSettings.All, d => d.Key == "hood_gloss");
    }

    /// <summary>The rows step in fives: 21 values, a preset's name at 0, 50 and 100.</summary>
    [Fact]
    public void TheRowsStepInFives()
    {
        Assert.Equal(PaintFinish.Steps.Length, PaintFinish.StepNames.Length);
        Assert.Equal(["Matte", "5*", "10*"], PaintFinish.StepNames[..3]);
        Assert.Equal("Satin*", PaintFinish.StepNames[10]);
        Assert.Equal("Gloss*", PaintFinish.StepNames[^1]);
        for (int i = 0; i < PaintFinish.Steps.Length; i++) Assert.Equal(i, PaintFinish.NearestStep(PaintFinish.Steps[i]));
    }
}
