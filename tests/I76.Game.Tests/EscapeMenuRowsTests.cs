using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The Options Menu pad's port rows: Report a Bug on row 13 and Developer on row 14, away from the original's rows, reached by Down from Controls.</summary>
public class EscapeMenuRowsTests
{
    private static string Source()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "game", "Scripts", "Ui", "EscapeMenu.cs"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(dir!, "game", "Scripts", "Ui", "EscapeMenu.cs"));
    }

    [Fact]
    public void ReportABugAndDeveloperSitOnRows13And14()
    {
        var src = Source();
        Assert.Contains("private const int BugRow = 13;", src);
        // both pads (the network game's and the single player's) put Report a Bug on BugRow; Developer is the line under it
        Assert.Equal(2, Regex.Matches(src, @"Action\(""Report a Bug"", BugRow, ReportBug").Count);
        Assert.Matches(@"Action\(""Developer Tools"", BugRow \+ 1,", src);
    }

    [Fact]
    public void DownFromControlsReachesReportABugThenTheCloseButton()
    {
        // the list order is the navigation order (Next steps through Items, wrapping): Controls, Report a Bug, then Resume or Exit
        var src = Source();
        var single = src[src.IndexOf("Action(\"Abort Mission\", 1,", StringComparison.Ordinal)..];
        single = single[..single.IndexOf("];", StringComparison.Ordinal)];
        var labels = Regex.Matches(single, @"Action\(""([^""]+)"", (\w+),").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        Assert.Equal(["Abort Mission", "Play Options", "Graphic Detail", "Audio Control", "Controls", "Report a Bug"], labels.Select(l => l.Item1));
        Assert.Equal("BugRow", labels[^1].Item2);
        Assert.Contains("CancelButton(\"Exit\")", single);
        // the original's rows stay where they were
        Assert.Equal(["1", "3", "4", "5", "6"], labels.Take(5).Select(l => l.Item2));
        // Developer is inserted before the close button, so Down from Report a Bug reaches it
        Assert.Contains("_main.Items.Insert(_main.Items.Count - 1, Action(\"Developer Tools\"", src);
    }
}
