using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;
using V3 = System.Numerics.Vector3;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's Smoke Linger ([port] smoke_haze, SmokeHaze): Normal by default; Long lets a puff spread on past its life as a
/// haze whose smoke (density times area) is kept as it widens and merges, that fades to nothing at the threshold, and whose budget
/// raises the threshold when the haze covers too much of the screen. Without the engine.
/// </summary>
[Collection("SmokeStatics")]
public class SmokeHazeTests
{
    [Fact]
    public void NormalIsTheDefaultAndLongIsWrittenOnlyWhenChosen()
    {
        var s = new ShellSettings();
        Assert.False(s.SmokeHaze);
        Assert.Equal(0, SmokeLingerRow.Index(s));
        SmokeLingerRow.Step(s);
        Assert.True(s.SmokeHaze);
        Assert.Equal("Long" + ShellSettings.PortMark, SmokeLingerRow.Names[SmokeLingerRow.Index(s)]);
        Assert.Equal(GraphicsPages.Effects, GraphicsPages.PageOf(GraphicsRow.SmokeLinger));
        var t = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.SmokeLinger, s, t);
        Assert.True(t.SmokeHaze);
        GraphicsPages.Reset(t, GraphicsRow.SmokeLinger);
        Assert.False(t.SmokeHaze);
        foreach (bool look1997 in new[] { false, true }) Assert.NotEmpty(GraphicsHelp.Text(GraphicsRow.SmokeLinger, look1997));
    }

    [Fact]
    public void ItPersistsAndAnOlderFileIsNormal()
    {
        Assert.False(new ShellSettings().Write(null).HasSectionKey("port", "smoke_haze"));
        var file = new ShellSettings { SmokeHaze = true }.Write(null);
        Assert.True((bool)file.GetValue("port", "smoke_haze"));
        Assert.True(ShellSettings.Read(file).SmokeHaze);
        Assert.False(ShellSettings.Read(ShellFileCompatTests.Parse(ShellFileCompatTests.ForkMainFile)).SmokeHaze);
    }

    [Fact]
    public void TheSettingReachesTheSmoke()
    {
        bool was = SmokeHaze.Long;
        try
        {
            OriginalBehavior.Apply(new ShellSettings { SmokeHaze = true });
            Assert.True(SmokeHaze.Long);
            OriginalBehavior.Apply(new ShellSettings());
            Assert.False(SmokeHaze.Long);
        }
        finally { SmokeHaze.Long = was; }
    }

    [Theory]
    [InlineData(4f, 0.84f)]   // a black wreck column's puff
    [InlineData(2.2f, 0.42f)]   // a rocket trail's
    [InlineData(1.5f, 0.3f)]   // thin steam
    public void AGrowingPuffKeepsItsSmokeAndThinsAsTheInverseOfItsArea(float size, float dens)
    {
        float k = SmokeHaze.Growth(size, dens), mass = dens * size * size, s = size, lastGrowth = float.MaxValue;
        int steps = (int)MathF.Round(SmokeHaze.HazeSeconds / 0.1f);
        for (int i = 1; i <= steps; i++)
        {
            float before = s;
            s = SmokeHaze.Grow(s, k, 0.1f);
            Assert.True(s > before);
            Assert.True(s - before < lastGrowth + 1e-6f);   // ever more slowly
            lastGrowth = s - before;
            float d = SmokeHaze.Density(mass, s);
            Assert.Equal(mass, d * s * s, 3);   // its smoke is kept
            Assert.Equal(dens * size * size / (s * s), d, 4);   // opacity falls as the inverse of the area
        }
        // a lone puff reaches the threshold after the haze's time, whatever it started as
        Assert.Equal(SmokeHaze.Threshold, SmokeHaze.Density(mass, s), 3);
    }

    [Fact]
    public void APuffFadesToNothingAtTheThresholdNoPop()
    {
        float thr = SmokeHaze.Threshold;
        Assert.Equal(0f, SmokeHaze.Visible(thr));
        Assert.True(SmokeHaze.Retired(thr));
        Assert.False(SmokeHaze.Retired(thr * 1.01f));
        Assert.True(SmokeHaze.Visible(thr * 1.01f) < thr * 0.01f);   // just above it, all but invisible
        Assert.Equal(0.5f, SmokeHaze.Visible(0.5f));   // well above it, as it is
        // drawn density is continuous and rising through the threshold's band
        float last = 0f;
        for (float d = thr; d <= thr * 3f; d += thr * 0.01f)
        {
            float v = SmokeHaze.Visible(d);
            Assert.True(v >= last - 1e-7f);
            Assert.True(v - last < thr * 0.05f);
            last = v;
        }
        // a raised threshold (the budget) fades the faintest first
        Assert.Equal(0f, SmokeHaze.Visible(thr * 1.5f, thr * 2f));
        Assert.True(SmokeHaze.Visible(thr * 10f, thr * 2f) > 0f);
    }

    [Fact]
    public void MergingKeepsTheSmokeAndCoversBoth()
    {
        var at = new V3(0, 10, 0);
        float size = 6f, mass = 0.2f * 36f, mass0 = mass;
        var from = new V3(2, 10, 0);
        float fromSize = 5f, fromMass = 0.3f * 25f;
        // handed over in steps, as HazeStep does over AbsorbSeconds
        float left = fromMass;
        for (int i = 0; i < 10; i++)
        {
            float dm = fromMass / 10f;
            SmokeHaze.Absorb(ref at, ref size, ref mass, from, fromSize, dm);
            left -= dm;
        }
        Assert.Equal(mass0 + fromMass, mass, 3);
        Assert.Equal(0f, left, 3);
        Assert.True(at.X > 0f && at.X < 2f);   // between the two, nearer the one with more smoke
        Assert.True(size >= 6f);   // as wide as the wider at least
        Assert.True(size < 6f + 2f + 1f);   // and no wider than both side by side
        // two puffs on the same spot: the size holds, the density adds
        var a2 = V3.Zero; float s2 = 4f, m2 = 0.2f * 16f;
        SmokeHaze.Absorb(ref a2, ref s2, ref m2, V3.Zero, 4f, 0.2f * 16f);
        Assert.Equal(4f, s2, 4);
        Assert.Equal(0.4f, SmokeHaze.Density(m2, s2), 4);
    }

    [Fact]
    public void TheBudgetRaisesTheThresholdSmoothly()
    {
        Assert.Equal(1f, SmokeHaze.Pressure(0f, 0));
        Assert.Equal(1f, SmokeHaze.Pressure(SmokeHaze.CoverBudget, SmokeHaze.CountBudget));
        Assert.Equal(2f, SmokeHaze.Pressure(SmokeHaze.CoverBudget * 2f, 0), 4);
        Assert.Equal(3f, SmokeHaze.Pressure(0f, SmokeHaze.CountBudget * 3), 4);
        // eased over about two seconds: never a jump in a frame
        float p = 1f;
        for (int i = 0; i < 10; i++)
        {
            float q = SmokeHaze.Ease(p, 3f, 1f / 60f);
            Assert.True(q - p < 0.05f);
            p = q;
        }
        for (int i = 0; i < 1200; i++) p = SmokeHaze.Ease(p, 3f, 1f / 60f);   // ten seconds
        Assert.Equal(3f, p, 2);
    }

    [Fact]
    public void CoverIsTheQuadsShareOfTheScreen()
    {
        float tan = MathF.Tan(MathF.PI / 6f);   // 60 degrees high
        // a quad as tall as the view at that depth on a square screen covers it all
        Assert.Equal(1f, SmokeHaze.Cover(2f * 10f * tan, 10f, tan, 1f), 3);
        Assert.Equal(0.25f, SmokeHaze.Cover(10f * tan, 10f, tan, 1f), 3);
        Assert.Equal(1f, SmokeHaze.Cover(100f, 1f, tan, 1f));   // at most the screen
        Assert.True(SmokeHaze.Cover(5f, 100f, tan, 2.39f) < SmokeHaze.Cover(5f, 100f, tan, 1f));
    }

    [Fact]
    public void TheHazeThinsByTheCameraAndTowardItsReach()
    {
        Assert.Equal(0f, SmokeHaze.Near(1f, 10f));   // inside it
        Assert.Equal(1f, SmokeHaze.Near(10f, 10f));
        Assert.Equal(1f, SmokeHaze.Far(100f, 600f));
        Assert.Equal(0f, SmokeHaze.Far(600f, 600f));
        Assert.True(SmokeHaze.Far(550f, 600f) is > 0f and < 1f);
        // an adopted puff shows only after its wait, easing in
        Assert.Equal(0f, SmokeHaze.In(-0.1f, 2f));
        Assert.Equal(1f, SmokeHaze.In(0f, 0f));
        Assert.Equal(0.5f, SmokeHaze.In(1f, 2f), 4);
        Assert.Equal(1f, SmokeHaze.In(3f, 2f));
    }

    [Fact]
    public void LongStartsDenserAndDarkerAndNormalIsUntouched()
    {
        bool was = SmokeHaze.Long;
        try
        {
            SmokeHaze.Long = false;
            foreach (float d in new[] { 0.3f, 0.42f, 0.84f }) Assert.Equal(d, SmokeHaze.StartDens(d));
            foreach (float k in new[] { 0f, 0.08f, 0.4f, 1f }) Assert.Equal(k, SmokeHaze.StartDark(k));
            Assert.Equal(0.65f, SmokeHaze.LayerCap(0.65f, false));
            SmokeHaze.Long = true;
            Assert.Equal(0.42f * SmokeHaze.DensBoost, SmokeHaze.StartDens(0.42f), 5);
            Assert.True(SmokeHaze.StartDens(0.84f) <= 2f);   // past 1 the puff's soft edge fills in (SmokeStart's density)
            Assert.True(SmokeHaze.StartDark(0.08f) > 0.08f);
            Assert.Equal(1f, SmokeHaze.StartDark(1f));
            Assert.Equal(SmokeHaze.LongCap, SmokeHaze.LayerCap(0.65f, false));
            Assert.Equal(0.5f, SmokeHaze.LayerCap(0.5f, true));   // the I76_SMOKE_CAP hook wins
        }
        finally { SmokeHaze.Long = was; }
    }

    [Fact]
    public void ALoneHazeFadesOverTwoToThreeMinutesAndATrailStaysVisible()
    {
        Assert.InRange(SmokeHaze.HazeSeconds, 120f, 180f);
        // a rocket trail's puff under Long (2.2 m, 0.42 boosted) is still several times the threshold half a minute on
        float size = 2.2f, dens = 0.42f * SmokeHaze.DensBoost, k = SmokeHaze.Growth(size, dens);
        float s = SmokeHaze.Grow(size, k, 30f);
        Assert.True(SmokeHaze.Density(dens * size * size, s) > 3f * SmokeHaze.Threshold);
        Assert.False(SmokeHaze.Retired(SmokeHaze.Density(dens * size * size, SmokeHaze.Grow(size, k, 60f))));
    }

    [Fact]
    public void TheShadersAgeHoldsAtOneAndCreepsOn()
    {
        Assert.Equal(1f, SmokeHaze.ShaderAge(0f, 4f));
        Assert.True(SmokeHaze.ShaderAge(45f, 4f) < 5f);
        Assert.True(SmokeHaze.ShaderAge(10f, 4f) > SmokeHaze.ShaderAge(5f, 4f));
    }
}
