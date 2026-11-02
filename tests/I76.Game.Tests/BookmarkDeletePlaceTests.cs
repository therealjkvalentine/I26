using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Delete is on Save Bookmark as well as Load Bookmark (a tester opened Save Bookmark from the trip's salvage form and found
/// no way to delete), and on both it stands on the sheet: inside its 186..452 columns, clear of the list's rows and of the
/// other buttons.
/// </summary>
public class BookmarkDeletePlaceTests
{
    // the overlays' small font: about six pixels a character
    private static int Measure(string s) => 6 * s.Length;

    [Theory]
    [InlineData(false, false)]   // Save Bookmark
    [InlineData(true, false)]    // Load Bookmark
    [InlineData(true, true)]     // Nitro's Load Bookmark
    public void DeleteStandsOnTheSheetOnEveryBookmarkScreen(bool load, bool nitro)
    {
        var (x, y) = BookmarkScreen.DeletePlace(load, nitro, Measure);
        Assert.InRange(x, 186, 452 - Measure("Delete"));
        if (nitro) Assert.Equal(420, y);                                         // the buttons' line, left of Load at 328
        else Assert.Equal(158, y);                                               // the scroll-up arrow's line, above the first row at 184
        if (nitro) Assert.True(x + Measure("Delete") <= 328 - 25);
        if (!nitro && load) Assert.True(x >= 250 + Measure("All Scenes") + 25);  // after All Scenes
        if (!nitro && !load) Assert.Equal(250, x);                               // where All Scenes would be
    }
}
