using System.Text.RegularExpressions;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// A camera's compositor is shared (CompositorShare): the smoke layer once gave the rear mirror's camera a compositor of its own
/// effect alone, which dropped Mirror Rate Smooth's depth effect (MirrorDepth), and the glass re-projected from the engine's
/// placeholder texture showed flat brown blocks. Without the engine.
/// </summary>
public class CompositorShareTests
{
    private static string RepoFile(string rel)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "I76.sln"))) return Path.Combine(d.FullName, rel);
        throw new FileNotFoundException(rel);
    }

    /// <summary>The mirror camera's frames as they run: the depth effect put on, then the smoke layer's each frame; both stay, in either order.</summary>
    [Fact]
    public void TheDepthAndTheSmokeLayerBothStayOnTheMirrorCamera()
    {
        object depth = new(), smoke = new();
        foreach (var first in new[] { depth, smoke })
        {
            var list = new List<object>();
            CompositorShare.Edit(list, first, true);
            for (int frame = 0; frame < 3; frame++)
            {
                CompositorShare.Edit(list, smoke, true);
                CompositorShare.Edit(list, depth, true);
            }
            Assert.Equal(2, list.Count);
            Assert.Contains(depth, list);
            Assert.Contains(smoke, list);
        }
    }

    /// <summary>Mirror Rate off Smooth: the depth effect taken out, the smoke layer's kept; nothing changed when nothing is to change.</summary>
    [Fact]
    public void TakingOneEffectOutKeepsTheOthers()
    {
        object depth = new(), smoke = new(), heat = new();
        var list = new List<object> { smoke, depth, heat };
        Assert.True(CompositorShare.Edit(list, depth, false));
        Assert.Equal([smoke, heat], list);
        Assert.False(CompositorShare.Edit(list, depth, false));
        Assert.False(CompositorShare.Edit(list, smoke, true));
        Assert.Equal([smoke, heat], list);
    }

    /// <summary>The depth only once its effect runs on the camera and has made its texture; otherwise the turn alone.</summary>
    [Fact]
    public void TheWarpUsesTheDepthOnlyWhenItIsThere()
    {
        Assert.True(MirrorWarp.DepthUsable(attached: true, made: true));
        Assert.False(MirrorWarp.DepthUsable(attached: true, made: false));
        Assert.False(MirrorWarp.DepthUsable(attached: false, made: true));
        Assert.False(MirrorWarp.DepthUsable(attached: false, made: false));
    }

    /// <summary>No script gives a camera a compositor of its own over what is there: every effect goes through CompositorShare.</summary>
    [Fact]
    public void NoScriptReplacesACamerasCompositor()
    {
        var dir = RepoFile(Path.Combine("game", "Scripts"));
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "CompositorShare.cs") continue;
            var src = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(src, @"\.Compositor\s*=(?!=)"))
                found.Add($"{Path.GetFileName(file)}: {src[..m.Index].Count(c => c == '\n') + 1}");
        }
        Assert.Empty(found);
    }
}
