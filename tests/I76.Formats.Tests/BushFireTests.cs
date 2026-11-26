using I76.Formats.Vegetation;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>A burning plant's look (docs/modern-vegetation.md, Brush impacts: Fire): a small flame sized to the plant, not a blaze.</summary>
public class BushFireTests
{
    // the gallery's plants on T16 (I76_VEG_GALLERY): a snakeweed tuft, a creosote, a big mesquite (reach, height in m)
    private static readonly BushFireLook Tuft = BushFire.For(0.35f, 0.71f), Creosote = BushFire.For(0.46f, 1.17f), Mesquite = BushFire.For(1.44f, 1.94f);

    [Fact]
    public void TheFlameStandsAboutThePlantsOwnHeight()
    {
        foreach (var (look, h) in new[] { (Tuft, 0.71f), (Creosote, 1.17f), (Mesquite, 1.94f) })
            Assert.InRange(look.FlameTop, 0.75f * h, 1.25f * h);
        // the old flame rose about 2 m over any plant: 2.6 m/s and 1.5 m/s² over 0.7 s
        Assert.True(Tuft.FlameTop < 1f);
    }

    [Fact]
    public void FlamesAreNarrowAndBornInsideTheCrown()
    {
        foreach (var (look, r) in new[] { (Tuft, 0.35f), (Creosote, 0.46f), (Mesquite, 1.44f) })
        {
            Assert.True(look.FlameSpreadX <= 0.5f * r);
            Assert.True(look.FlameWidth <= MathF.Max(0.6f * r, 0.2f) + 0.01f);
        }
        Assert.True(Mesquite.FlameWidth <= 0.5f);
    }

    [Fact]
    public void ABiggerPlantBurnsWithMoreButNeverAsManyAsBefore()
    {
        Assert.True(Tuft.Flames < Creosote.Flames || Tuft.Flames == Creosote.Flames);
        Assert.True(Creosote.Flames < Mesquite.Flames);
        Assert.True(Tuft.Smoke <= Creosote.Smoke && Creosote.Smoke < Mesquite.Smoke);
        Assert.True(Tuft.SmokeSize < Mesquite.SmokeSize);
        // before: 48 flames and 26 smoke puffs whatever the plant
        foreach (var look in new[] { BushFire.For(0.1f, 0.1f), Tuft, Creosote, Mesquite, BushFire.For(9f, 9f) })
        {
            Assert.InRange(look.Flames, 6, 20);
            Assert.InRange(look.Smoke, 5, 14);
            Assert.True(look.Flames + look.Smoke + look.Embers < 48 + 26);
        }
    }

    [Fact]
    public void OnlyABigShrubShedsEmbersAndThenAFew()
    {
        Assert.Equal(0, Tuft.Embers);
        Assert.InRange(Creosote.Embers, 0, 2);
        Assert.InRange(Mesquite.Embers, 1, 5);
        Assert.InRange(BushFire.For(9f, 9f).Embers, 1, 5);
    }

    [Fact]
    public void TheGlowIsFaintAndShort()
    {
        foreach (var look in new[] { Tuft, Creosote, Mesquite })
        {
            // before: 1.8 energy over 7 m whatever the plant
            Assert.True(look.LightEnergy < 1.2f);
            Assert.True(look.LightRange < 7f);
        }
        Assert.True(Tuft.LightRange < Mesquite.LightRange);
    }
}
