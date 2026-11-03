using I76.Formats.Imaging;
using I76.Formats.Shell;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>The Draw Distance setting (Graphic Detail): Far by default, the original's 600 m clip at Original.</summary>
public class DrawDistanceTests
{
    [Fact]
    public void AMissingOrUnknownValueIsFar()
    {
        Assert.Equal(DrawDistance.Far, DrawDistance.Parse(null));   // an old shell.cfg without the key
        Assert.Equal(DrawDistance.Far, DrawDistance.Parse(7));
        Assert.Equal(DrawDistance.Far, DrawDistance.Parse(-1));
    }

    [Theory]
    [InlineData(DrawDistance.Original)]
    [InlineData(DrawDistance.Far)]
    public void AChosenValueRoundTrips(int setting) => Assert.Equal(setting, DrawDistance.Parse(setting));

    [Fact]
    public void WithNoChoiceTheLookDecides()
    {
        Assert.Equal(DrawDistance.Original, DrawDistance.Resolve(null, look1997: true));   // the original's 600 m clip and panels
        Assert.Equal(DrawDistance.Far, DrawDistance.Resolve(null, look1997: false));
        // a saved choice stands in either look
        Assert.Equal(DrawDistance.Far, DrawDistance.Resolve(DrawDistance.Far, look1997: true));
        Assert.Equal(DrawDistance.Original, DrawDistance.Resolve(DrawDistance.Original, look1997: false));
    }

    [Fact]
    public void OriginalClipsAtTheEnginesSixHundred()
    {
        Assert.Equal(600f, DrawDistance.CameraFar(DrawDistance.Original));
        Assert.Equal(20000f, DrawDistance.CameraFar(DrawDistance.Far));
    }

    [Fact]
    public void TheModernFadeEndsInsideTheClip()
    {
        Assert.True(DrawDistance.ModernFog(DrawDistance.Original));
        Assert.False(DrawDistance.ModernFog(DrawDistance.Far));
        Assert.True(DrawDistance.FogBegin < DrawDistance.FogEnd && DrawDistance.FogEnd < DrawDistance.OriginalClip);
    }

    [Fact]
    public void ThePanelsAreDrawnOnlyAtOriginalAndOnlyWhenTheMissionHasThem()
    {
        Assert.True(DrawDistance.ModernPanels(DrawDistance.Original, true));
        Assert.False(DrawDistance.ModernPanels(DrawDistance.Original, false));   // n01: no panels, no white band
        Assert.False(DrawDistance.ModernPanels(DrawDistance.Far, true));
    }

    [Fact]
    public void A0110FileFarIsNoChoiceButALaterOneIs()
    {
        Assert.Null(DrawDistance.FromFile(DrawDistance.Far, 11));          // 0.11.0's Save wrote it on every save
        Assert.Null(DrawDistance.FromFile(DrawDistance.Far, 1));
        Assert.Equal(DrawDistance.Original, DrawDistance.FromFile(DrawDistance.Original, 11));   // the player's own choice
        Assert.Equal(DrawDistance.Far, DrawDistance.FromFile(DrawDistance.Far, 12));
        Assert.Null(DrawDistance.FromFile(null, 12));
        Assert.Equal(DrawDistance.Original, DrawDistance.Resolve(DrawDistance.FromFile(DrawDistance.Far, 11), look1997: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SettingTheValueInEffectKeepsFollowingTheLook(bool look1997)
    {
        int shown = LookChoice.Resolve(null, look1997);
        Assert.Null(LookChoice.Choose(null, look1997, shown));            // Done or Cancel on an untouched row
        Assert.Equal(1 - shown, LookChoice.Choose(null, look1997, 1 - shown));
        Assert.Equal(shown, LookChoice.Choose(shown, look1997, shown));   // a choice already made stays made
    }
}
