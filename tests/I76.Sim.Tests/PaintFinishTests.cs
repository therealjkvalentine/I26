using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>Hood Gloss: a level 0..100, the earlier Hood Finish's three words read as 0, 50 and 100, the shader's blend through them.</summary>
public class PaintFinishTests
{
    [Theory]
    [InlineData("35", null, false, 35)]
    [InlineData(" 37 ", "gloss", false, 37)]        // the new key wins over the earlier ones
    [InlineData("150", null, false, 100)]           // clamped to the range
    [InlineData("-5", null, false, 0)]
    [InlineData("62.6", null, false, 63)]           // a float written by hand rounds
    [InlineData("shiny", "satin", false, 50)]       // not a number: the earlier word
    [InlineData(null, "matte", false, 0)]
    [InlineData(null, "Satin", false, 50)]
    [InlineData(null, "gloss", true, 100)]          // hood_finish wins over hood_matte
    [InlineData(null, "glossy", false, 100)]
    [InlineData(null, null, false, 5)]              // nothing saved: the default, a hint of sheen (round 5; Matte before)
    [InlineData(null, null, true, 0)]               // the older hood_matte = true carries forward
    [InlineData(null, "nonsense", false, 5)]
    public void Parse(string? gloss, string? word, bool legacyMatte, int expect) => Assert.Equal(expect, PaintFinish.Parse(gloss, word, legacyMatte));

    [Fact]
    public void ALevelRoundTrips()
    {
        for (int v = PaintFinish.Min; v <= PaintFinish.Max; v++) Assert.Equal(v, PaintFinish.Parse(v.ToString()));
    }

    /// <summary>The rows show a preset's name at 0, 50 and 100 and the number between, the port's marked (the pads' *).</summary>
    [Fact]
    public void TheRowNamesThePresetsAndMarksTheShine()
    {
        Assert.Equal(21, PaintFinish.Steps.Length);
        Assert.Equal("Matte", PaintFinish.Name(0));
        Assert.Equal("Satin*", PaintFinish.Name(50));
        Assert.Equal("Gloss*", PaintFinish.Name(100));
        Assert.Equal("35*", PaintFinish.Name(35));
        Assert.Equal("37*", PaintFinish.Name(37));
        Assert.Single(PaintFinish.StepNames, n => !n.EndsWith('*'));
        Assert.Equal("Matte", PaintFinish.NearestPreset(24));
        Assert.Equal("Satin", PaintFinish.NearestPreset(25));
        Assert.Equal("Satin", PaintFinish.NearestPreset(75));
        Assert.Equal("Gloss", PaintFinish.NearestPreset(76));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(37, 40)]
    [InlineData(95, 100)]
    [InlineData(100, 0)]
    public void AClickGoesToTheNextStep(int level, int next) => Assert.Equal(next, PaintFinish.NextStep(level));

    [Theory]
    [InlineData(37, 7)]
    [InlineData(38, 8)]
    [InlineData(100, 20)]
    public void AHandSetLevelShowsAsTheNearestStep(int level, int step) => Assert.Equal(step, PaintFinish.NearestStep(level));

    /// <summary>0, 50 and 100 are the earlier Matte, Satin and Gloss exactly (the plain paint: roughness 0.4, specular 0.5, the full coat at 0.06).</summary>
    [Fact]
    public void ThePresetsAreTheEarlierSteps()
    {
        static void Is(PaintFinish.Material m, params float[] want)
        {
            float[] got = [m.Roughness, m.Specular, m.Clearcoat, m.ClearcoatRoughness, m.Metallic, m.HubcapCoat];
            for (int i = 0; i < want.Length; i++) Assert.Equal(want[i], got[i], 5);
        }
        Is(PaintFinish.Blend(0), 0.88f, 0.12f, 0f, 0.3f, 0f, 0f);
        Is(PaintFinish.Blend(50), 0.65f, 0.3f, 0.2f, 0.3f, 0f, 0.5f);
        Is(PaintFinish.Blend(100), 0.4f, 0.5f, 1f, 0.06f, 0f, 0.5f);
    }

    /// <summary>Every step shines at least as much as the one below: roughness and the coat's roughness never rise, the coat and specular never fall.</summary>
    [Fact]
    public void TheShineRisesSteadily()
    {
        var prev = PaintFinish.Blend(0);
        for (int v = 1; v <= 100; v++)
        {
            var b = PaintFinish.Blend(v);
            Assert.True(b.Roughness <= prev.Roughness + 1e-6f && b.ClearcoatRoughness <= prev.ClearcoatRoughness + 1e-6f, $"{v}");
            Assert.True(b.Clearcoat >= prev.Clearcoat - 1e-6f && b.Specular >= prev.Specular - 1e-6f, $"{v}");
            prev = b;
        }
        var q = PaintFinish.Blend(25);
        Assert.Equal(0.765f, q.Roughness, 3);
        Assert.Equal(0.1f, q.Clearcoat, 3);
        var t = PaintFinish.Blend(75);
        Assert.Equal(0.525f, t.Roughness, 3);
        Assert.Equal(0.6f, t.Clearcoat, 3);
    }
}
