using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's help line (GraphicsHelp): the eight rows that cost the most say what they do and what they cost, in both looks,
/// every other row nothing; the costs are the survey's (docs/perf/costs.md) and the 1997 look's lines say they cost nothing there.
/// </summary>
public class GraphicsHelpTests
{
    private static readonly GraphicsRow[] TopEight =
    [
        GraphicsRow.AmbientOcclusion, GraphicsRow.RearMirror, GraphicsRow.AntiAliasing, GraphicsRow.ShadowDetail,
        GraphicsRow.TerrainDetail, GraphicsRow.Vegetation, GraphicsRow.VisibilityRange, GraphicsRow.HdTextures,
    ];

    [Fact]
    public void TheTopEightHaveALineInBothLooksAndNoOtherRowDoes()
    {
        foreach (var r in Enum.GetValues<GraphicsRow>())
            foreach (bool look1997 in new[] { false, true })
            {
                var t = GraphicsHelp.Text(r, look1997);
                if (TopEight.Contains(r)) Assert.False(string.IsNullOrWhiteSpace(t), $"{r} has no help");
                else Assert.Null(t);
            }
    }

    [Theory]
    [InlineData(GraphicsRow.AmbientOcclusion, "29 %")]
    [InlineData(GraphicsRow.RearMirror, "17 %")]
    [InlineData(GraphicsRow.AntiAliasing, "12 %")]
    [InlineData(GraphicsRow.ShadowDetail, "8 %")]
    [InlineData(GraphicsRow.TerrainDetail, "6 %")]
    [InlineData(GraphicsRow.Vegetation, "4 %")]
    [InlineData(GraphicsRow.VisibilityRange, "3 %")]
    [InlineData(GraphicsRow.HdTextures, "0.7 GB")]
    public void TheModernLookSaysTheCost(GraphicsRow r, string cost) => Assert.Contains(cost, GraphicsHelp.Text(r, false));

    [Fact]
    public void TheMirrorsLineNamesItsTwoReaches()
    {
        var t = GraphicsHelp.Text(GraphicsRow.RearMirror, false)!;
        Assert.Contains("100 m", t);
        Assert.Contains("Full", t);
        Assert.Contains("mirror_range", t);
    }

    [Theory]
    [InlineData(GraphicsRow.AmbientOcclusion)]
    [InlineData(GraphicsRow.AntiAliasing)]
    [InlineData(GraphicsRow.ShadowDetail)]
    [InlineData(GraphicsRow.TerrainDetail)]
    [InlineData(GraphicsRow.Vegetation)]
    public void The1997LooksCostlessRowsSaySo(GraphicsRow r) => Assert.Contains("No cost in the 1997 look", GraphicsHelp.Text(r, true));

    /// <summary>Each line is plain text the shell's bitmap font has: printable ASCII only.</summary>
    [Fact]
    public void TheLinesArePrintableAscii()
    {
        foreach (var r in TopEight)
            foreach (bool look1997 in new[] { false, true })
                Assert.All(GraphicsHelp.Text(r, look1997)!, c => Assert.InRange(c, ' ', '~'));
    }

    [Fact]
    public void WrapBreaksAtSpacesWithinTheWidth()
    {
        float Measure(string s) => s.Length * 6f;
        var lines = GraphicsHelp.Wrap("Ambient occlusion: contact shadows under cars and shrubs.", Measure, 120);
        Assert.Equal(["Ambient occlusion:", "contact shadows", "under cars and", "shrubs."], lines);
        Assert.All(lines, l => Assert.True(Measure(l) <= 120));
        // a word wider than the line keeps a line of its own
        Assert.Equal(["a", "verylongwordindeed", "b"], GraphicsHelp.Wrap("a verylongwordindeed b", Measure, 60));
        Assert.Empty(GraphicsHelp.Wrap("", Measure, 60));
    }
}
