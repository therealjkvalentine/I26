using System.Text.RegularExpressions;
using I76.Game.Shell;
using I76.Game.Ui;
using I76.Sim.Missions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Play Options' Smart A.I. and Solid Cliffs rows (the fork's): on the main menu's list and the Esc menu's pad, Off / On\* (Smart A.I.
/// Custom\* for some behaviours, the Developer panel's check boxes), [port] smart_ai and solid_cliffs round trips, the tooltips.
/// </summary>
public class SmartAiRowTests
{
    private static string RepoFile(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "I76.sln"))) return Path.Combine(d.FullName, rel);
        throw new FileNotFoundException(rel);
    }

    [Fact]
    public void BothRowsAreOnTheMainMenusList()
    {
        Assert.Contains("Smart A.I.", PlayOptionsScreen.PortRows);
        Assert.Contains("Solid Cliffs", PlayOptionsScreen.PortRows);
        Assert.Contains("Smart A.I.", PlayOptionsScreen.ListRows(nitro: true));
        Assert.Contains("Solid Cliffs", PlayOptionsScreen.ListRows(nitro: false));
    }

    /// <summary>The Esc menu's Play Options pad, read from its code: each row a Cycle setting under the shell's label, with the shell's values.</summary>
    [Fact]
    public void BothRowsAreOnTheEscPad()
    {
        var src = File.ReadAllText(RepoFile(Path.Combine("game", "Scripts", "Ui", "EscapeMenu.cs")));
        Assert.Matches(new Regex(@"_play\.Items\.Add\(Setting\(Cycle, ""Smart A\.I\."", \d+, 2, ShellSettings\.SmartAiRowNames"), src);
        Assert.Matches(new Regex(@"_play\.Items\.Add\(Setting\(Cycle, ""Solid Cliffs"", \d+, 2, ShellSettings\.SolidCliffsRowNames"), src);
        Assert.Equal(PlayOptionsScreen.SmartAiRow, "Smart A.I.");
        Assert.Equal(PlayOptionsScreen.SolidCliffsRow, "Solid Cliffs");
    }

    [Fact]
    public void TheValuesMarkThePorts()
    {
        Assert.Equal(["Off", "On*", "Custom*"], ShellSettings.SmartAiRowNames);
        Assert.Equal(["Off", "On*"], ShellSettings.SolidCliffsRowNames);
    }

    [Fact]
    public void AnyPartialSetShowsCustom()
    {
        Assert.Equal(0, ShellSettings.SmartAiRowIndex(SmartAi.None));
        Assert.Equal(1, ShellSettings.SmartAiRowIndex(SmartAi.All));
        // any_target is no behaviour: alone it is Off, with every behaviour (the rule's first form) Custom
        Assert.Equal(0, ShellSettings.SmartAiRowIndex(SmartAi.AnyTarget));
        Assert.Equal(2, ShellSettings.SmartAiRowIndex(SmartAi.All | SmartAi.AnyTarget));
        foreach (var (_, bit) in SmartAiNames.Bits.Where(b => (b.Bit & SmartAi.All) != 0))
        {
            Assert.Equal(2, ShellSettings.SmartAiRowIndex(bit));
            Assert.Equal(2, ShellSettings.SmartAiRowIndex(SmartAi.All & ~bit));
        }
    }

    [Fact]
    public void AClickStepsOffToOnAndOnOrCustomToOff()
    {
        Assert.Equal(SmartAi.All, ShellSettings.SmartAiRowNext(0));
        Assert.Equal(SmartAi.None, ShellSettings.SmartAiRowNext(1));
        Assert.Equal(SmartAi.None, ShellSettings.SmartAiRowNext(2));
    }

    private static ShellSettings RoundTrip(ShellSettings s) => ShellSettings.Read(s.Write(null));

    [Fact]
    public void TheRowWritesTheKeyAndItReadsBack()
    {
        var s = new ShellSettings();
        s.SetSmartAi(SmartAi.All);
        Assert.Equal("all", (string)s.Write(null).GetValue("port", "smart_ai"));
        Assert.Equal(SmartAi.All, RoundTrip(s).SmartAi);
        s.SetSmartAi(SmartAi.None);
        Assert.False(s.Write(null).HasSectionKey("port", "smart_ai"));
        Assert.Equal(SmartAi.None, RoundTrip(s).SmartAi);
        s.SetSmartAi(SmartAi.GunLay | SmartAi.Unstick);
        Assert.Equal("gun_lay,unstick", (string)s.Write(null).GetValue("port", "smart_ai"));
        var back = RoundTrip(s);
        Assert.Equal(SmartAi.GunLay | SmartAi.Unstick, back.SmartAi);
        Assert.Equal("Custom*", ShellSettings.SmartAiRowNames[ShellSettings.SmartAiRowIndex(back.SmartAi)]);
        // the Developer panel's any_target box with every behaviour: the rule's first form, "everything"
        s.SetSmartAi(SmartAi.All | SmartAi.AnyTarget);
        Assert.Equal("everything", (string)s.Write(null).GetValue("port", "smart_ai"));
        Assert.Equal(SmartAi.All | SmartAi.AnyTarget, RoundTrip(s).SmartAi);
        s.SetSmartAi(SmartAi.GunLay | SmartAi.AnyTarget);
        Assert.Equal(SmartAi.GunLay | SmartAi.AnyTarget, RoundTrip(s).SmartAi);
    }

    /// <summary>The key as written by hand before the row: "all", names, groups and "off" read as they did.</summary>
    [Theory]
    [InlineData("all", 1)]
    [InlineData("ALL", 1)]
    [InlineData("off", 0)]
    [InlineData("", 0)]
    [InlineData("t", 2)]
    [InlineData("gun_lay,settle", 2)]
    [InlineData("t n g d", 1)]
    [InlineData("511", 1)]
    [InlineData("everything", 2)]
    [InlineData("any_target", 0)]
    public void AKeyWrittenByHandShowsOnTheRow(string word, int index)
    {
        var f = new ShellSettings().Write(null);
        f.SetValue("port", "smart_ai", word);
        var s = ShellSettings.Read(f);
        Assert.Equal(index, ShellSettings.SmartAiRowIndex(s.SmartAi));
        Assert.Equal(s.SmartAi, RoundTrip(s).SmartAi);
    }

    [Fact]
    public void SolidCliffsRoundTrips()
    {
        var s = new ShellSettings { SolidCliffs = true };
        Assert.True(RoundTrip(s).SolidCliffs);
        s.SolidCliffs = false;
        Assert.False(RoundTrip(s).SolidCliffs);
    }

    /// <summary>The Developer panel: a check box a behaviour, each with its tip; Solid Cliffs a key with a row now, smart_ai no longer set by hand only.</summary>
    [Fact]
    public void TheDeveloperPanelHasEveryBehaviour()
    {
        foreach (var (name, _) in SmartAiNames.Bits) Assert.False(string.IsNullOrWhiteSpace(DevTips.SmartAiBit(name)), name);
        Assert.DoesNotContain("smart_ai", DevSettings.HandSetOnly);
        Assert.Single(DevSettings.WithRow, e => e.Key == "solid_cliffs");
        Assert.DoesNotContain(DevSettings.All, e => e.Key == "solid_cliffs");
        var s = new ShellSettings();
        s.SetSmartAi(SmartAi.Settle);
        Assert.Equal("smart_ai = settle (next mission)", DevSettings.Line(DevSettings.SmartAiKey, s));
    }

    /// <summary>
    /// The tooltips say what the row does, that it is the fork's and when it applies, and fit: no longer than the Turrets row's,
    /// the longest of Play Options' tips, which the box shows whole at 640x480.
    /// </summary>
    [Fact]
    public void TheTooltipsSayItAndFit()
    {
        int longest = MenuTips.Of("Turrets")!.Length;
        foreach (var row in new[] { "Smart A.I.", "Solid Cliffs" })
        {
            var t = MenuTips.Of(row);
            Assert.NotNull(t);
            Assert.StartsWith(row + ":", t, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fork's own", t);
            Assert.Contains("next mission start", t);
            Assert.Contains("host's", t);
            Assert.InRange(t!.Length, 60, longest);
        }
        Assert.Contains("Custom", MenuTips.Of("Smart A.I."));
    }
}
