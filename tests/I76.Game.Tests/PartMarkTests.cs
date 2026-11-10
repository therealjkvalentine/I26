using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The BUILD AND REPAIR form's part marks (record 69's braces) are circled handwritten letters in the modern look, as the original's:
/// C in the car, V in the van, R in repair. The form's key (the art's legend) carries the same circled letters. Other glyphs are left alone.
/// </summary>
public class PartMarkTests
{
    [Theory]
    [InlineData('{', 'C')]
    [InlineData('|', 'V')]
    [InlineData('}', 'R')]
    public void PartMarkGlyphsAreCircledLetters(char glyph, char letter) => Assert.Equal(letter, ShellGlyphs.CircledLetterOf(69, glyph));

    [Theory]
    [InlineData(69, 'A')]
    [InlineData(68, '{')]
    [InlineData(66, '|')]
    public void OtherGlyphsAreNotMarks(int record, char glyph) => Assert.Null(ShellGlyphs.CircledLetterOf(record, glyph));
}
