using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// No modern puff ends while it is visibly opaque (SmokeFade): a puff that ends with its life grows through it and its opacity eases to
/// nothing by its end; a puff the pool takes back fades out over a moment rather than being cut; one leaving its draw distance thins
/// out before it. Without the engine; the sources are checked to use the rules.
/// </summary>
[Collection("SmokeStatics")]
public class SmokeFadeTests
{
    private static string Repo(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, .. path]));
    }

    private static IEnumerable<float> Lives(int n = 200) => Enumerable.Range(0, n + 1).Select(i => i / (float)n);

    [Fact]
    public void APuffsOpacityIsWholeUntilItsLastPartAndNothingAtItsEnd()
    {
        Assert.Equal(1f, SmokeFade.Out(0f));
        Assert.Equal(1f, SmokeFade.Out(SmokeFade.OutFrom));
        Assert.Equal(0f, SmokeFade.Out(1f));
        Assert.Equal(0f, SmokeFade.Out(1.2f));   // a frame past its life (it is removed then)
        float was = 1f;
        foreach (float lf in Lives())
        {
            float o = SmokeFade.Out(lf);
            Assert.True(o <= was + 1e-6f, $"opacity rises at life {lf}");
            was = o;
        }
        // the last frame it is drawn (at 30 fps a puff of 3 s has 1 % of its life left) shows almost nothing
        Assert.True(SmokeFade.Out(0.99f) < 0.01f);
    }

    [Fact]
    public void UnderNormalAPuffGrowsThroughItsLifeAndIsNothingWhenRemoved()
    {
        bool was = SmokeHaze.Long;
        try
        {
            SmokeHaze.Long = false;
            foreach (float end in new[] { 1.6f, 2.5f, 4.8f, 6.2f })
            {
                float prev = 0f;
                foreach (float lf in Lives())
                {
                    float size = SmokeHaze.PuffSize(0.8f, end, lf) * SmokeFade.Swell(lf);
                    Assert.True(size >= prev - 1e-5f, $"the puff shrinks at life {lf} (end {end})");
                    prev = size;
                }
                // it ends larger than its end size: it goes on spreading as it thins
                Assert.Equal(end * (1f + SmokeFade.SwellBy), prev, 3);
                // drawn density at removal: none
                Assert.Equal(0f, 0.84f * SmokeFade.Out(1f));
            }
        }
        finally { SmokeHaze.Long = was; }
    }

    [Fact]
    public void APuffThePoolTakesBackFadesOutOverAMoment()
    {
        Assert.Equal(1f, SmokeFade.Retire(0f));                       // not retiring
        Assert.Equal(1f, SmokeFade.Retire(SmokeFade.RetireSeconds));  // just begun
        float prev = 1f;
        for (float left = SmokeFade.RetireSeconds; left > 0f; left -= 1f / 60f)
        {
            float o = SmokeFade.Retire(left);
            Assert.True(o <= prev + 1e-6f);
            prev = o;
        }
        Assert.True(SmokeFade.Retire(1f / 60f) < 0.01f);   // its last frame
        // the pool starts retiring its oldest before it is full, so the ring never reaches a puff still showing
        Assert.False(SmokeFade.Crowded(800, 1000));
        Assert.True(SmokeFade.Crowded(900, 1000));
        Assert.True(SmokeFade.RetireAhead(4608) >= 8);
    }

    [Fact]
    public void APuffThinsTowardTheEndOfItsDrawDistance()
    {
        const float far = SmokeWindow.ModernTrailFar;
        Assert.Equal(1f, SmokeFade.Edge(0.5f * far, far));
        Assert.Equal(1f, SmokeFade.Edge(0.8f * far, far));
        Assert.Equal(0f, SmokeFade.Edge(far, far));
        Assert.True(SmokeFade.Edge(0.95f * far, far) < 0.5f);
    }

    [Fact]
    public void UnderLongThePuffIsHandedToTheHazeAsItIs()
    {
        // under Long a puff's life ends in the haze (its own fade, SmokeHaze.Visible), so the end-of-life fade must not thin it first:
        // the sources apply Out and Swell only under Normal
        foreach (var file in new[] { "DamageSmoke.cs", "RocketSmoke.cs" })
        {
            string src = Repo("game", "Scripts", "Sim", file);
            Assert.Contains("SmokeHaze.On ? (lf, size", src);
            Assert.Contains("SmokeFade.Out(lf)", src);
            Assert.Contains("SmokeFade.Swell(lf)", src);
            Assert.Contains("SmokeFade.Retire(p.Gone)", src);
            Assert.Contains("SmokeFade.Edge(", src);
            Assert.Contains("SmokeFade.Crowded(", src);
        }
        // and the haze fades to nothing at its threshold, continuously from the puff's density
        Assert.Equal(0f, SmokeHaze.Visible(SmokeHaze.Threshold));
    }

    [Fact]
    public void NoSlotStillShowingIsTakenForANewPuff()
    {
        string dmg = Repo("game", "Scripts", "Sim", "DamageSmoke.cs"), rocket = Repo("game", "Scripts", "Sim", "RocketSmoke.cs");
        Assert.Contains("if (_mp[_mnext].Live) return;", dmg);
        Assert.Contains("if (s._mp[s._mnext].Live) return;", dmg);
        Assert.Contains("if (_pool[_next].Live) return;", rocket);
    }
}
