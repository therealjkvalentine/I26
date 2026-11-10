using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The equipment tooltips on the garage's lists (PartTips through the menus' MenuTooltip): Play Options' row and its default, the
/// tooltip's two lines, and the box beside a part row, never over it, inside the screen at 4:3 and on the owner's ultrawide.
/// </summary>
public class EquipmentTooltipsTests
{
    [Fact]
    public void TheOptionIsOnByDefaultAndHasItsRowAndTooltip()
    {
        Assert.True(new ShellSettings().EquipmentTips);
        Assert.Contains("Equipment Tooltips", PlayOptionsScreen.PortRows);
        Assert.Contains("garage", MenuTips.Of("Equipment Tooltips"));
    }

    [Fact]
    public void ALineBreakStartsTheNumbersLine()
    {
        float Measure(string s) => s.Length * 5f;
        var lines = TooltipPlacement.Wrap("Cup Holders: one hit in 10 past the armor spares the parts.\nHolds a cup, too.", Measure, 1000);
        Assert.Equal(["Cup Holders: one hit in 10 past the armor spares the parts.", "Holds a cup, too."], lines);
        // a long first line still wraps, then the second starts on its own line
        lines = TooltipPlacement.Wrap("aaaa bbbb cccc\ndddd", Measure, 40);
        Assert.Equal(["aaaa", "bbbb", "cccc", "dddd"], lines);
        // without a break nothing changes (the menus' tooltips)
        Assert.Equal(TooltipPlacement.Wrap("one two three", Measure, 40), GraphicsHelp.Wrap("one two three", Measure, 40));
    }

    public static IEnumerable<object[]> Screens => [[1280f, 960f], [3440f, 1440f]];

    /// <summary>
    /// The garage's rows in shell units (x, y, w, h): BUILD AND REPAIR's part, weapon and special rows, the repair order, DEFENSE's cells,
    /// a picker's row, the CAR / VAN INVENTORY's columns (left and right), FIELD SALVAGE's lists, the melee form's car panel.
    /// </summary>
    private static readonly (int X, int Y, int W, int H)[] GarageRows =
    [
        (26, 39, 240, 17), (26, 141, 240, 17), (26, 165, 240, 17), (26, 274, 240, 17), (26, 308, 240, 17),
        (450, 65, 166, 17), (450, 218, 166, 17), (106, 354, 80, 17), (186, 405, 80, 17),
        (252, 154, 136, 17), (252, 306, 136, 17),
        (27, 129, 144, 17), (467, 249, 144, 17), (464, 230, 146, 17),
        (26, 35, 145, 17), (485, 360, 143, 17), (168, 446, 143, 17),
        (468, 56, 162, 17), (468, 226, 162, 17),
    ];

    [Theory]
    [MemberData(nameof(Screens))]
    public void BesideAPartRowTheBoxStaysOnTheScreenAndOffTheRow(float w, float h)
    {
        float scale = Math.Min(w / 640, h / 480), offX = (w - 640 * scale) / 2, offY = (h - 480 * scale) / 2;
        float bw = TooltipPlacement.MaxWidth(scale, w);
        foreach (int lines in new[] { 2, 4 })
        {
            float bh = (lines * 11f + 8) * scale;
            foreach (var (x, y, rw, rh) in GarageRows)
            {
                var row = new TipBox(offX + x * scale, offY + y * scale, rw * scale, rh * scale);
                var b = TooltipPlacement.Place(bw, bh, w, h, row.X + 5, row.Y + 5, true, row);
                Assert.InRange(b.X, TooltipPlacement.Margin, w - TooltipPlacement.Margin - bw);
                Assert.InRange(b.Y, TooltipPlacement.Margin, h - TooltipPlacement.Margin - bh);
                bool overlaps = b.X < row.Right && b.Right > row.X && b.Y < row.Bottom && b.Bottom > row.Y;
                Assert.False(overlaps, $"{w}x{h}: the box covers the row at ({x}, {y})");
            }
        }
    }
}
