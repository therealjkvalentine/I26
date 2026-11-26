using System.Text.RegularExpressions;
using I76.Game.Shell;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Road Detail ([port] road_detail, Graphic Detail > World): off unless the file says otherwise and written only when on; on World under
/// Terrain Shadows; the environment's override for captures; and in the shaders, only the modern terrain reads it, behind the global, so
/// Off costs nothing and the 1997 look never sees it. Without the engine.
/// </summary>
public class RoadDetailTests
{
    private static string Game(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, "game", .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, "game", .. path]));
    }

    [Fact]
    public void ItIsOffInAFreshBlockAndInAFileWithoutTheKey()
    {
        var s = new ShellSettings();
        Assert.False(s.RoadDetail);
        Assert.False(s.Write(null).HasSectionKey("port", "road_detail"));
        Assert.False(ShellSettings.Read(ShellFileCompatTests.Parse(ShellFileCompatTests.ForkMainFile)).RoadDetail);
    }

    [Fact]
    public void OnSurvivesASaveAndALoad()
    {
        var file = new ShellSettings { RoadDetail = true }.Write(null);
        Assert.True((bool)file.GetValue("port", "road_detail"));
        Assert.True(ShellSettings.Read(file).RoadDetail);
    }

    [Fact]
    public void TheRowStepsAndSitsOnWorldUnderTerrainShadows()
    {
        var s = new ShellSettings();
        Assert.Equal(["Off", "On" + ShellSettings.PortMark], RoadDetailRow.Names);
        RoadDetailRow.Step(s); Assert.True(s.RoadDetail); Assert.Equal(1, RoadDetailRow.Index(s));
        RoadDetailRow.Put(s, 0); Assert.False(s.RoadDetail);
        var world = GraphicsPages.Rows[GraphicsPages.World];
        Assert.Equal(Array.IndexOf(world, GraphicsRow.TerrainShadows) + 1, Array.IndexOf(world, GraphicsRow.RoadDetail));
        var on = new ShellSettings { RoadDetail = true };
        GraphicsPages.Reset(on, GraphicsPages.World);
        Assert.False(on.RoadDetail);
        var t = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.RoadDetail, new ShellSettings { RoadDetail = true }, t);
        Assert.True(t.RoadDetail);
        foreach (bool look1997 in (bool[])[false, true]) Assert.NotEmpty(GraphicsHelp.Text(GraphicsRow.RoadDetail, look1997));
    }

    [Theory]
    [InlineData(false, null, 0f)]
    [InlineData(true, null, 1f)]
    [InlineData(false, "1", 1f)]
    [InlineData(true, "0", 0f)]
    [InlineData(true, "yes", 1f)]
    public void TheEnvironmentOverridesTheSettingForACapture(bool setting, string? env, float want)
        => Assert.Equal(want, RoadDetail.Value(setting, env));

    [Fact]
    public void OnlyTheModernTerrainReadsItBehindTheGlobal()
    {
        var s = Game("Shaders", "terrain_modern.gdshaderinc");
        // the one call, behind the global (Off: no read of any of it), and not in the painted lot's variant
        Assert.Single(Regex.Matches(s, @"road_detail\(g_pos"));
        Assert.Matches(@"if \(i76_road_detail > 0\.0\) road_detail\(", s);
        int fn = s.IndexOf("void road_detail(", StringComparison.Ordinal);
        Assert.True(fn > 0);
        int guard = s.LastIndexOf("#ifndef PAINTED_GROUND", fn, StringComparison.Ordinal);
        Assert.True(guard > 0 && s.IndexOf("#endif", guard, StringComparison.Ordinal) > fn);
        // the derivatives come before the function's first branch (uniform control flow)
        int body = s.IndexOf('{', fn), fw = s.IndexOf("fwidth(light)", fn, StringComparison.Ordinal), br = s.IndexOf("if (", body, StringComparison.Ordinal);
        Assert.True(fw > 0 && fw < br);
        // the global is declared, off by default
        Assert.Matches(new Regex(@"i76_road_detail=\{\s*""type"": ""float"",\s*""value"": 0\.0\s*\}"), Game("project.godot"));
        // the 1997 look's shaders never see it
        foreach (var f in new[] { "terrain_faithful.gdshader", "road.gdshader", "outside_1997.gdshaderinc" })
            Assert.DoesNotContain("road_detail", Game("Shaders", f));
    }
}
