using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The user folder's movies/ file that stands in for an install's Smacker movie (MovieReplacement).</summary>
public class MovieReplacementTests
{
    private static string Dir(params string[] files)
    {
        var d = Path.Combine(Path.GetTempPath(), "i26-movies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        foreach (var f in files) File.WriteAllBytes(Path.Combine(d, f), []);
        return d;
    }

    [Fact]
    public void TheStemMatchesInAnyCase()
    {
        var d = Dir("int01f01.OGV", "OUT01F01.ogv");
        try
        {
            Assert.Equal("int01f01.OGV", Path.GetFileName(MovieReplacement.Find(d, "/game/smk/INT01F01.SMK")));
            Assert.Equal("OUT01F01.ogv", Path.GetFileName(MovieReplacement.Find(d, "out01f01.smk")));
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void OnlyAnOgvOfThatStemReplacesIt()
    {
        var d = Dir("INT01F01.mp4", "INT01F01.png", "INT01F010.ogv", "INT02F01.ogv");
        try { Assert.Null(MovieReplacement.Find(d, "INT01F01.SMK")); }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void NoFolderNoReplacement() => Assert.Null(MovieReplacement.Find(Path.Combine(Path.GetTempPath(), "i26-no-such-folder-" + Guid.NewGuid().ToString("N")), "INT01F01.SMK"));
}
