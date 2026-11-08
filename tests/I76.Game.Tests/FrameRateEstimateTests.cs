using I76.Game.Shell;
using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's frame-rate line (Ui/FrameRateEstimate.cs): the Esc menu's measured figure and its smoothing, the main menu's
/// estimate from the cost model, the words of each, and the last mission's record as the file keeps it.
/// </summary>
public class FrameRateEstimateTests
{
    private static readonly FrameCostSettings Defaults = FrameCostSettings.Of(new ShellSettings(), 3440, 1440);

    private static void Feed(LiveFps f, double seconds, Func<int, double> dt)
    {
        double t = 0;
        for (int i = 0; t < seconds; i++) { double d = dt(i); f.Add(d); t += d; }
    }

    [Fact]
    public void TheMeasuredFigureIsTheLastSecondsRate()
    {
        var f = new LiveFps();
        Assert.Null(f.Fps);
        Feed(f, 2, _ => 1 / 84.0);
        Assert.Equal(84, f.Fps!.Value, 0);
        Assert.Equal(84, f.First!.Value, 0);
    }

    [Fact]
    public void UnevenFramesReadSteady()
    {
        // frames of 8 and 14 ms in turn (a stutter of a frame each) read as their mean rate, the same at every reading
        var f = new LiveFps();
        var seen = new List<int>();
        double t = 0;
        for (int i = 0; t < 5; i++)
        {
            double d = i % 2 == 0 ? 0.008 : 0.014;
            f.Add(d);
            t += d;
            if (t > 1.5 && f.Fps is { } v) seen.Add((int)Math.Round(v));
        }
        Assert.All(seen, v => Assert.InRange(v, 90, 92));
    }

    [Fact]
    public void AChangeDropsTheOldFramesAndKeepsTheFirstFigure()
    {
        var f = new LiveFps();
        Feed(f, 2, _ => 1 / 80.0);
        f.Changed();
        // the shown figure stays until the next reading; the settling frames (a pipeline compile's long one) are not counted
        Assert.Equal(80, f.Fps!.Value, 0);
        f.Add(0.2);
        Feed(f, LiveFps.Settle + LiveFps.Every + 0.05, _ => 1 / 100.0);
        Assert.Equal(100, f.Fps!.Value, 0);
        Assert.Equal(80, f.First!.Value, 0);
        Assert.Equal("Measured 100 fps (+20)", FpsLabels.Measured(f.Fps, f.First));
    }

    [Theory]
    [InlineData(84.4, 84.0, "Measured 84 fps")]
    [InlineData(96.0, 84.0, "Measured 96 fps (+12)")]
    [InlineData(70.0, 84.0, "Measured 70 fps (-14)")]
    [InlineData(84.0, null, "Measured 84 fps")]
    [InlineData(null, null, "Measured -- fps")]
    public void TheMeasuredWords(double? fps, double? first, string text) => Assert.Equal(text, FpsLabels.Measured(fps, first));

    [Theory]
    [InlineData(96.0, "Estimated ~95 fps")]
    [InlineData(97.6, "Estimated ~100 fps")]
    [InlineData(2.0, "Estimated ~5 fps")]
    [InlineData(null, "Estimated: after a mission")]
    public void TheEstimatedWords(double? fps, string text) => Assert.Equal(text, FpsLabels.Estimated(fps));

    [Fact]
    public void AnEstimateAtTheMeasuredSettingsIsTheMeasure()
    {
        var rec = new FpsRecord(97.3, Defaults, "t05");
        Assert.Equal(97.3, FrameCostModel.Estimate(rec, Defaults)!.Value, 6);
    }

    [Fact]
    public void AnEstimateIsTheMachinesFigureTimesTheModelsRatio()
    {
        var rec = new FpsRecord(60, Defaults, "t05");
        var ao = Defaults with { Ao = true };
        Assert.Equal(60 * FrameCostModel.Ms(Defaults) / FrameCostModel.Ms(ao), FrameCostModel.Estimate(rec, ao)!.Value, 6);
        Assert.True(FrameCostModel.Estimate(rec, ao) < 60);
        // a slower machine's estimate moves by the same share
        var half = new FpsRecord(30, Defaults, "t05");
        Assert.Equal(FrameCostModel.Estimate(rec, ao)!.Value / 2, FrameCostModel.Estimate(half, ao)!.Value, 6);
    }

    [Fact]
    public void EveryCheaperValueIsFaster()
    {
        var d = FrameCostModel.Ms(Defaults);
        Assert.True(FrameCostModel.Ms(Defaults with { Shadow = 0 }) < d);
        Assert.True(FrameCostModel.Ms(Defaults with { Shadow = 2 }) > d);
        Assert.True(FrameCostModel.Ms(Defaults with { Terrain = 0 }) < d);
        Assert.True(FrameCostModel.Ms(Defaults with { Aa = 0 }) < d);
        Assert.True(FrameCostModel.Ms(Defaults with { Aa = 2 }) > d);
        Assert.True(FrameCostModel.Ms(Defaults with { Aa = 3 }) > FrameCostModel.Ms(Defaults with { Aa = 2 }));
        Assert.True(FrameCostModel.Ms(Defaults with { Mirror = 0 }) < FrameCostModel.Ms(Defaults with { Mirror = 1 }));
        Assert.True(FrameCostModel.Ms(Defaults with { Mirror = 1 }) < d);
        Assert.True(FrameCostModel.Ms(Defaults with { Vegetation = 0 }) < d);
        Assert.True(FrameCostModel.Ms(Defaults with { Vegetation = 2 }) > d);
        Assert.True(FrameCostModel.Ms(Defaults with { Ao = true }) > d);
        Assert.True(FrameCostModel.Ms(Defaults with { RenderScalePercent = 85 }) < d);
        Assert.True(FrameCostModel.Ms(Defaults with { RenderScalePercent = 150 }) > d);
    }

    [Fact]
    public void ThePixelsPartFollowsThePixelCount()
    {
        // 1920x1080 is 0.42 of 3440x1440's pixels; Render Scale 50 % a quarter of the window's
        Assert.Equal(1920.0 * 1080 / (3440.0 * 1440), (Defaults with { Width = 1920, Height = 1080 }).Pixels, 9);
        Assert.Equal(0.25, (Defaults with { RenderScalePercent = 50 }).Pixels, 9);
        Assert.Equal(1.0, (Defaults with { RenderScalePercent = 50 }).WindowPixels, 9);
        double big = FrameCostModel.MainMs(Defaults), small = FrameCostModel.MainMs(Defaults with { Width = 1920, Height = 1080 });
        double px = 1920.0 * 1080 / (3440.0 * 1440);
        Assert.Equal(FrameCostModel.MainPixelMs * (1 - px), big - small, 6);
        // the defaults' main view at 3440x1440 and 1920x1080, as measured on T05's route
        Assert.Equal(7.26, big, 2);
        Assert.Equal(3.77, small, 1);
        // ambient occlusion's cost goes with the pixels too
        double aoBig = FrameCostModel.MainMs(Defaults with { Ao = true }) - big;
        double aoSmall = FrameCostModel.MainMs(Defaults with { Width = 1920, Height = 1080, Ao = true }) - small;
        Assert.Equal(aoBig * px, aoSmall, 6);
    }

    [Fact]
    public void UnderAHundredPercentTheUpscaleIsPaidAtTheWindowsSize()
    {
        var s85 = Defaults with { RenderScalePercent = 85 };
        double expect = FrameCostModel.MainMs(Defaults) - FrameCostModel.MainPixelMs * (1 - 0.85 * 0.85) + FrameCostModel.UpscaleMs;
        Assert.Equal(expect, FrameCostModel.MainMs(s85), 6);
    }

    [Fact]
    public void TheFrameIsTheSlowerPartAndSome()
    {
        foreach (var s in new[] { Defaults, Defaults with { Ao = true }, Defaults with { Width = 1280, Height = 960, RenderScalePercent = 50 } })
        {
            double ms = FrameCostModel.Ms(s), slower = Math.Max(FrameCostModel.ThreadMs(s), FrameCostModel.CardMs(s));
            Assert.InRange(ms, slower, slower * Math.Pow(2, 1 / FrameCostModel.Power) + 1e-9);
        }
        // a small window leaves the main thread holding the frame: little faster than the defaults
        var tiny = Defaults with { Width = 640, Height = 480, RenderScalePercent = 50, Aa = 0 };
        Assert.InRange(FrameCostModel.Ms(tiny), FrameCostModel.CpuMs, FrameCostModel.CpuMs * 1.01);
    }

    [Fact]
    public void AFrameTheMainThreadHeldStaysNearIt()
    {
        // 80 fps (12.5 ms) with the card's main view busy 7 ms of it: the processor held the frame, so a cheaper setting gains little
        var cpu = new FpsRecord(80, Defaults, "t05", GpuMs: 7);
        Assert.InRange(FrameCostModel.Estimate(cpu, Defaults with { RenderScalePercent = 85 })!.Value, 80, 84);
        Assert.Equal(80, FrameCostModel.Estimate(cpu, Defaults)!.Value, 6);
        // a dearer one that makes the card the slower part does lower it
        Assert.InRange(FrameCostModel.Estimate(cpu, Defaults with { Ao = true })!.Value, 60, 75);
        // with the card holding the frame whole, the main thread is taken as about half of it: a smaller window gains nearly by the pixels
        var gpu = new FpsRecord(80, Defaults, "t05", GpuMs: 11.5);
        var ao = Defaults with { Ao = true };
        Assert.True(FrameCostModel.Estimate(gpu, ao) < 70);
        Assert.InRange(FrameCostModel.Estimate(gpu, Defaults with { Width = 1920, Height = 1080 })!.Value, 110, 160);
        // without the card's time the estimate goes by the model's ratio alone
        var old = new FpsRecord(80, Defaults, "t05");
        Assert.Equal(80 * FrameCostModel.Ms(Defaults) / FrameCostModel.Ms(ao), FrameCostModel.Estimate(old, ao)!.Value, 6);
    }

    [Fact]
    public void NoEstimateWithoutAMeasureOrInThe1997Look()
    {
        Assert.Null(FrameCostModel.Estimate(null, Defaults));
        Assert.Null(FrameCostModel.Estimate(new FpsRecord(90, Defaults, "t05"), Defaults with { Look1997 = true }));
    }

    [Fact]
    public void TheCapIsMaxFpsElsePlainVSyncsRefresh()
    {
        var s = new ShellSettings { VSync = false };
        Assert.Null(FrameCostModel.Cap(s, 144));
        s.VSync = true;
        s.LowLatencyPresent = false;
        Assert.Equal(144, FrameCostModel.Cap(s, 144));
        s.LowLatencyPresent = true;   // the mailbox present draws past the refresh
        Assert.Null(FrameCostModel.Cap(s, 144));
        s.MaxFps = Array.IndexOf(I76.Sim.Views.FrameLimit.Caps, 120);
        Assert.Equal(120, FrameCostModel.Cap(s, 144));
    }

    [Fact]
    public void TheSettingsAreReadFromTheBlock()
    {
        var s = new ShellSettings { Vegetation = 2, AmbientOcclusion = true, RenderScale = Array.IndexOf(I76.Game.Boot.RenderScale.Percents, 75) };
        s.Graphics[0x2] = 0; s.Graphics[0x6] = 2; s.Graphics[0x8] = 3; s.Graphics[0xd] = 1;
        Assert.Equal(new FrameCostSettings(2560, 1080, 75, 0, 2, 3, 1, 2, true, I76.Formats.Imaging.DrawDistance.Far, false), FrameCostSettings.Of(s, 2560, 1080));
    }

    [Fact]
    public void TheRecordReadsBackAsWritten()
    {
        var rec = new FpsRecord(97.25, Defaults with { Width = 2560, Height = 1080, Ao = true, RenderScalePercent = 85 }, "t05", 8.123);
        var back = FpsRecord.Parse(rec.Format());
        Assert.NotNull(back);
        Assert.Equal(97.3, back!.Value.Fps, 6);   // one decimal kept
        Assert.Equal(rec.At, back.Value.At);
        Assert.Equal("t05", back.Value.Mission);
        Assert.Equal(8.12, back.Value.GpuMs, 6);
        // a record from before the card's time was kept reads as unknown
        Assert.Equal(0, FpsRecord.Parse(rec.Format().Replace("gpu_ms=8.12\n", ""))!.Value.GpuMs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fps=90\nsize=3440x1440\n")]                     // keys missing
    [InlineData("fps=abc\nsize=3440x1440\nrender_scale=100\nterrain=1\nshadow=1\naa=1\nmirror=2\nvegetation=1\nao=0\nrange=1\n")]
    [InlineData("fps=0\nsize=3440x1440\nrender_scale=100\nterrain=1\nshadow=1\naa=1\nmirror=2\nvegetation=1\nao=0\nrange=1\n")]
    public void AnUnreadableRecordIsNone(string? text) => Assert.Null(FpsRecord.Parse(text));

    [Fact]
    public void TheRecordPersistsInItsFile()
    {
        var dir = Directory.CreateTempSubdirectory("i76-fps-");
        try
        {
            var path = Path.Combine(dir.FullName, "frame-rate.cfg");
            Assert.Null(FpsRecord.Read(path));
            var rec = new FpsRecord(101.5, Defaults, "t01");
            rec.Write(path);
            Assert.Equal(rec, FpsRecord.Read(path));
            new FpsRecord(64, Defaults with { Shadow = 2 }, "t05").Write(path);   // the next mission's replaces it
            Assert.Equal(64, FpsRecord.Read(path)!.Value.Fps);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void PlayIsTheMedianSecondPastTheStart()
    {
        var p = new PlayFps();
        // the start's seconds (slow, compiling) are left out
        for (int i = 0; i < 49; i++) p.Add(0.2);
        Assert.Equal(0, p.Seconds);
        for (int i = 0; i < 100 * 20; i++) p.Add(0.01);
        Assert.Null(p.Median());   // 20 seconds: too few
        for (int i = 0; i < 50 * 20; i++) p.Add(0.02);
        // 20 s at 100 and 20 s at 50 (rounding at the seconds' edges): the median second is one or the other
        Assert.InRange(p.Seconds, 39, 41);
        Assert.NotNull(p.Median());
        Assert.InRange(p.Median()!.Value, 50, 100);
        Assert.Equal(0, p.GpuMs());
        // a stall (a load, a pause) is not a frame
        int n = p.Seconds;
        p.Add(5);
        Assert.Equal(n, p.Seconds);
    }

    [Fact]
    public void TheRecentFigureIsTheLastFewSeconds()
    {
        var p = new PlayFps();
        Assert.Null(p.Recent());
        for (int i = 0; i < 100 * 40; i++) p.Add(0.01);   // 10 s left out, then 30 s at 100
        for (int i = 0; i < 50 * 6; i++) p.Add(0.02);     // then 6 s at 50
        Assert.Equal(50, p.Recent()!.Value, 0);
    }

    [Fact]
    public void AMissionAtItsCapLeavesNoFigure()
    {
        var p = new PlayFps();
        for (int i = 0; i < 60 * 60; i++) p.Add(1 / 60.0, cap: 60);
        Assert.Null(p.Median());
        var q = new PlayFps();
        for (int i = 0; i < 60 * 45; i++) q.Add(1 / 45.0, cap: 60);
        Assert.Equal(45, q.Median()!.Value, 1);
    }

    [Fact]
    public void TheLineIsOnByDefaultAndEveryFigureHasItsHelp()
    {
        Assert.True(new ShellSettings().MenuFps);
        Assert.Contains("paused", GraphicsHelp.MeasuredFps(false));
        Assert.Contains("runs on", GraphicsHelp.MeasuredFps(true));
        Assert.EndsWith("In play just before the menu: 95 fps.", GraphicsHelp.MeasuredFps(false, 95.4));
        Assert.DoesNotContain("In play", GraphicsHelp.MeasuredFps(false, null));
        Assert.Contains("play a mission", GraphicsHelp.EstimatedFps(null));
        var est = GraphicsHelp.EstimatedFps(new FpsRecord(97.4, Defaults, "t05"));
        Assert.Contains("T05, 3440x1440, 97 fps", est);
        // every help line fits the tooltip's few lines
        foreach (var t in new[] { GraphicsHelp.MeasuredFps(false, 95), GraphicsHelp.MeasuredFps(true, 95), GraphicsHelp.EstimatedFps(null), est })
            Assert.InRange(t.Length, 40, 260);
    }
}
