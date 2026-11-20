using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The bookmark lists' names: a typed name as typed, one saved without a name by its scene's title (docs/deviations.md "Bookmark names").</summary>
public class BookmarkNamesTests
{
    private static string Row(Bookmarks.Entry e) => Bookmarks.Row(e, s => s.Length * 6, 1000);

    /// <summary>The rows bookmarks.cfg keeps for a Save Bookmark with the field left empty (any build so far, and the original's): mission, "", state.</summary>
    [Theory]
    [InlineData(2, 0, "Scene 2. Levelland School")]
    [InlineData(3, 0, "Scene 3. Bar-D")]
    [InlineData(10, 0, "Scene 10. Van to Tatum")]
    [InlineData(14, 0, "Scene 14. Escort the Van")]
    [InlineData(4, 1, "Scene 5. Save Taurus")]       // saved after a win: the next scene's, the one it plays
    [InlineData(17, 0, "Scene 17. Malocchio")]
    public void AnUnnamedBookmarkReadsAsItsScene(int mission, int state, string row) =>
        Assert.Equal(row, Row(new Bookmarks.Entry(mission, "", $"save{mission:000}", state, false)));

    [Fact] public void ATypedNameIsKeptAsTyped()
    {
        Assert.Equal("Scene 3. before the bar", Row(new Bookmarks.Entry(3, "before the bar", "save000", 0, false)));
        Assert.Equal("Scene 5. Checkpoint", Row(new Bookmarks.Entry(5, "Checkpoint", "save001", 0, false)));   // typed, not the escape menu's
        Assert.Equal(" x ", new Bookmarks.Entry(5, " x ", "save002", 0, false).Shown);
    }

    [Fact] public void OnlyBlanksCountAsNoName() => Assert.Equal("Bar-D", Bookmarks.Title(3, "   "));

    [Fact] public void ACheckpointBookmarkCarriesItsScenesTitle() =>
        Assert.Equal("Scene 5. Save Taurus - Checkpoint", Row(new Bookmarks.Entry(5, CheckpointBookmarks.Name, "save003", 0, false, Checkpoint: true)));

    [Fact] public void AllScenesBookmarksReadTheSameUnderTheFormerName()
    {
        Assert.Equal("Scene 3. Bar-D", Row(new Bookmarks.Entry(3, "Bar-D", "save000", 0, false)));
        Assert.Equal("Scene 3. Bar-D", Row(new Bookmarks.Entry(3, "The Bar-D", "save001", 0, false)));
        Assert.Equal("The Bar-D", Bookmarks.Title(4, "The Bar-D"));   // another scene's former name is a name like any other
    }

    [Theory]
    [InlineData(0)]
    [InlineData(18)]
    public void AScenePastTheTripKeepsItsName(int scene)
    {
        Assert.Equal("", Bookmarks.Title(scene, ""));
        Assert.Equal("mine", Bookmarks.Title(scene, "mine"));
    }

    /// <summary>The name is never written: the entry keeps what was saved, so the file reads the same in any build.</summary>
    [Fact] public void TheStoredNameIsNotChanged()
    {
        var e = new Bookmarks.Entry(6, "", "save005", 0, false);
        Assert.Equal("Out of Gas", e.Shown);
        Assert.Equal("", e.Name);
    }

    [Fact] public void ALongRowIsCutToTheListWithTwoDots()
    {
        Func<string, int> six = s => s.Length * 6;
        var e = new Bookmarks.Entry(2, "", "save000", 0, false, Checkpoint: false);
        Assert.Equal("Scene 2. Levelland School", Bookmarks.Row(e, six, 1000));
        Assert.Equal("Scene 2. Levelland..", Bookmarks.Row(new Bookmarks.Entry(2, "Levelland School again", "save001", 0, false), six, 120));
        // a checkpoint row keeps its " - Checkpoint" and cuts the title
        var cut = Bookmarks.Row(new Bookmarks.Entry(2, CheckpointBookmarks.Name, "save001", 0, false, Checkpoint: true), six, 200);
        Assert.Equal("Scene 2. Levelland.. - Checkpoint", cut);
        Assert.True(six(cut) <= 200);
        Assert.Equal("Levelland School - Checkpoint", new Bookmarks.Entry(2, CheckpointBookmarks.Name, "save001", 0, false, Checkpoint: true).Fitted("", six, 1000));
        Assert.Equal("Scene 9. ", Bookmarks.Fit("Scene 9. ", six, 120));
        Assert.Equal("Sce..", Bookmarks.Fit("Scene 9. x", six, 30));
    }

    /// <summary>Every scene's longest row (its checkpoint bookmark) fits the list as it is drawn, cut or not.</summary>
    [Fact] public void EveryScenesRowFitsTheRoom()
    {
        Func<string, int> wide = s => s.Length * 9;
        for (int n = 1; n <= 17; n++)
            Assert.True(wide(Bookmarks.Row(new Bookmarks.Entry(n, CheckpointBookmarks.Name, "save000", 0, false, Checkpoint: true), wide, BookmarkScreen.RowRoom)) <= BookmarkScreen.RowRoom);
    }
}
