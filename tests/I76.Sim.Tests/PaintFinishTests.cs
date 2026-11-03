using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

public class PaintFinishTests
{
    [Theory]
    [InlineData("matte", false, PaintFinish.Matte)]
    [InlineData("Satin", false, PaintFinish.Satin)]
    [InlineData("gloss", true, PaintFinish.Gloss)]     // the new key wins over the old one
    [InlineData("glossy", false, PaintFinish.Gloss)]
    [InlineData(null, false, PaintFinish.Matte)]       // nothing saved: the default, the original's flat paint
    [InlineData(null, true, PaintFinish.Matte)]        // the old hood_matte = true carries forward
    [InlineData("nonsense", false, PaintFinish.Matte)]
    public void Parse(string? word, bool legacyMatte, int expect) => Assert.Equal(expect, PaintFinish.Parse(word, legacyMatte));

    [Fact]
    public void WordRoundTrips()
    {
        for (int f = 0; f < PaintFinish.Names.Length; f++) Assert.Equal(f, PaintFinish.Parse(PaintFinish.Word(f)));
    }

    /// <summary>The row marks the port's shine (the pads' *); the cfg words stay plain.</summary>
    [Fact]
    public void TheRowMarksTheShineAndTheFileKeepsPlainWords()
    {
        Assert.Equal(["Matte", "Satin*", "Gloss*"], PaintFinish.Names);
        Assert.Equal("satin", PaintFinish.Word(PaintFinish.Satin));
        Assert.Equal("gloss", PaintFinish.Word(PaintFinish.Gloss));
    }
}
