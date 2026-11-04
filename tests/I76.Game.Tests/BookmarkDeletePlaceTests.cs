using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Delete is on Save Bookmark as well as Load Bookmark (a tester opened Save Bookmark from the trip's salvage form and found
/// no way to delete), and on both it stands on the sheet, in the same place: inside its 186..452 columns, clear of the list's
/// rows and of the other buttons: at the foot on the buttons' line, under the rows (the owner's layout). Load Bookmark has no other button of the port's (Import Saves and All Scenes are on the
/// shell's Developer panel).
/// </summary>
public class BookmarkDeletePlaceTests
{
    // the overlays' small font: about six pixels a character
    private static int Measure(string s) => 6 * s.Length;

    [Fact]
    public void DeleteStandsOnTheButtonsLineUnderTheRows()
    {
        var (x, y) = BookmarkScreen.DeletePlace(Measure);
        Assert.InRange(x, 186, 452 - Measure("Delete"));
        Assert.Equal(420, y);                                                    // the buttons' line (Load or Save at 328, Cancel), under the last row
        Assert.True(y >= 184 + 10 * 23);                                         // below the tenth row (rows 23 apart, measured)
        Assert.Equal(328 - 25, x + Measure("Delete"));                           // left of the action, Cancel's 25 px between
        Assert.True(x > 201 + 20);                                               // clear of the scroll-down arrow (201, 20 wide)
    }

    [Fact]
    public void ThePadsDownFromTheListLandsOnTheActionNotDelete()
    {
        var (dx, dy) = BookmarkScreen.DeletePlace(Measure);
        // the last row shown and the buttons' line (a button's hit box: its text, the small font about 12 high)
        var row = new I76.Formats.Input.NavRect(190, 184 + 9 * 23, 262, 23);
        I76.Formats.Input.NavRect[] rects = [row, new(dx, dy, Measure("Delete"), 12), new(328, 420, Measure("Load"), 12), new(328 + Measure("Load") + 25, 420, Measure("Cancel"), 12)];
        Assert.Equal(2, I76.Formats.Input.PadNav.Pick(rects, 0, I76.Formats.Input.NavDir.Down));
        Assert.Equal(1, I76.Formats.Input.PadNav.Pick(rects, 2, I76.Formats.Input.NavDir.Left));
    }
}
