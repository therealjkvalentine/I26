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
    [InlineData(DrawDistance.Faded)]
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

    /// <summary>Faded is the patch kit's picture: a 2300 m clip, the ground fading into the horizon from 0.75 of it.</summary>
    [Fact]
    public void FadedClipsAtTheKitsDistanceAndFadesFromThreeQuartersOfIt()
    {
        Assert.Equal(2300f, DrawDistance.CameraFar(DrawDistance.Faded));
        Assert.Equal(0.75f * DrawDistance.FadedClip, DrawDistance.FadedFogBegin);
        Assert.True(DrawDistance.Fade1997(DrawDistance.Faded));
        Assert.False(DrawDistance.Fade1997(DrawDistance.Original));
        Assert.False(DrawDistance.Fade1997(DrawDistance.Far));
        // the modern look fades into its sky there too, ending inside the clip, and keeps its panels
        Assert.True(DrawDistance.ModernFog(DrawDistance.Faded));
        var (b, e) = DrawDistance.ModernFogRange(DrawDistance.Faded);
        Assert.True(b == DrawDistance.FadedFogBegin && e > b && e < DrawDistance.FadedClip);
        Assert.Equal((DrawDistance.FogBegin, DrawDistance.FogEnd), DrawDistance.ModernFogRange(DrawDistance.Original));
        Assert.True(DrawDistance.ModernPanels(DrawDistance.Faded, true));
        // a saved Faded holds in either look
        Assert.Equal(DrawDistance.Faded, DrawDistance.Resolve(DrawDistance.Faded, look1997: true));
        Assert.Equal(DrawDistance.Faded, DrawDistance.Resolve(DrawDistance.Faded, look1997: false));
        Assert.Equal(DrawDistance.Faded, DrawDistance.FromFile(DrawDistance.Faded, 13));
    }

    /// <summary>The fade's colour: the panels' foot rows (the first four of the data), the opaque texels only, over every panel.</summary>
    [Fact]
    public void TheFadeColourIsThePanelsFootRowsMean()
    {
        static byte[] Panel(int w, int h, byte r, byte g, byte b, byte topRowsAlpha = 255)
        {
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    bool foot = y < HorizonFade.Rows;
                    (px[i], px[i + 1], px[i + 2], px[i + 3]) = foot ? (r, g, b, topRowsAlpha) : ((byte)0, (byte)0, (byte)255, (byte)255);
                }
            return px;
        }
        var c = HorizonFade.Colour([(Panel(8, 16, 255, 0, 0), 8, 16), (Panel(8, 16, 0, 255, 0), 8, 16)]);
        Assert.NotNull(c);
        Assert.Equal(0.5f, c!.Value.R, 3);
        Assert.Equal(0.5f, c.Value.G, 3);
        Assert.Equal(0f, c.Value.B, 3);   // the rows above the foot (blue) are not in it
        // a keyed (transparent) foot is left out
        var k = HorizonFade.Colour([(Panel(8, 16, 255, 0, 0), 8, 16), (Panel(8, 16, 0, 255, 0, 0), 8, 16)]);
        Assert.Equal(1f, k!.Value.R, 3);
        Assert.Null(HorizonFade.Colour([]));
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
