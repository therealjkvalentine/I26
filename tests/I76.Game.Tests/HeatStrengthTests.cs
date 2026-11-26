using I76.Game.Shell;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Shimmer Strength ([port] heat_strength, HeatStrengthRow): Medium in a fresh block and in every file from before the row, written only
/// when not Medium, read back, applied to the pass as one factor over fire, ripple and mirage; and the hot day's ripple band (HeatBand)
/// where section IJ put it: low over the far road and the ground beside it, nothing near, nothing high in the view. Without the engine.
/// </summary>
public class HeatStrengthTests
{
    [Fact]
    public void MediumIsTheDefaultAndAnOldFileGetsIt()
    {
        var s = new ShellSettings();
        Assert.Equal(HeatStrengthRow.Medium, s.HeatStrength);
        Assert.False(s.Write(null).HasSectionKey("port", "heat_strength"));
        var old = ShellSettings.Read(ShellFileCompatTests.Parse(ShellFileCompatTests.ForkMainFile));
        Assert.Equal(HeatStrengthRow.Medium, old.HeatStrength);
        Assert.Equal(1f, HeatStrengthRow.Factor(old.HeatStrength));
    }

    [Theory]
    [InlineData(HeatStrengthRow.Low, "low")]
    [InlineData(HeatStrengthRow.High, "high")]
    [InlineData(HeatStrengthRow.Extreme, "extreme")]
    public void AChoiceSurvivesASaveAndALoad(int strength, string word)
    {
        var file = new ShellSettings { HeatShimmer = HeatShimmerRow.Road, HeatStrength = strength }.Write(null);
        Assert.Equal(word, file.GetValue("port", "heat_strength").ToString());
        var back = ShellSettings.Read(file);
        Assert.Equal(strength, back.HeatStrength);
        Assert.Equal(HeatShimmerRow.Road, back.HeatShimmer);
    }

    [Fact]
    public void AnUnknownWordIsMedium()
    {
        Assert.Equal(HeatStrengthRow.Medium, HeatStrengthRow.FromFile(null));
        Assert.Equal(HeatStrengthRow.Medium, HeatStrengthRow.FromFile("blistering"));
        Assert.Equal(HeatStrengthRow.High, HeatStrengthRow.FromFile(" High "));
    }

    [Fact]
    public void TheRowStepsAndScales()
    {
        Assert.Equal(["Low*", "Medium*", "High*", "Extreme*"], HeatStrengthRow.Names);
        var s = new ShellSettings();
        HeatStrengthRow.Step(s); Assert.Equal(HeatStrengthRow.High, s.HeatStrength);
        HeatStrengthRow.Step(s); Assert.Equal(HeatStrengthRow.Extreme, s.HeatStrength);
        HeatStrengthRow.Step(s); Assert.Equal(HeatStrengthRow.Low, s.HeatStrength);
        HeatStrengthRow.Put(s, 9); Assert.Equal(HeatStrengthRow.Extreme, s.HeatStrength);
        // each step stronger, Low half of Medium
        Assert.Equal(0.5f, HeatStrengthRow.Factor(HeatStrengthRow.Low));
        for (int i = 1; i < HeatStrengthRow.Factors.Length; i++) Assert.True(HeatStrengthRow.Factors[i] > HeatStrengthRow.Factors[i - 1]);
        Assert.Equal("high", HeatStrengthRow.NameOf(1.6f));
        Assert.Equal("custom", HeatStrengthRow.NameOf(1.3f));
    }

    [Fact]
    public void ItSitsUnderHeatShimmerAndResetPutsMediumBack()
    {
        var fx = GraphicsPages.Rows[GraphicsPages.Effects];
        Assert.Equal(Array.IndexOf(fx, GraphicsRow.HeatShimmer) + 1, Array.IndexOf(fx, GraphicsRow.HeatStrength));
        var s = new ShellSettings { HeatStrength = HeatStrengthRow.Low };
        GraphicsPages.Reset(s, GraphicsPages.Effects);
        Assert.Equal(HeatStrengthRow.Medium, s.HeatStrength);
        var t = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.HeatStrength, new ShellSettings { HeatStrength = HeatStrengthRow.Extreme }, t);
        Assert.Equal(HeatStrengthRow.Extreme, t.HeatStrength);
        foreach (bool look1997 in (bool[])[false, true]) Assert.NotEmpty(GraphicsHelp.Text(GraphicsRow.HeatStrength, look1997));
    }

    [Fact]
    public void ApplyHandsTheFactorToThePass()
    {
        try
        {
            HeatShimmer.Apply(HeatShimmerRow.Road, false, HeatStrengthRow.Factor(HeatStrengthRow.High));
            Assert.Equal(1.6f, HeatShimmer.Strength);
            Assert.Contains("strength 1.6 (high)", HeatTune.Line());
            HeatShimmer.Apply(HeatShimmerRow.Road, false);
            Assert.Equal(1f, HeatShimmer.Strength);
        }
        finally { HeatShimmer.Apply(HeatShimmerRow.Off, false); }
    }

    [Fact]
    public void TheRippleStaysLowOverTheFarRoad()
    {
        // nothing near (the ground beside the car, the hood), full past 400 m, a little more far away
        Assert.Equal(0f, HeatBand.Weight(-1f, 40f));
        Assert.Equal(0f, HeatBand.Weight(-0.5f, HeatBand.DefaultNear));
        Assert.Equal(1f, HeatBand.Weight(-0.3f, 400f), 3);
        Assert.True(HeatBand.Weight(-0.05f, 1500f) > 1.2f);
        Assert.True(HeatBand.Weight(-0.05f, 1500f) <= 1f + HeatBand.FarGain);
        // only rays near level: the far road's band, not a mesa's face or the sky over it
        Assert.Equal(1f, HeatBand.Angle(0.3f), 3);
        Assert.Equal(0f, HeatBand.Angle(2f));
        Assert.Equal(0f, HeatBand.Angle(-2f));
        Assert.Equal(0f, HeatBand.Weight(5f, float.PositiveInfinity));
        // the same band either side of level by default (section IJ's 1.72 degrees), full within a fifth of it
        Assert.Equal(HeatBand.DefaultAboveDeg, HeatBand.DefaultBelowDeg);
        Assert.Equal(1.72f, HeatBand.DefaultBelowDeg);
        Assert.Equal(HeatBand.Angle(0.8f), HeatBand.Angle(-0.8f));
        Assert.True(HeatBand.Angle(0.8f) is > 0f and < 1f);
        // the shipped tuning starts from the band
        Assert.Equal(HeatBand.DefaultNear, HeatTune.DefaultRoadNear);
        Assert.Equal(HeatBand.DefaultBelowDeg, HeatTune.DefaultRippleDeg);
    }

    [Fact]
    public void TheNudgeIsAnAngleSoItKeepsItsSizeOnABigScreen()
    {
        // IJ's was 1.8 pixels of the 3D view at any size: at 3440x1440 and Render Scale 200 % under a pixel on the screen. Now an angle:
        // the same share of the view at any resolution, never under 1.8 pixels
        float At(float focalPx) => Math.Max(HeatBand.AmplitudeRad * focalPx, HeatBand.MinPixels);
        float hd = At(935f), owner = At(935f * 1440f / 1080f * 2f);
        Assert.True(hd >= 2f);
        Assert.Equal(hd * 1440f / 1080f * 2f, owner, 2);
        Assert.Equal(HeatBand.MinPixels, At(300f));
    }
}
