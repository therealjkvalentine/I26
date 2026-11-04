using I76.Game.Boot;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The briefing played while the mission builds ([port] briefing_while_loading): when it applies, and its default.</summary>
public class BriefingOverlapTests
{
    private static bool Plan(bool setting = true, bool look1997 = false, string? briefing = "int01f01.smk", bool found = true,
        bool skip = false, bool network = false, bool editor = false, bool retry = false)
        => BriefingOverlap.Plan(setting, look1997, briefing, found, skip, network, editor, retry);

    [Fact]
    public void AModernLookStartWithABriefingBuildsUnderIt() => Assert.True(Plan());

    [Fact]
    public void ItIsOnUnlessTheFileSaysOff() => Assert.True(new ShellSettings().BriefingWhileLoading);

    /// <summary>The original's order (the build, then the briefing) everywhere else.</summary>
    [Fact]
    public void EveryOtherStartKeepsTheOriginalsOrder()
    {
        Assert.False(Plan(setting: false));
        Assert.False(Plan(look1997: true));
        Assert.False(Plan(briefing: null));
        Assert.False(Plan(briefing: ""));
        Assert.False(Plan(found: false));     // a briefing named but not in smk/: nothing to hide the build under
        Assert.False(Plan(skip: true));       // --skip-movies
        Assert.False(Plan(network: true));    // co-op plays its briefing before hosting or joining
        Assert.False(Plan(editor: true));
        Assert.False(Plan(retry: true));      // a checkpoint's retry has no briefing
    }
}
