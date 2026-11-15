using I76.Formats.Shell;
using I76.Formats.Tests;
using I76.Game.Boot;
using Xunit;
using Xunit.Abstractions;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's Renderer row says what runs: Auto with the driver it picked, a choice that did not start as the fallback, a
/// setting changed for the next start as it is (upstream #198).
/// </summary>
public class RendererRowTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("vulkan", "Auto (Vulkan)")]
    [InlineData("d3d12", "Auto (D3D12)")]
    [InlineData("opengl3", "Auto (OpenGL)")]
    [InlineData("opengl3_angle", "Auto (OpenGL)")]
    [InlineData("metal", "Auto (Metal)")]
    public void AutoSaysWhatItPicked(string driver, string value) =>
        Assert.Equal(value, RenderBackend.RowText(RendererChoice.Auto, RendererChoice.Auto, driver, true).Value);

    [Fact]
    public void AChoiceThatRunsIsItsNameAlone()
    {
        Assert.Equal(("Vulkan", null), RenderBackend.RowText(RendererChoice.Vulkan, RendererChoice.Vulkan, "vulkan", true));
        Assert.Equal(("OpenGL", null), RenderBackend.RowText(RendererChoice.OpenGL, RendererChoice.OpenGL, "opengl3", true));
    }

    [Fact]
    public void AChoiceThatFailedSaysItFellBack()
    {
        var (value, note) = RenderBackend.RowText(RendererChoice.D3D12, RendererChoice.D3D12, "vulkan", true);
        Assert.Equal("Vulkan (fallback)", value);
        Assert.Equal("D3D12 did not start here: the game fell back to Vulkan.", note);
        Assert.Equal("OpenGL (fallback)", RenderBackend.RowText(RendererChoice.Vulkan, RendererChoice.Vulkan, "opengl3", true).Value);
    }

    [Fact]
    public void VulkanOnAMacRunsMetalWithoutAFailure() =>
        Assert.Equal("Vulkan (Metal)", RenderBackend.RowText(RendererChoice.Vulkan, RendererChoice.Vulkan, "metal", false).Value);

    [Fact]
    public void ASettingChangedForTheNextStartShowsAsItIs()
    {
        var (value, note) = RenderBackend.RowText(RendererChoice.OpenGL, RendererChoice.Auto, "vulkan", true);
        Assert.Equal("OpenGL", value);
        Assert.Equal("Running now: Vulkan. OpenGL from the next start.", note);
        // and back to Auto from a start on OpenGL: not "Auto (OpenGL)", which would say what Auto picks
        Assert.Equal("Auto", RenderBackend.RowText(RendererChoice.Auto, RendererChoice.OpenGL, "opengl3", true).Value);
    }

    [Fact]
    public void TheCommandLineChoiceIsNotCalledAFallback()
    {
        var (value, note) = RenderBackend.RowText(RendererChoice.Vulkan, null, "opengl3", true);
        Assert.Equal("Vulkan", value);
        Assert.Equal("Running now: OpenGL. The command line chose it.", note);
        Assert.True(RenderBackend.CommandLineChose(["--windowed", "--rendering-driver", "opengl3", "--", "--mission", "t01"]));
        Assert.True(RenderBackend.CommandLineChose(["--rendering-method=gl_compatibility"]));
        Assert.False(RenderBackend.CommandLineChose(["--windowed", "--", "--rendering-driver"]));
    }

    [Fact]
    public void D3D12WhereItIsNotOfferedIsAuto()
    {
        Assert.Equal("Auto (Vulkan)", RenderBackend.RowText(RendererChoice.D3D12, RendererChoice.Auto, "vulkan", false).Value);
        Assert.Equal(RendererChoice.Auto, RenderBackend.Effective(RendererChoice.D3D12, false));
    }

    [Fact]
    public void AnUnknownDriverLeavesTheSettingAlone() =>
        Assert.Equal(("Auto", null), RenderBackend.RowText(RendererChoice.Auto, RendererChoice.Auto, "", true));

    /// <summary>
    /// Every value the row can show fits between its label and the sheet's rule in the sheet's face (record 70): the row places it
    /// at x 397 or past the label, and a longer one moves left to end at 461 (GraphicsScreen.Row, Item.Right).
    /// </summary>
    [InstallFact]
    public void EveryValueFitsBesideTheLabel()
    {
        var f = ShellDatabase.Open(TestInstall.Install.FindFile("DATABASE.MW2")!).Font(0x46);
        int labelEnd = 200 + f.Measure("Renderer");
        int x = Math.Max(397, labelEnd + 16);
        string[] values = ["Auto", "Vulkan", "D3D12", "OpenGL", "Auto (Vulkan)", "Auto (D3D12)", "Auto (OpenGL)", "Auto (Metal)",
            "Vulkan (fallback)", "D3D12 (fallback)", "OpenGL (fallback)", "Vulkan (Metal)", "D3D12 (Metal)", "OpenGL (Metal)"];
        foreach (var v in values)
        {
            int at = Math.Min(x, 461 - f.Measure(v));
            output.WriteLine($"{v}: {f.Measure(v)} px at x {at} (label ends at {labelEnd})");
            Assert.True(at >= labelEnd + 8, $"\"{v}\" starts at {at}, the label ends at {labelEnd}");
        }
    }
}
