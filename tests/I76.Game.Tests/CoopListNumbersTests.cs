using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The host form's mission list rows (upstream #234: the co-op trip list read as if some missions had no number).</summary>
public class CoopListNumbersTests
{
    // a handwriting 9 px a letter and a smaller one 7 px a letter, in the list's 182 px
    private static int Big(string s) => 9 * s.Length;
    private static int Small(string s) => 7 * s.Length;

    [Fact]
    public void ATripMissionTooLongForTheRowKeepsItsNumber()
    {
        // it fits: as it is
        Assert.Equal(("Scene 1: Short", false), MeleeScreen.ListRowText("Scene 1: Short", Big, Small, 182));
        // too long for the handwriting: the smaller one, number and all
        Assert.Equal(("Scene 7: A Longer Name", true), MeleeScreen.ListRowText("Scene 7: A Longer Name", Big, Small, 182));
        // too long even so: cut at the end, never at the number
        var (text, smaller) = MeleeScreen.ListRowText("Scene 11: A Title Far Too Long For Any Row", Big, Small, 182);
        Assert.True(smaller);
        Assert.StartsWith("Scene 11: A Title", text);
        Assert.True(Small(text) <= 182);
        // an upstream-style "7. Title" keeps its "7. " too (the row used to drop it for a long title)
        Assert.StartsWith("7. ", MeleeScreen.ListRowText("7. A Title Far Too Long For Any Row At All", Big, Small, 182).Text);
    }

    [Fact]
    public void AListDrawnSmallerIsSmallerInEveryRow()
    {
        // a co-op list with a title too wide for the handwriting draws every row in the smaller one, so the trip's rows read alike
        Assert.Equal(("Scene 1: Short", true), MeleeScreen.ListRowText("Scene 1: Short", Big, Small, 182, smaller: true));
        Assert.Equal(("Training", true), MeleeScreen.ListRowText("Training", Big, Small, 182, smaller: true));
        for (int n = 1; n <= 17; n++)
            for (int len = 1; len <= 60; len += 7)
                Assert.StartsWith($"Scene {n}: ", MeleeScreen.ListRowText($"Scene {n}: {new string('x', len)}", Big, Small, 182, smaller: true).Text);
    }
}
