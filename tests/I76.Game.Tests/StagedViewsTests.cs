using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The views a mission makes a step at a time (Staged: the combat and damage views, the cockpit, the HUD, the drop shadows, the
/// modern effects) are in the tree while their steps run, frames drawn between them when the tree is not paused (a command-line
/// start, the 1997 look's load behind the card, a melee): their per-frame code ran on what was not made yet and threw (the
/// cockpit's specials panel, the combat view's fire drops, caltrops and smoke). Read from the source: a node cannot be made here.
/// </summary>
public class StagedViewsTests
{
    private static string RepoFile(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "I76.sln"))) return Path.Combine(d.FullName, rel);
        throw new FileNotFoundException(rel);
    }

    private static IEnumerable<string> Scripts() =>
        Directory.EnumerateFiles(RepoFile(Path.Combine("game", "Scripts")), "*.cs", SearchOption.AllDirectories);

    /// <summary>The statements of a method's body (its text between the braces that open after the signature), comments dropped.</summary>
    private static List<string> Body(string src, string signature)
    {
        int at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        int open = src.IndexOf('{', at), depth = 0, end = open;
        for (; end < src.Length; end++)
        {
            if (src[end] == '{') depth++;
            else if (src[end] == '}' && --depth == 0) break;
        }
        return src[(open + 1)..end].Split('\n')
            .Select(l => Regex.Replace(l, @"//.*$", "").Trim())
            .Where(l => l.Length > 0).ToList();
    }

    [Fact]
    public void AStagedViewIsNotProcessedUntilItsStepsHaveMadeIt()
    {
        var staged = Scripts().Where(f => File.ReadAllText(f).Contains("public bool Staged { get; init; }")).ToList();
        Assert.True(staged.Count >= 6, string.Join(", ", staged.Select(Path.GetFileName)));
        foreach (var f in staged)
        {
            var body = Body(File.ReadAllText(f), "public IEnumerable<object?> ReadySteps()");
            Assert.True(body[0] == "SetProcess(false);", $"{Path.GetFileName(f)}: ReadySteps starts with {body[0]}");
            var last = body.Last(l => l != "yield return null;");
            Assert.True(last == "SetProcess(true);", $"{Path.GetFileName(f)}: ReadySteps ends with {last}");
            Assert.DoesNotContain(body, l => l.Contains("yield break"));
        }
    }

    [Fact]
    public void EveryCallerOfThePlayersViewsRunsTheHudsSteps()
    {
        // the network game's build added the staged HUD and never ran its steps: no crosshair, brackets or CB line, and a throw a frame
        int calls = 0;
        foreach (var f in Scripts())
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"AddPlayerViews\([^)]*\)(?<after>[^;\n]*)"))
            {
                if (m.Value.Contains("SimWorld world")) continue;   // the definition
                calls++;
                Assert.True(m.Groups["after"].Value.StartsWith(".ReadySteps()"), $"{Path.GetFileName(f)}: {m.Value}");
            }
        Assert.True(calls >= 2);
    }

    [Fact]
    public void TheShellStopsItsFrameOnceALaunchHasTakenItOutOfTheTree()
    {
        // a launch reloads the scene in the screen's tick, which takes the shell out of the tree at once: the tooltip's tracking
        // after it asked a viewport there was none of
        var body = Body(File.ReadAllText(RepoFile(Path.Combine("game", "Scripts", "Shell", "Shell.cs"))), "public override void _Process(double delta)");
        int tick = body.FindIndex(l => l.StartsWith("try { ticked?.Tick(); }"));
        int stop = body.IndexOf("if (!IsInsideTree()) return;");
        int tip = body.IndexOf("TrackTip(delta);");
        Assert.True(tick >= 0 && stop > tick && tip > stop, $"tick {tick}, stop {stop}, tip {tip}");
    }
}
