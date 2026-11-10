using Godot;
using I76.Game.Boot;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Graphic Detail's Render Scale and the Anti-Aliasing row's 8x step.</summary>
public class RenderScaleTests
{
    [Fact] public void ScalesRunFrom50To200PercentWith100TheDefault()
    {
        Assert.Equal([50, 67, 75, 85, 100, 150, 200], RenderScale.Percents);
        Assert.Equal(RenderScale.Percents.Length, RenderScale.Names.Length);
        Assert.Equal(1f, RenderScale.Scale(RenderScale.Default));
        Assert.Equal(0.5f, RenderScale.Scale(0));
        Assert.Equal(0.75f, RenderScale.Scale(2));
        Assert.Equal(2f, RenderScale.Scale(6));
        Assert.Equal(2f, RenderScale.Scale(9));   // clamped
        Assert.Equal(0.5f, RenderScale.Scale(-1));
        Assert.Equal(RenderScale.Default, new I76.Game.Shell.ShellSettings().RenderScale);
    }

    [Fact] public void ParseKeepsTheDefaultForAnythingNotOffered()
    {
        Assert.Equal(RenderScale.Default, RenderScale.Parse(null));
        Assert.Equal(RenderScale.Default, RenderScale.Parse(100));
        Assert.Equal(RenderScale.Default, RenderScale.Parse(175));
        Assert.Equal(RenderScale.Default, RenderScale.Parse(0));
        Assert.Equal(0, RenderScale.Parse(50));
        Assert.Equal(2, RenderScale.Parse(75));
        Assert.Equal(5, RenderScale.Parse(150));
        Assert.Equal(6, RenderScale.Parse(200));
    }

    [Fact] public void TheAntiAliasingRowHasEightX()
    {
        Assert.Equal(["Off", "2x", "4x", "8x*"], RenderScale.AntiAliasing);
        Assert.Equal(Viewport.Msaa.Disabled, RenderScale.Msaa(0, false));
        Assert.Equal(Viewport.Msaa.Msaa2X, RenderScale.Msaa(1, false));
        Assert.Equal(Viewport.Msaa.Msaa4X, RenderScale.Msaa(2, false));
        Assert.Equal(Viewport.Msaa.Msaa8X, RenderScale.Msaa(3, false));
    }

    [Fact] public void EightXIsFourXWhereTheRendererHasNoEightX() => Assert.Equal(Viewport.Msaa.Msaa4X, RenderScale.Msaa(3, true));

    /// <summary>The Esc menu's Anti-Aliasing row offers the shell's list: with three names an 8x chosen in the shell showed blank and stepped to 2x.</summary>
    [Fact] public void TheEscRowHasTheShellsList()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "game", "Scripts", "Ui", "EscapeMenu.cs"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var row = File.ReadAllLines(Path.Combine(dir!, "game", "Scripts", "Ui", "EscapeMenu.cs")).Single(l => l.Contains("\"Anti-Aliasing\""));
        Assert.Contains("RenderScale.AntiAliasing.Length, I76.Game.Boot.RenderScale.AntiAliasing", row);
    }

    [Fact] public void TheSupersampledViewIsNoBiggerThanFourK()   // where the card's memory is not known
    {
        Assert.Equal(2f, RenderScale.Capped(2f, new Vector2I(1920, 1080)));     // 3840x2160
        Assert.Equal(1.5f, RenderScale.Capped(1.5f, new Vector2I(2560, 1440)));  // 3840x2160
        Assert.Equal(1.5f, RenderScale.Capped(2f, new Vector2I(2560, 1440)), 3);  // 200 % lowered to 4K's pixels
        Assert.Equal(1f, RenderScale.Capped(2f, new Vector2I(3840, 2160)));      // a 4K desktop: drawn as it is
        Assert.Equal(1f, RenderScale.Capped(2f, new Vector2I(5120, 2880)));      // never under 100 %
        Assert.Equal(1f, RenderScale.Capped(1f, new Vector2I(1280, 720)));
        Assert.Equal(0.5f, RenderScale.Capped(0.5f, new Vector2I(5120, 2880)));  // under 100 % as chosen
        Assert.Equal(2f, RenderScale.Capped(2f, new Vector2I(0, 0)));           // no size yet: as chosen
    }

    [Fact] public void TheViewsBuffersGrowWithItsPixelsAndSamples()
    {
        var w = new Vector2I(3440, 1440);
        long one = RenderScale.TargetBytes(w, 1f, 1);
        Assert.Equal((long)(3440L * 1440 * RenderScale.ResolvedBytes), one);   // at 100 % no scaled copy
        Assert.True(RenderScale.TargetBytes(w, 1f, 8) > RenderScale.TargetBytes(w, 1f, 4));
        // 200 %: four times the drawn pixels, and the window-sized result it is scaled into
        Assert.Equal(4 * RenderScale.TargetBytes(w, 1f, 8) + (long)(3440L * 1440 * RenderScale.WindowBytes), RenderScale.TargetBytes(w, 2f, 8), 1e-6);
        Assert.Equal(0, RenderScale.TargetBytes(new Vector2I(0, 0), 2f, 8));
        Assert.Equal(1, RenderScale.Samples(Viewport.Msaa.Disabled));
        Assert.Equal(8, RenderScale.Samples(RenderScale.Msaa(3, false)));
        Assert.Equal(4, RenderScale.Samples(RenderScale.Msaa(3, true)));
    }

    [Fact] public void TheScaleIsLoweredOnlyAsFarAsTheBuffersMustFit()
    {
        var w = new Vector2I(3440, 1440);
        long need = RenderScale.TargetBytes(w, 2f, 8);
        Assert.Equal(2f, RenderScale.Capped(2f, w, 8, need));          // fits: as chosen
        Assert.Equal(2f, RenderScale.Capped(2f, w, 8, long.MaxValue));
        float held = RenderScale.Capped(2f, w, 8, need / 2);
        Assert.InRange(held, 1.3f, 1.5f);
        Assert.True(RenderScale.TargetBytes(w, held, 8) <= need / 2);   // the held scale fits
        Assert.True(RenderScale.TargetBytes(w, held + 0.01f, 8) > need / 2);   // and is the most that does, to the percent
        Assert.Equal(System.MathF.Round(held * 100) / 100, held);
        Assert.Equal(1f, RenderScale.Capped(2f, w, 8, 0));             // never under 100 %
        Assert.Equal(1f, RenderScale.Capped(1f, w, 8, 0));
        Assert.Equal(0.67f, RenderScale.Capped(0.67f, w, 8, 0));       // under 100 % as chosen
        Assert.Equal(2f, RenderScale.Capped(2f, new Vector2I(0, 0), 8, 0));
        // fewer samples, more scale for the same memory
        Assert.True(RenderScale.Capped(2f, w, 2, need / 2) > held);
    }

    [Fact] public void TheBudgetIsTheCardsShareLessWhatTheGameHoldsBesidesTheView()
    {
        // an 11 GiB card: 3/4 of it, less 3 GiB of textures and meshes (the engine's 3.5 GiB less the view's own 0.5 GiB)
        Assert.Equal((long)(11264 * RenderScale.MemoryShare * 1048576) - (3L << 30), RenderScale.Budget(null, 11264, 7L << 29, 1L << 29));
        Assert.Equal(0, RenderScale.Budget(null, 4096, 6L << 30, 0));            // full already: nothing (then 100 %)
        Assert.Null(RenderScale.Budget(null, null, 1L << 30, 0));                // not known: the 3840x2160 rule
        Assert.Null(RenderScale.Budget(null, 0, 1L << 30, 0));
        Assert.Equal(2048L << 20, RenderScale.Budget(2048, null, 1L << 30, 0));   // [port] render_scale_cap=2048 over everything
        Assert.Equal(2048L << 20, RenderScale.Budget(2048, 11264, 9L << 30, 0));
    }

    /// <summary>The owner's desktop (3440x1440, an 11 GiB card): every step at every Anti-Aliasing is drawn as chosen with the textures it measured.</summary>
    [Fact] public void AnElevenGigabyteCardAt3440x1440DrawsEveryStep()
    {
        var w = new Vector2I(3440, 1440);
        long budget = RenderScale.Budget(null, 11264, 3L << 30, 0)!.Value;
        foreach (int samples in new[] { 1, 2, 4, 8 })
            Assert.Equal(2f, RenderScale.Capped(2f, w, samples, budget));
        // a 4K desktop on an 8 GiB card with 2 GiB of textures: 200 % with 8x is held, 4x is not lowered as far
        var k4 = new Vector2I(3840, 2160);
        long b8 = RenderScale.Budget(null, 8192, 2L << 30, 0)!.Value;
        float at8 = RenderScale.Capped(2f, k4, 8, b8), at4 = RenderScale.Capped(2f, k4, 4, b8);
        Assert.InRange(at8, 1f, 1.99f);
        Assert.True(at4 >= at8);
    }

    [Theory]
    [InlineData("off", 0)]
    [InlineData(" OFF ", 0)]
    [InlineData("auto", null)]
    [InlineData("3072", 3072)]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    [InlineData("lots", null)]
    [InlineData(null, null)]
    public void TheCapKeyReadsOffAutoOrMegabytes(string? word, int? cap) => Assert.Equal(cap, RenderScale.ParseCap(word));

    [Fact] public void TheCapKeyIsWrittenAsItIsRead()
    {
        foreach (int? cap in new int?[] { null, 0, 3072 })
            Assert.Equal(cap, RenderScale.ParseCap(RenderScale.CapWord(cap)));
        Assert.Equal("auto", RenderScale.CapWord(null));
        Assert.Null(new I76.Game.Shell.ShellSettings().RenderScaleCap);
    }

    [Fact] public void TheRowShowsTheChoiceAsItIsWhenNothingHoldsIt()
    {
        Assert.Equal("200%*", RenderScale.Label(6));
        Assert.Equal("100%", RenderScale.Label(RenderScale.Default));
        Assert.Equal(RenderScale.Names, RenderScale.Shown);
        Assert.Equal("", RenderScale.HeldNote);
    }

    /// <summary>A held choice: the pads' value column has room for the scale drawn, the tooltip for the sentence.</summary>
    [Fact] public void AHeldChoiceShowsTheScaleDrawn()
    {
        Assert.Equal("held 155%", RenderScale.Label(6, 6, 155));
        Assert.Equal("150%*", RenderScale.Label(5, 6, 155));      // another value of the row, as it is
        Assert.Equal("200%*", RenderScale.Label(6, 6, null));
        Assert.Equal("200%*", RenderScale.Label(6, 6, 200));
        Assert.Equal("200 % (held at 155 %: video memory)", RenderScale.HeldWords(6, 155));
        Assert.Equal("", RenderScale.HeldWords(6, null));
        Assert.Equal("", RenderScale.HeldWords(RenderScale.Default, 100));
    }
}