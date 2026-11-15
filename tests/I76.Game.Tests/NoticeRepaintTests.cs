using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// A notice (NoticePopup) closes by putting the clean background back under its box, 179..479 by 194..304 of the 640x480
/// frame, whatever the screen had drawn there; the screen must draw itself again after it. FIELD SALVAGE's weapons list
/// (from x 173, rows 17 px apart from y 52) has its tenth and eleventh rows inside that box: after T04's CAN'T SALVAGE FROM A
/// DREAM notice their part names were left cut to their first pixels (a circled mark, an open bracket).
/// </summary>
public class NoticeRepaintTests
{
    private static string RepoFile(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "I76.sln"))) return Path.Combine(d.FullName, rel);
        throw new FileNotFoundException(rel);
    }

    /// <summary>The weapons list's tenth and eleventh rows lie inside the box a notice restores.</summary>
    [Fact]
    public void TheSalvageWeaponsTenthAndEleventhRowsAreUnderTheNotice()
    {
        var box = new Godot.Rect2I(179, 194, 300, 110);
        for (int row = 9; row <= 10; row++)
            Assert.True(box.HasPoint(new Godot.Vector2I(173 + 2 + 20, 51 + 1 + row * 17 + 4)), $"row {row + 1}");
    }

    /// <summary>Every notice the shell opens is given what follows its close: the next screen, or the screen under it drawn again.</summary>
    [Fact]
    public void EveryNoticeHandsTheScreenBack()
    {
        var dir = RepoFile(Path.Combine("game", "Scripts", "Shell"));
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var src = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(src, @"new NoticePopup\b[^;]*"))
            {
                var init = m.Value;
                Assert.True(init.Contains("Closed ="), $"{Path.GetFileName(file)}: a notice with nothing after its close: {init}");
                // a step that may be null (an optional parameter) falls back to the screen's own repaint
                var closed = Regex.Match(init, @"Closed = ([A-Za-z_]+) [}\n]");
                Assert.False(closed.Success && closed.Groups[1].Value == "then", $"{Path.GetFileName(file)}: Closed = then may be null: {init}");
            }
        }
        var garage = File.ReadAllText(Path.Combine(dir, "GarageScreens.cs"));
        Assert.Contains("Closed = then ?? Repaint", garage);
    }
}
