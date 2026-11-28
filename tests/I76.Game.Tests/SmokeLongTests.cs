using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Smoke Linger Long's own puffs (SmokeHaze's slow hand-over): drawn as Normal's on SmokeFade's curve until the hand-over (HandAt of
/// Normal's life), then the haze takes each on with no step in its size, growth rate, density or shader age, keeps growing ever more
/// slowly and thins to nothing about SmokeHaze.HazeSeconds later. Long never shows a puff smaller than Normal's at the same age. Without
/// the engine; the sources are checked to use the rules.
/// </summary>
[Collection("SmokeStatics")]
public class SmokeLongTests
{
    // damage puffs: start 0.8 m, their ends and base lives (smoke_linger 400: four times)
    private static readonly (float End, float Base)[] Puffs = [(1.6f, 2f), (3f, 3f), (4.8f, 4f), (6.2f, 4f), (2.2f, 1.9f)];

    private static (float Size, float Rate, float Hand, float Stage1, float Early, float Span) At(float end, float baseLife)
    {
        float span = baseLife * SmokeLinger.Default / 100f, lf = SmokeHaze.HandAt;
        return (SmokeFade.Size(0.8f, end, lf), SmokeFade.Rate(0.8f, end, lf, span), span * lf, span * (1f - lf), SmokeHaze.EarlySpread(span), span);
    }

    /// <summary>Long's size at an age: Normal's curve until the hand-over, then the slow haze.</summary>
    private static float LongSize(float end, float baseLife, float age)
    {
        var h = At(end, baseLife);
        return age < h.Hand ? SmokeFade.Size(0.8f, end, age / h.Span) : SmokeHaze.SlowSize(h.Size, h.Rate, age - h.Hand, h.Stage1, h.Early);
    }

    [Fact]
    public void LongIsNeverSmallerThanNormalOverTheFirstTwentySeconds()
    {
        foreach (var (end, b) in Puffs)
        {
            float span = b * SmokeLinger.Default / 100f;
            for (float age = 0f; age <= 20f; age += 0.05f)
            {
                // Normal's puff is gone after its life (it has faded to nothing by then)
                if (age > span) { Assert.True(LongSize(end, b, age) > 0f); continue; }
                float normal = SmokeFade.Size(0.8f, end, age / span);
                Assert.True(LongSize(end, b, age) >= normal - 1e-3f, $"end {end}, base {b}: at {age:0.00} s Long {LongSize(end, b, age):0.000} m < Normal {normal:0.000} m");
            }
        }
    }

    [Fact]
    public void TheHandOverHasNoStepInSizeRateDensityOrShaderAge()
    {
        foreach (var (end, b) in Puffs)
        {
            var h = At(end, b);
            const float e = 1e-3f;
            // size: the curve's just before against the haze's just after
            float before = SmokeFade.Size(0.8f, end, (h.Hand - e) / h.Span), after = SmokeHaze.SlowSize(h.Size, h.Rate, e, h.Stage1, h.Early);
            Assert.True(MathF.Abs(after - before) < 0.01f, $"size steps {before} -> {after}");
            // growth rate (m/s)
            float rateBefore = SmokeFade.Rate(0.8f, end, (h.Hand - e) / h.Span, h.Span), rateAfter = SmokeHaze.SlowRate(h.Rate, e, h.Stage1, h.Early);
            Assert.True(MathF.Abs(rateAfter - rateBefore) < 0.005f, $"rate steps {rateBefore} -> {rateAfter}");
            // density: the haze's smoke is the puff's density times its area, so it starts at the puff's density
            const float dens = 1.54f;
            Assert.Equal(dens, SmokeHaze.Density(dens * h.Size * h.Size, h.Size), 4);
            // shader age: Normal's (age over its life) on both sides
            Assert.Equal(h.Hand / h.Span, SmokeHaze.SlowShaderAge(h.Hand, h.Span), 5);
            Assert.True(SmokeHaze.SlowShaderAge(h.Span + 0.01f, h.Span) - SmokeHaze.SlowShaderAge(h.Span - 0.01f, h.Span) < 0.01f);
        }
    }

    [Fact]
    public void TheHazeGrowsEverMoreSlowlyAndNeverFasterThanThePuffDid()
    {
        foreach (var (end, b) in Puffs)
        {
            var h = At(end, b);
            float birth = SmokeFade.Rate(0.8f, end, 0f, h.Span), prev = float.MaxValue;
            for (float since = 0f; since <= 200f; since += 0.5f)
            {
                float r = SmokeHaze.SlowRate(h.Rate, since, h.Stage1, h.Early);
                Assert.True(r <= prev + 1e-6f && r <= birth + 1e-6f);
                prev = r;
            }
        }
    }

    [Fact]
    public void ALonePuffThinsToNothingAboutTheFadeTimeAfterTheHandOver()
    {
        foreach (var (end, b) in Puffs)
        {
            var h = At(end, b);
            const float dens = 1.54f;
            float mass = dens * h.Size * h.Size, loss = SmokeHaze.SlowLoss(mass, h.Size, h.Rate, h.Stage1, h.Early);
            float f = SmokeHaze.HazeSeconds, sf = SmokeHaze.SlowSize(h.Size, h.Rate, f, h.Stage1, h.Early);
            float df = mass * MathF.Exp(-loss * f) / (sf * sf);
            Assert.True(loss == 0f ? df <= SmokeHaze.Threshold * 1.01f : MathF.Abs(df - SmokeHaze.Threshold) < 1e-4f, $"end {end}: {df} at {f} s");
            // and it ends larger than its end size by far (KH's haze: about 6.7 to 8 times; small puffs relatively more)
            Assert.InRange(sf / end, 5f, 12f);
        }
    }

    [Fact]
    public void TheSourcesTakeTheirPuffsOnWithNoStep()
    {
        static string Src(string f)
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "game", "Scripts", "Sim", f))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!, "game", "Scripts", "Sim", f));
        }
        string dmg = Src("DamageSmoke.cs"), rocket = Src("RocketSmoke.cs");
        Assert.Contains("p.Life = SmokeHaze.On ? p.Span * SmokeHaze.HandAt : p.Span;", dmg);
        Assert.Contains("p.Rate = SmokeFade.Rate(0.8f, p.End, lf, p.Span);", dmg);
        Assert.Contains("SmokeHaze.SlowShaderAge(p.Age, p.Span)", dmg);
        Assert.Contains("rate: SmokeFade.Rate(Start, p.End, lf, p.Life), span: p.Life", rocket);
    }
}
