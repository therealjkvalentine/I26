using System.Text.RegularExpressions;
using I76.Game.Shell;
using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The menus' tooltip (MenuTooltip): it shows after the usual hover delay and goes on leaving; it stays inside the screen by the
/// pointer and beside a focused row at 4:3 and on an ultrawide; and every row of every menu has one (a row without fails here).
/// </summary>
public class MenuTooltipTests
{
    private static string RepoFile(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "I76.sln"))) return Path.Combine(d.FullName, rel);
        throw new FileNotFoundException(rel);
    }

    [Fact]
    public void ItShowsAfterTheDelayAndGoesOnLeaving()
    {
        var t = new TooltipTimer();
        Assert.Equal(0.5, TooltipTimer.Delay);
        Assert.False(t.Update("row", 1 / 60.0));
        for (int i = 0; i < 28; i++) Assert.False(t.Update("row", 1 / 60.0));   // 0.48 s
        Assert.True(t.Update("row", 0.05));
        Assert.True(t.Update("row", 1 / 60.0));
        Assert.False(t.Update(null, 1 / 60.0));                                  // left: gone
        Assert.False(t.Update("other", 0.6));                                    // a new row starts its own delay
        Assert.True(t.Update("other", 0.6));
        t.Restart();                                                             // a click: gone until the delay again
        Assert.False(t.Update("other", 0.1));
        Assert.True(t.Update("other", 0.5));
    }

    public static IEnumerable<object[]> Screens =>
    [
        [1280f, 960f],     // 4:3
        [3440f, 1440f],    // the owner's ultrawide
        [1920f, 1080f],
        [640f, 480f],
    ];

    /// <summary>By the pointer anywhere on the screen, the box is inside it with its margin, and off the pointer's own spot.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void ByThePointerItStaysOnTheScreen(float w, float h)
    {
        float scale = Math.Min(w / 640, h / 480);
        float bw = TooltipPlacement.MaxWidth(scale, w), bh = 60 * scale;
        for (float px = 0; px <= w; px += w / 16)
            for (float py = 0; py <= h; py += h / 12)
            {
                var b = TooltipPlacement.Place(bw, bh, w, h, px, py, false, default);
                Assert.InRange(b.X, TooltipPlacement.Margin, w - TooltipPlacement.Margin - bw);
                Assert.InRange(b.Y, TooltipPlacement.Margin, h - TooltipPlacement.Margin - bh);
            }
        // in the open, below and right of the pointer
        var mid = TooltipPlacement.Place(100, 30, w, h, w / 3, h / 3, false, default);
        Assert.True(mid.X > w / 3 && mid.Y > h / 3);
    }

    /// <summary>For the pad and the keys, beside the focused row (right of it, else left), never over it, and inside the screen.</summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void BesideAFocusedRowItStaysOnTheScreen(float w, float h)
    {
        float scale = Math.Min(w / 640, h / 480), offX = (w - 640 * scale) / 2;
        float bw = TooltipPlacement.MaxWidth(scale, w), bh = 50 * scale;
        // a sheet's row (x 200..462 of the 640 frame, pillarboxed on a wide screen), top to bottom
        for (int line = 0; line < 14; line++)
        {
            var row = new TipBox(offX + 200 * scale, (118 + 23 * line) * scale, 262 * scale, 23 * scale);
            var b = TooltipPlacement.Place(bw, bh, w, h, 0, 0, true, row);
            Assert.InRange(b.X, TooltipPlacement.Margin, w - TooltipPlacement.Margin - bw);
            Assert.InRange(b.Y, TooltipPlacement.Margin, h - TooltipPlacement.Margin - bh);
            bool overlaps = b.X < row.Right && b.Right > row.X && b.Y < row.Bottom && b.Bottom > row.Y;
            // where the screen leaves room on a side, it does not cover the row
            if (row.X - bw - TooltipPlacement.Gap >= TooltipPlacement.Margin || row.Right + bw + TooltipPlacement.Gap <= w - TooltipPlacement.Margin) Assert.False(overlaps);
        }
    }

    [Fact]
    public void ABoxWiderThanTheScreenIsHeldToIt()
    {
        Assert.Equal(640 - 2 * TooltipPlacement.Margin, TooltipPlacement.MaxWidth(4, 640));
        var b = TooltipPlacement.Place(900, 40, 640, 480, 600, 450, false, default);
        Assert.Equal(TooltipPlacement.Margin, b.X);
    }

    // ---- coverage: every row has a tooltip ----

    private static void AllHaveTips(IEnumerable<string> labels, string where)
    {
        var missing = labels.Where(l => string.IsNullOrWhiteSpace(MenuTips.Of(l))).ToList();
        Assert.True(missing.Count == 0, $"{where}: no tooltip for {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryPlayOptionsRowHasATooltip() => AllHaveTips(
        ["Arcade Phys.", "No Salv. Manag.", "Unlim. Ammo", "Unlim. Armor", "Unlim. Chas. Rein.", "Unlim. Wiper Fluid", "Difficulty Level", .. PlayOptionsScreen.PortRows],
        "Play Options");

    [Fact]
    public void ArcadeAndModernPhysicsSayWhatTheyDo()
    {
        Assert.Contains("roof", MenuTips.Of("Arcade Phys."));
        Assert.Contains("original's handling", MenuTips.Of("Modern Physics"));
    }

    [Fact]
    public void EveryControlsRowHasATooltip()
    {
        AllHaveTips(ControlsMenuScreen.Rows.OfType<string>(), "Controls");
        AllHaveTips(["Mouse Camera", "Mouse Sensitivity", "Invert Y Axis", "Cockpit Mouse Look", "Camera Return", "View Zoom Reset", "Mouse In Window",
            "Mouse Button", "Mouse Wheel", "Restore Defaults"], "Mouse Options");
        AllHaveTips(["Key Binding", "Game Key", "Calibrate", "Restore"], "the key map");
    }

    [Fact]
    public void EveryAudioRowHasATooltip() => AllHaveTips(["Music Level", "Sfx Level", "SFX Level", "Voice Level", "CB Microphone", "Output", "LFE Level"], "Audio Control");

    /// <summary>The Options Menu's entries, read from the sheet's code: every Entry(y, "label", ...) has a tooltip.</summary>
    [Fact]
    public void EveryOptionsMenuEntryHasATooltip()
    {
        var src = File.ReadAllText(RepoFile(Path.Combine("game", "Scripts", "Shell", "Overlays.cs")));
        var labels = Regex.Matches(src, @"Entry\(\d+, ""([^""]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Contains("Controls", labels);
        Assert.DoesNotContain("Mouse Options", labels);           // under Controls now
        Assert.DoesNotContain("Control Configuration", labels);
        AllHaveTips(labels, "the Options Menu");
    }

    /// <summary>The Esc menu's pads, read from its code: every action, setting, slider and check row has a tooltip (its Graphic Detail rows are GraphicsHelp's, covered there).</summary>
    [Fact]
    public void EveryEscapeMenuRowHasATooltip()
    {
        var src = File.ReadAllText(RepoFile(Path.Combine("game", "Scripts", "Ui", "EscapeMenu.cs")));
        var labels = new List<string>();
        foreach (var pattern in new[] { @"Action\(""([^""]+)""", @"Level\(""([^""]+)""", @"Setting\(Cycle, ""([^""]+)""", @"Flags = Check, Label = ""([^""]+)""", @"Flags = Command, Label = ""([^""]+)""", @"Flags = Act, Label = ""([^""]+)""" })
            labels.AddRange(Regex.Matches(src, pattern).Select(m => m.Groups[1].Value));
        var checks = Regex.Match(src, @"string\[\] checks = \[([^\]]+)\]").Groups[1].Value;
        labels.AddRange(Regex.Matches(checks, @"""([^""]+)""").Select(m => m.Groups[1].Value));
        labels = [.. labels.Distinct()];
        // the Graphic Detail rows' own (G(...), Setting(...) with no Cycle literal) are GraphicsHelp's; the rest are by label
        var graphics = new HashSet<string> { "VSync", "Max FPS", "Show FPS", "Render Scale", "Vegetation", "Ambient Occlusion", "Field of View", "1997 Mode", "1997 Palette Grade", "Crash Blur", "Hood Gloss", "Visibility Range" };
        Assert.Contains("Controls", labels);
        Assert.Contains("Arcade Phys.", labels);
        Assert.Contains("Modern Physics", labels);
        Assert.DoesNotContain("Mouse Options", labels);
        Assert.DoesNotContain("Force Feedback", labels);          // the Controls sheet's
        // the exit confirmation's Yes and No answer the question printed over them
        AllHaveTips(labels.Where(l => !graphics.Contains(l) && l is not ("Yes" or "No")), "the Esc menu");
    }

    [Fact]
    public void EveryDeveloperPanelSettingHasATooltip()
    {
        var missing = DevSettings.All.Concat(DevSettings.WithRow).Concat(DevSettings.Night).Select(e => e.Key).Where(k => string.IsNullOrWhiteSpace(DevTips.Of(k))).ToList();
        Assert.True(missing.Count == 0, $"no tooltip for {string.Join(", ", missing)}");
    }

    /// <summary>The Developer panels' buttons and check boxes, read from their code, each have a tooltip.</summary>
    [Fact]
    public void EveryDeveloperPanelControlHasATooltip()
    {
        foreach (var file in new[] { Path.Combine("game", "Scripts", "Ui", "DevPanel.cs"), Path.Combine("game", "Scripts", "Shell", "ShellDevPanel.cs") })
        {
            var src = File.ReadAllText(RepoFile(file));
            var labels = Regex.Matches(src, @"Button\(""([^""]+)""").Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(src, @"new CheckBox \{ Text = ""([^""]+)""").Select(m => m.Groups[1].Value)).Distinct().ToList();
            Assert.NotEmpty(labels);
            var missing = labels.Where(l => string.IsNullOrWhiteSpace(DevTips.Control(l))).ToList();
            Assert.True(missing.Count == 0, $"{file}: no tooltip for {string.Join(", ", missing)}");
        }
    }

    /// <summary>Every tooltip is plain text: printable ASCII, no config key syntax shown to the player.</summary>
    [Fact]
    public void TheTooltipsArePlainText()
    {
        foreach (var l in MenuTips.Labels)
        {
            var t = MenuTips.Of(l)!;
            Assert.All(t, c => Assert.InRange(c, ' ', '~'));
            Assert.DoesNotContain("=", t);
        }
    }
}
