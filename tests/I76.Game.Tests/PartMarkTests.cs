using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The BUILD AND REPAIR form's part marks (record 69's braces) are drawn in the modern look as the legend draws them: the
/// parenthesised letter, (C) in the car, (V) in the van, (R) in repair, in the legend's red. Other glyphs are left alone.
/// </summary>
public class PartMarkTests
{
    [Theory]
    [InlineData('{', "(C)")]
    [InlineData('|', "(V)")]
    [InlineData('}', "(R)")]
    public void PartMarkGlyphsAreTheLegendsParenthesisedLetters(char glyph, string mark) => Assert.Equal(mark, ShellGlyphs.MarkOf(69, glyph));

    [Theory]
    [InlineData(69, 'A')]
    [InlineData(68, '{')]
    [InlineData(66, '|')]
    public void OtherGlyphsAreNotMarks(int record, char glyph) => Assert.Null(ShellGlyphs.MarkOf(record, glyph));

    [Fact]
    public void MarksAreRedInkLikeTheLegend()
    {
        var ink = ShellGlyphs.LegendInk;
        Assert.True(ink.R > 0.5f && ink.R > 2 * ink.G && ink.G > ink.B);
    }
}
