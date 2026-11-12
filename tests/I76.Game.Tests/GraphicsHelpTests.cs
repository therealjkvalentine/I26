using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's tooltips (GraphicsHelp): every row says what it does in both looks, and the costly ones what they cost; the costs
/// are the survey's (docs/perf/costs.md) and the 1997 look's lines say they cost nothing there.
/// </summary>
public class GraphicsHelpTests
{
    /// <summary>A row with no tooltip fails here: every row of both pages, in both looks.</summary>
    [Fact]
    public void EveryRowHasATooltipInBothLooks()
    {
        foreach (var r in Enum.GetValues<GraphicsRow>())
            foreach (bool look1997 in new[] { false, true })
                Assert.False(string.IsNullOrWhiteSpace(GraphicsHelp.Text(r, look1997)), $"{r} has no tooltip");
        foreach (var page in GraphicsPages.Rows)
            foreach (var r in page) Assert.False(string.IsNullOrWhiteSpace(GraphicsHelp.Text(r, false)));
        Assert.False(string.IsNullOrWhiteSpace(GraphicsHelp.AdvancedEntry));
        Assert.Equal(GraphicsHelp.AdvancedEntry, MenuTips.Of("Advanced..."));
    }

    [Theory]
    [InlineData(GraphicsRow.AmbientOcclusion, "29 %")]
    [InlineData(GraphicsRow.Mirrors, "17 %")]
    [InlineData(GraphicsRow.AntiAliasing, "12 %")]
    [InlineData(GraphicsRow.ShadowDetail, "8 %")]
    [InlineData(GraphicsRow.TerrainDetail, "6 %")]
    [InlineData(GraphicsRow.Vegetation, "4 %")]
    [InlineData(GraphicsRow.VisibilityRange, "3 %")]
    [InlineData(GraphicsRow.WorldTextures, "0.7 GB")]
    [InlineData(GraphicsRow.CarTextures, "0.5 GB")]
    public void TheModernLookSaysTheCost(GraphicsRow r, string cost) => Assert.Contains(cost, GraphicsHelp.Text(r, false));

    /// <summary>The texture rows say what they cover and how they differ: the world's ground and buildings; the cars and the dash, 4x against 8x.</summary>
    [Fact]
    public void TheTextureRowsSayTheDifference()
    {
        Assert.Contains("ground", GraphicsHelp.Text(GraphicsRow.WorldTextures, false));
        var car = GraphicsHelp.Text(GraphicsRow.CarTextures, false);
        Assert.Contains("dash", car);
        Assert.Contains("8x", car);
        Assert.Contains("1997", car);
        Assert.Contains("1997 look", GraphicsHelp.Text(GraphicsRow.WorldTextures, true));
        Assert.Contains("1997 look", GraphicsHelp.Text(GraphicsRow.CarTextures, true));
    }

    [Fact]
    public void TheMirrorsLineNamesItsTwoReaches()
    {
        var t = GraphicsHelp.Text(GraphicsRow.Mirrors, false)!;
        Assert.Contains("100 m", t);
        Assert.Contains("Developer panel", t);
        Assert.DoesNotContain("=", t);   // no config key shown to the player
    }

    [Theory]
    [InlineData(GraphicsRow.AmbientOcclusion)]
    [InlineData(GraphicsRow.ShadowDetail)]
    [InlineData(GraphicsRow.TerrainDetail)]
    [InlineData(GraphicsRow.Vegetation)]
    public void The1997LooksCostlessRowsSaySo(GraphicsRow r) => Assert.Contains("No cost in the 1997 look", GraphicsHelp.Text(r, true));

    /// <summary>Each line is plain text the shell's bitmap font has: printable ASCII only.</summary>
    [Fact]
    public void TheLinesArePrintableAscii()
    {
        foreach (var r in Enum.GetValues<GraphicsRow>())
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
        // a figure keeps its unit: "12 %" is never split
        Assert.Equal(["4x is about", "12 % of it"], GraphicsHelp.Wrap("4x is about 12 % of it", Measure, 66));
    }
}
