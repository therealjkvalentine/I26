using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Smoke Linger Long's fresh smoke (SmokeStart; [port] smoke_start_dark, smoke_start_density, smoke_start_width, smoke_fade): blacker,
/// denser and narrower at the source than section IW's, the long fade kept, each on the Developer panel; and the mirrors' smoke, which a
/// cap of 0 left at nothing whenever the main view had no smoke of its own.
/// </summary>
[Collection("SmokeStatics")]
public class SmokeStartTests
{
    private static Dictionary<string, string> Cfg(ShellSettings s)
    {
        var keys = new Dictionary<string, string>();
        s.WriteSmokeStart((k, v) => keys[k] = v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return keys;
    }

    [Fact]
    public void TheDefaultsAreBlackerDenserNarrowerAndAreNotWritten()
    {
        var s = new ShellSettings();
        Assert.Equal((85, 200, 55, 150), (s.SmokeStartDark, s.SmokeStartDensity, s.SmokeStartWidth, s.SmokeFade));
        Assert.True(SmokeStart.DefaultDark > SmokeStart.BeforeDark && SmokeStart.DefaultDensity > SmokeStart.BeforeDensity && SmokeStart.DefaultWidth < SmokeStart.BeforeWidth);
        Assert.Equal(SmokeStart.BeforeFade, SmokeStart.DefaultFade);   // the long fade kept
        Assert.Empty(Cfg(s));
        var other = new ShellSettings { SmokeStartDark = 0, SmokeStartDensity = 135, SmokeStartWidth = 100, SmokeFade = 300 };
        var text = Cfg(other);
        Assert.Equal(4, text.Count);
        var back = new ShellSettings();
        back.ReadSmokeStart(k => text.GetValueOrDefault(k));
        Assert.Equal((0, 135, 100, 300), (back.SmokeStartDark, back.SmokeStartDensity, back.SmokeStartWidth, back.SmokeFade));
    }

    [Theory]
    [InlineData(null, 85, 200, 55, 150)]
    [InlineData("x", 85, 200, 55, 150)]
    [InlineData("-5", 0, 100, 30, 60)]
    [InlineData("9999", 90, 250, 100, 600)]
    public void TheFilesWordsAreClamped(string? word, int dark, int dens, int width, int fade)
    {
        Assert.Equal(dark, SmokeStart.ParseDark(word));
        Assert.Equal(dens, SmokeStart.ParseDensity(word));
        Assert.Equal(width, SmokeStart.ParseWidth(word));
        Assert.Equal(fade, SmokeStart.ParseFade(word));
    }

    /// <summary>The narrowing is at the source only: the size at the end of a puff's life (and so the haze's spread) is Normal's.</summary>
    [Fact]
    public void ThePuffStartsNarrowAndEndsAtItsOwnSize()
    {
        Assert.Equal(0.8f * 0.55f, SmokeStart.Size(0.8f, 4f, 0f, 0.55f), 4);
        Assert.Equal(4f, SmokeStart.Size(0.8f, 4f, 1f, 0.55f), 4);
        Assert.Equal(0.8f + 3.2f * 0.75f, SmokeStart.Size(0.8f, 4f, 0.5f, 1f), 4);   // width 100: Normal's curve exactly
        float last = 0f;
        for (float t = 0f; t <= 1f; t += 0.05f) { float sz = SmokeStart.Size(0.8f, 4f, t, 0.55f); Assert.True(sz >= last); last = sz; }
    }

    /// <summary>Soot blackens at its birth and is its own colour by the end of its life; a light trail hardly changes; nothing in the haze.</summary>
    [Fact]
    public void FreshSootIsBlackerAndEasesBack()
    {
        Assert.Equal(1f - 0.85f, SmokeStart.Shade(0.85f, 1f, 0f), 4);
        Assert.Equal(1f, SmokeStart.Shade(0.85f, 1f, 1f), 4);
        Assert.Equal(1f, SmokeStart.Shade(0.85f, 1f, 1.4f), 4);
        Assert.True(SmokeStart.Shade(0.85f, 0.2f, 0f) > 0.95f);
        Assert.True(SmokeStart.Shade(0.85f, 1f, 0.3f) < SmokeStart.Shade(0.85f, 1f, 0.6f));
        Assert.Equal(1f, SmokeStart.Shade(0f, 1f, 0f));
    }

    [Fact]
    public void LongTakesTheKeysAndNormalDoesNot()
    {
        bool was = SmokeHaze.Long;
        try
        {
            OriginalBehavior.Apply(new ShellSettings { SmokeStartDensity = 135, SmokeStartDark = 0, SmokeStartWidth = 100, SmokeFade = 150 });
            SmokeHaze.Long = true;
            if (SmokeHaze.Hook is not null || SmokeStart.Hook is not null) return;   // a run's hook decides
            Assert.Equal(0.79f, SmokeHaze.LongCap, 3);        // section IW's 0.8, near enough
            Assert.Equal(0.42f * 1.35f, SmokeHaze.StartDens(0.42f), 4);
            OriginalBehavior.Apply(new ShellSettings());
            SmokeHaze.Long = true;
            Assert.Equal(0.95f, SmokeHaze.LongCap, 3);
            Assert.Equal(1.4f, SmokeHaze.StartDens(0.7f), 4);   // past 1: the puff's edge fills in
            Assert.Equal(0.85f, SmokeHaze.FreshDark, 4);
            Assert.Equal(0.55f, SmokeHaze.Scatter, 4);
            Assert.Equal(150f, SmokeHaze.HazeSeconds);
            SmokeHaze.Long = false;
            Assert.Equal(0.7f, SmokeHaze.StartDens(0.7f));
            Assert.Equal(0f, SmokeHaze.FreshDark);
            Assert.Equal(1f, SmokeHaze.Scatter);
            Assert.Equal(SmokeStart.Size(0.8f, 4f, 0.2f, 1f), SmokeHaze.PuffSize(0.8f, 4f, 0.2f));
        }
        finally { SmokeHaze.Long = was; OriginalBehavior.Apply(new ShellSettings()); }
    }

    [Fact]
    public void TheDeveloperPanelHasTheFourAsLiveSliders()
    {
        foreach (var (key, dflt) in new[] { ("smoke_start_dark", "85*"), ("smoke_start_density", "200*"), ("smoke_start_width", "55*"), ("smoke_fade", "150") })
        {
            var e = Assert.Single(I76.Game.Ui.DevSettings.All, e => e.Key == key);
            Assert.Equal(I76.Game.Ui.DevWhen.Live, e.When);
            Assert.True(e.Slider);
            Assert.Equal(dflt, e.Values[e.Get(new ShellSettings())]);
            Assert.Single(e.Values, v => !v.EndsWith('*'));   // the value before the key, unmarked
        }
    }

    /// <summary>The mirrors' composite reads the one global cap: it must be on while the mirrors alone have smoke (the smoke behind, none ahead).</summary>
    [Fact]
    public void TheCapIsOnWhileOnlyTheMirrorsHaveSmoke()
    {
        Assert.Equal(0.8f, SmokeLayer.CapFor(false, true, 0.8f));
        Assert.Equal(0.8f, SmokeLayer.CapFor(true, false, 0.8f));
        Assert.Equal(0f, SmokeLayer.CapFor(false, false, 0.8f));
    }

    /// <summary>Every layered smoke hands the mirrors their own list, picked by distance from the mirrors, not the main view's (which leaves out what is behind the car).</summary>
    [Fact]
    public void EverySmokeHandsTheMirrorsItsOwnList()
    {
        static string Src(string f) => File.ReadAllText(Path.Combine(Repo(), "game", "Scripts", "Sim", f));
        foreach (var f in new[] { "DamageSmoke.cs", "RocketSmoke.cs", "ChemClouds.cs" })
        {
            var s = Src(f);
            Assert.Contains("SmokeLayer.MirrorPuff(", s);
            Assert.Contains("SmokeLayer.SubmitMirror(this, _mirrorBuf,", s);
        }
        Assert.Contains("CapFor(_ready && total > 0, mirrorTotal > 0,", Src("SmokeLayer.cs"));
    }

    private static string Repo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "I76.sln"))) d = d.Parent;
        return d!.FullName;
    }
}
