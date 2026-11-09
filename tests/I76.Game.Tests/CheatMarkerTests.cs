using System.Text.RegularExpressions;
using I76.Game.Shell;
using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The cheats marker: the original's part, set by any of Unlim. Ammo, Armor or Chas. Rein. at a read of the Play Options and
/// kept until the end menu turns it off; and the port's, a Developer panel action on the attempt's simulation, gone when a
/// mission starts fresh.
/// </summary>
public class CheatMarkerTests
{
    [Fact] public void ACheatOnAtMissionStartMarksTheRunAndTurningItOffInTheShellDoesNotUnmarkIt()
    {
        CheatMarker.Clear();
        CheatMarker.Read(0x01 | 0x02 | 0x20);      // cornering, no salvage, wiper: not cheats
        Assert.False(CheatMarker.Set);
        foreach (int bit in new[] { 0x04, 0x08, 0x10 })
        {
            CheatMarker.Clear();
            CheatMarker.Read(bit);
            Assert.True(CheatMarker.Set);
        }
        CheatMarker.Read(0);                         // the next mission, the cheat turned off in the shell
        Assert.True(CheatMarker.Set);
        CheatMarker.NewAttempt();                    // a fresh start: the original's marker stays
        Assert.True(CheatMarker.Set);
        CheatMarker.Clear();                         // Turn off Cheater Options
        Assert.False(CheatMarker.Set);
    }

    /// <summary>(b) and (c): a panel action marks this attempt only; a mission started fresh starts clear, a retry (no new attempt) keeps it.</summary>
    [Fact] public void APanelActionMarksThisAttemptAloneAndAFreshStartClearsIt()
    {
        CheatMarker.Clear();
        CheatMarker.Mark();
        Assert.True(CheatMarker.Set);
        CheatMarker.Read(0);                         // the in-mission options closed, no cheat on: still this attempt's
        Assert.True(CheatMarker.Set);
        CheatMarker.NewAttempt();                    // Replay, a shell launch, the panel's Start
        CheatMarker.Read(0);
        Assert.False(CheatMarker.Set);
        CheatMarker.Mark();
        CheatMarker.Clear();                         // Turn off Cheater Options clears the panel's part too
        Assert.False(CheatMarker.Set);
    }

    /// <summary>(a): a change of a Settings row on the panel sets and saves the setting and nothing else; the marker is not touched.</summary>
    [Fact] public void ASettingsRowOfThePanelIsNotACheat()
    {
        CheatMarker.Clear();
        foreach (var e in DevSettings.All.Concat(DevSettings.WithRow))
        {
            var s = new ShellSettings();
            for (int v = 0; v < e.Values.Length; v++) e.Set(s, v);
            Assert.False(CheatMarker.Set);
        }
    }

    /// <summary>
    /// Which of the panel's actions mark (the ones that change the attempt's simulation) and which never do, read from the host's
    /// source: the Settings, Store, the kept places, the Signals and Start are a developer's tools, not cheats.
    /// </summary>
    [Fact] public void TheActionsThatMarkAreTheOnesThatChangeTheSimulation()
    {
        var src = File.ReadAllText(RepoFile(Path.Combine("game", "Scripts", "Boot", "MainDev.cs")));
        // each public member of the panel's host, from its header to the next's
        var heads = Regex.Matches(src, @"^    public [^\n]*?\b(Dev\w+)\s*(\(|=>|\{|\n)", RegexOptions.Multiline).ToList();
        Assert.True(heads.Count > 20, "the panel's host members");
        var marks = new Dictionary<string, bool>();
        for (int i = 0; i < heads.Count; i++)
        {
            int from = heads[i].Index, to = i + 1 < heads.Count ? heads[i + 1].Index : src.Length;
            // a call of the marking helper, Cheat("...") or Cheat($"..."), in the member (and the private ones that follow it)
            marks[heads[i].Groups[1].Value] = Regex.IsMatch(src[from..to], @"\bCheat\(\$?""");
        }
        foreach (var cheat in new[] { "DevTeleport", "DevRepair", "DevGod", "DevBring", "DevSwap", "DevRefill", "DevRestore", "DevFrozen", "DevStep", "DevScale" })
            Assert.True(marks[cheat], cheat);
        foreach (var tool in new[] { "DevSettingChanged", "DevStore", "DevKeep", "DevForget", "DevStart", "DevSignalsShown", "DevStatus", "DevPoints", "DevMounts", "DevClose" })
            Assert.False(marks[tool], tool);
    }

    private static string RepoFile(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "I76.sln"))) return Path.Combine(d.FullName, rel);
        throw new FileNotFoundException(rel);
    }
}
