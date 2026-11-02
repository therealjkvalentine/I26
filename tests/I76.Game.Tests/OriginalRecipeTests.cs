using I76.Game.Boot;
using I76.Game.Shell;
using I76.Game.Sim;
using I76.Sim;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The Original Recipe pad: the original's behaviour by default, three rows that follow the look, and the combat's two rules offline only.</summary>
public class OriginalRecipeTests
{
    private sealed class Flat : IGround
    {
        public (float Height, System.Numerics.Vector3 Normal) Query(double x, double y, double z) => (0f, System.Numerics.Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private static OriginalRecipe.Row Row(string label) => OriginalRecipe.Rows.Single(r => r.Label == label);

    [Fact]
    public void EveryRowStartsOnTheOriginalsFirstValueOrTheLooks()
    {
        var s = new ShellSettings();
        foreach (var r in OriginalRecipe.Rows)
        {
            Assert.Equal("Original", r.Names[0]);
            Assert.Equal(0, r.Get(s, true));    // the 1997 look: everything the original's
        }
        // the modern look: the look's own for the rows that follow it
        Assert.Equal("Full", Row("Rear Mirror Range").Names[Row("Rear Mirror Range").Get(s, false)]);
        Assert.Equal("Held", Row("Brake Lamps").Names[Row("Brake Lamps").Get(s, false)]);
        Assert.Equal("Bolts", Row("MG Tracers").Names[Row("MG Tracers").Get(s, false)]);
        Assert.Equal("Far", Row("Draw Distance").Names[Row("Draw Distance").Get(s, false)]);
    }

    [Fact]
    public void ALookRowKeepsFollowingTheLookUntilItsValueChanges()
    {
        var s = new ShellSettings();
        var mirror = Row("Rear Mirror Range");
        mirror.Set(s, true, 0);                 // Done on the value shown: no choice made
        Assert.Null(s.MirrorRangeChoice);
        mirror.Set(s, true, 1);
        Assert.Equal(1, s.MirrorRangeChoice);
        Assert.Equal(1, mirror.Get(s, true));
        OriginalRecipe.ResetAll(s);
        Assert.Null(s.MirrorRangeChoice);
    }

    [Fact]
    public void CopyAndResetCoverEveryRow()
    {
        var a = new ShellSettings();
        foreach (var r in OriginalRecipe.Rows) r.Set(a, true, r.Names.Length - 1);
        var b = new ShellSettings();
        foreach (var r in OriginalRecipe.Rows) r.Copy(a, b);
        foreach (var r in OriginalRecipe.Rows) Assert.Equal(r.Names.Length - 1, r.Get(b, true));
        OriginalRecipe.ResetAll(b);
        foreach (var r in OriginalRecipe.Rows) Assert.Equal(0, r.Get(b, true));
    }

    [Theory]
    [InlineData("unlimited", 1)]
    [InlineData("Original", 0)]
    [InlineData("nonsense", null)]
    [InlineData(null, null)]
    public void StoredWordsReadBack(string? word, int? index) => Assert.Equal(index, OriginalRecipe.ParseChoice(word, OriginalRecipe.OrdnanceNames));

    [Fact]
    public void TheMirrorClipsAt100MetresOrDrawsTheWholeView()
    {
        OriginalBehavior.MirrorRange = null;
        Assert.Equal(100f, OriginalBehavior.MirrorFar(true));
        Assert.Equal(MirrorCameraSettings.FullFarMetres, OriginalBehavior.MirrorFar(false));
        OriginalBehavior.MirrorRange = OriginalBehavior.Original;
        Assert.Equal(100f, OriginalBehavior.MirrorFar(false));
        OriginalBehavior.MirrorRange = null;
    }

    [Fact]
    public void The1997LookAlwaysDrawsTheGunsLine()
    {
        OriginalBehavior.MgTracers = OriginalBehavior.Updated;
        Assert.True(OriginalBehavior.LineTracers(true));
        Assert.False(OriginalBehavior.LineTracers(false));
        OriginalBehavior.MgTracers = null;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheCombatsRulesReachAnOfflineSessionOnly(bool net, bool expected)
    {
        var c = new Combat(new SimWorld(new Flat(), new CrtRand(1)), _ => null) { NetSession = net };
        PlayOptionsCombat.Apply(new ShellSettings { OrdnanceLimit = 1 }, c);
        Assert.Equal(expected, c.UnlimitedOrdnance);
        PlayOptionsCombat.Apply(new ShellSettings(), c);
        Assert.False(c.UnlimitedOrdnance);
    }
}
