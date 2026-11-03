using I76.Formats.Mission;
using I76.Formats.Vfs;
using Xunit;

namespace I76.Formats.Tests;

public class MissionTitlesTests
{
    private static void EveryStockMissionHasATitle(GameInstall install)
    {
        // the maps the game ships, not what a player added beside them
        var all = MissionTitles.Stock(install).Where(m => install.FindMission(m.Stem) is { } f && MapFileSet.IsStockMission(f)).ToList();
        Assert.NotEmpty(all);
        foreach (var m in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Title), $"{m.Stem} has no title");
            Assert.EndsWith($"({m.Stem.ToUpperInvariant()})", m.Label);
        }
        Assert.Equal(all.Count, all.Select(m => m.Stem).Distinct().Count());
    }

    [InstallFact]
    public void EveryBaseMissionHasATitleAndTheGroupsComeInTheGamesOrder()
    {
        EveryStockMissionHasATitle(TestInstall.Install);
        var all = MissionTitles.Stock(TestInstall.Install);
        Assert.Equal("Jorczak's Peak (M14)", all.First(m => m.Stem == "m14").Label);
        Assert.Equal("The Crater (M01)", all.First(m => m.Group == MissionTitles.Melee).Label);
        Assert.Equal(MissionTitles.Campaign, all[0].Group);
        Assert.True(all.FindIndex(m => m.Stem == "t02") < all.FindIndex(m => m.Stem == "t10"));
    }

    [NitroFact]
    public void EveryNitroMissionHasATitle()
    {
        var nitro = new GameInstall(NitroFactAttribute.Dir!);
        EveryStockMissionHasATitle(nitro);
        var all = MissionTitles.Stock(nitro);
        Assert.Equal("A New Hope (P01)", all.First(m => m.Stem == "p01").Label);
        Assert.Equal("Evening Stroll (N01)", all.First(m => m.Stem == "n01").Label);
    }

    [InstallFact]
    public void TheBaseCoopListIsTheStoryMissionsInTheGamesOrder()
    {
        var list = MissionTitles.Coop(TestInstall.Install).Select(m => m.Stem).ToList();
        // the campaign (A01, T01-T17), then the scenarios in the shell's own order; no melee arena
        string[] t = [.. Enumerable.Range(1, 17).Select(i => $"t{i:00}")];
        Assert.Equal(["a01", .. t, .. MissionTitles.BaseScenarios.Select(s => s.Msn)], list);
        Assert.Equal("Scene 3: The Bar-D", MissionTitles.Coop(TestInstall.Install).First(m => m.Stem == "t03").Title);
        Assert.Equal("Scene 17: Malocchio", MissionTitles.Coop(TestInstall.Install).First(m => m.Stem == "t17").Title);
        Assert.Equal("Training", MissionTitles.CoopTitle(TestInstall.Install, "a01"));
        Assert.Equal("Oil Well Well", MissionTitles.CoopTitle(TestInstall.Install, "S03"));
        Assert.Equal("M01", MissionTitles.CoopTitle(TestInstall.Install, "m01"));
    }

    [NitroFact]
    public void TheNitroCoopListIsTheCampaignInTheGamesOrder()
    {
        var nitro = new GameInstall(NitroFactAttribute.Dir!);
        var list = MissionTitles.Coop(nitro);
        var stems = list.Select(m => m.Stem).ToList();
        Assert.Equal(20, list.Count);
        Assert.All(stems, s => Assert.Contains(s[0], "bp"));
        Assert.Equal(Enumerable.Range(1, 19).Select(i => $"p{i:00}").Append("b01").Order().ToList(), stems.Order().ToList());
        Assert.Equal("A New Hope", list.First(m => m.Stem == "p01").Title);
    }

    [InstallFact]
    public void ACopyOfAStoryMissionIsKnownAndAnArenaOrEditedScriptIsNot()
    {
        var install = TestInstall.Install;
        // packs for the original put T01-T17 and the scenarios in addon as .lvl under other names (coop01.lvl, auto01.lvl)
        var dir = Path.Combine(Path.GetTempPath(), "i26-coopcopy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string Copy(string stem, string name)
            {
                var to = Path.Combine(dir, name);
                File.Copy(install.Resolve("miss16", stem + ".msn")!, to);
                return to;
            }
            Assert.True(MissionTitles.IsCoopCopy(install, Copy("t05", "coop05.lvl")));
            Assert.True(MissionTitles.IsCoopCopy(install, Copy("s03", "auto01.lvl")));
            Assert.False(MissionTitles.IsCoopCopy(install, Copy("m01", "crater.lvl")));
            // one byte of the script changed: a map of the player's own, kept
            var edited = Copy("t05", "mine.lvl");
            var m = new MissionFile(edited);
            var b = File.ReadAllBytes(edited);
            int at = b.AsSpan().IndexOf(m.FsmPayload().AsSpan()[..64]);
            Assert.True(at > 0);
            b[at + m.FsmPayload().Length - 1] ^= 1;
            File.WriteAllBytes(edited, b);
            Assert.False(MissionTitles.IsCoopCopy(install, edited));
        }
        finally { Directory.Delete(dir, true); }
    }


    [Fact]
    public void TheTripScenesAreNamedByWhatHappensInThem()
    {
        // the port's names (the original shows only "Scene N."); the executable's unshown titles are not used
        Assert.Equal(17, MissionTitles.SceneNames.Length);
        Assert.Equal(17, MissionTitles.SceneNames.Distinct().Count());
        Assert.All(MissionTitles.SceneNames, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal("Scene 4: Dream Race", MissionTitles.TripTitle(4));
        Assert.Equal("Scene 5: Save Taurus", MissionTitles.TripTitle(5));
        Assert.Equal("Scene 1: Meet Taurus", MissionTitles.TripTitle(1));
        Assert.Equal("Scene 17: Malocchio", MissionTitles.TripTitle(17));
        Assert.Equal("Save Taurus", MissionTitles.SceneName(5));
        Assert.Equal("Scene 18", MissionTitles.TripTitle(18));
        Assert.DoesNotContain("Roswell", MissionTitles.SceneNames);
        Assert.DoesNotContain("The Desert War", MissionTitles.SceneNames);
    }

    [InstallFact]
    public void TheCampaignListsTheScenesByTheirNames()
    {
        var all = MissionTitles.Stock(TestInstall.Install);
        Assert.Equal("Training (A01)", all.First(m => m.Stem == "a01").Label);
        Assert.Equal("Scene 5: Save Taurus (T05)", all.First(m => m.Stem == "t05").Label);
        Assert.Equal("Scene 4: Dream Race (T04)", all.First(m => m.Stem == "t04").Label);
    }
}
