using I76.Formats.Video;
using I76.Game.Shell;
using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>[port] movie_decode: an upscaled .ogv decoded on the movie player's own thread (the default) or by the engine's player, with its Developer row and the fallback without the native library.</summary>
public class MovieDecodeKeyTests
{
    [Theory]
    [InlineData(null, MovieDecodeMode.Thread)]
    [InlineData("", MovieDecodeMode.Thread)]
    [InlineData("thread", MovieDecodeMode.Thread)]
    [InlineData(" Engine ", MovieDecodeMode.Engine)]
    [InlineData("ENGINE", MovieDecodeMode.Engine)]
    [InlineData("ffmpeg", MovieDecodeMode.Thread)]
    public void TheKeyReadsThreadOrEngineAndAnythingElseAsTheDefault(string? text, MovieDecodeMode read) =>
        Assert.Equal(read, MovieDecode.Parse(text));

    [Fact]
    public void TheDefaultIsThreadAndOnlyEngineIsWritten()
    {
        var s = new ShellSettings();
        Assert.Equal(MovieDecodeMode.Thread, s.MovieDecode);
        Assert.False(s.Write(null).HasSectionKey("port", "movie_decode"));
        s.MovieDecode = MovieDecodeMode.Engine;
        var file = s.Write(null);
        Assert.Equal("engine", file.GetValue("port", "movie_decode").ToString());
        Assert.Equal(MovieDecodeMode.Engine, ShellSettings.Read(file).MovieDecode);
        // the fork's file from before the key: the default
        Assert.Equal(MovieDecodeMode.Thread, ShellSettings.Read(ShellFileCompatTests.Parse(ShellFileCompatTests.ForkMainFile)).MovieDecode);
    }

    [Fact]
    public void WithoutTheLibraryTheEnginesPlayerPlaysIt()
    {
        Assert.Equal(MovieDecodeMode.Engine, MovieDecode.Choose(MovieDecodeMode.Thread, null, () => false));
        Assert.Equal(MovieDecodeMode.Thread, MovieDecode.Choose(MovieDecodeMode.Thread, null, () => true));
    }

    [Fact]
    public void TheEnginesChoiceNeverLooksForTheLibrary()
    {
        bool looked = false;
        Assert.Equal(MovieDecodeMode.Engine, MovieDecode.Choose(MovieDecodeMode.Engine, null, () => looked = true));
        Assert.False(looked);
        Assert.Equal(MovieDecodeMode.Engine, MovieDecode.Choose(MovieDecodeMode.Thread, "engine", () => looked = true));
        Assert.False(looked);
    }

    [Theory]
    [InlineData(MovieDecodeMode.Engine, "thread", MovieDecodeMode.Thread)]
    [InlineData(MovieDecodeMode.Thread, "engine", MovieDecodeMode.Engine)]
    [InlineData(MovieDecodeMode.Engine, "", MovieDecodeMode.Engine)]
    [InlineData(MovieDecodeMode.Engine, "bogus", MovieDecodeMode.Engine)]   // a hook it does not know leaves the setting
    public void TheHookOverridesTheSetting(MovieDecodeMode setting, string hook, MovieDecodeMode chosen) =>
        Assert.Equal(chosen, MovieDecode.Choose(setting, hook, () => true));

    [Fact]
    public void TheRowOffersThreadAndEngineAndSetsTheKey()
    {
        var e = Assert.Single(DevSettings.All, d => d.Key == "movie_decode");
        Assert.Equal(DevWhen.NextScreen, e.When);
        Assert.Equal(new[] { "thread*", "engine" }, e.Values);
        var s = new ShellSettings();
        Assert.Equal("thread*", e.Values[e.Get(s)]);
        e.Set(s, 1);
        Assert.Equal(MovieDecodeMode.Engine, s.MovieDecode);
        Assert.Contains("engine", DevTips.Of("movie_decode"));
    }

    [Fact]
    public void TheLibraryIsLookedForWhereTheGameLoadsNativeLibrariesFrom()
    {
        // (beside the executable for an export; native/<platform>/ under the project for a run from the editor or a checkout)
        Assert.Equal(OperatingSystem.IsWindows() ? "i26ogv.dll" : OperatingSystem.IsMacOS() ? "libi26ogv.dylib" : "libi26ogv.so", OgvLibrary.FileName);
        Assert.StartsWith(OperatingSystem.IsWindows() ? "win-" : OperatingSystem.IsMacOS() ? "osx-" : "linux-", OgvLibrary.Platform);
    }
}
