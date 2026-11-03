using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The cockpit panels' stamp at a scale: the layout's pixels on a panel and a sheet each served at their own size.</summary>
public class CockpitPanelsBlitTests
{
    /// <summary>A w × h RGBA8 sheet in stored (bottom-up) order whose texel (x, y) of the picture is (x, y, 0, a).</summary>
    private static byte[] Sheet(int w, int h, Func<int, int, byte> alpha)
    {
        var b = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { int i = ((h - 1 - y) * w + x) * 4; b[i] = (byte)x; b[i + 1] = (byte)y; b[i + 3] = alpha(x, y); }
        return b;
    }

    private static (byte X, byte Y, byte A) At(byte[] img, int w, int h, int x, int y) { int i = ((h - 1 - y) * w + x) * 4; return (img[i], img[i + 1], img[i + 3]); }

    [Fact]
    public void ASheetAtTwiceThePanelsScaleIsSampledNearest()
    {
        var sheet = Sheet(8, 8, (x, y) => 255);
        var panel = new byte[4 * 4 * 4];
        // the layout's 2x2 rectangle at (1, 1) of the sheet to (2, 2) of the panel
        CockpitPanels.Blit(sheet, 8, 8, 2, 1, 1, 2, 2, panel, 4, 4, 1, 2, 2, keyed: true);
        Assert.Equal(((byte)2, (byte)2, (byte)255), At(panel, 4, 4, 2, 2));
        Assert.Equal(((byte)4, (byte)4, (byte)255), At(panel, 4, 4, 3, 3));
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(panel, 4, 4, 1, 1));
    }

    [Fact]
    public void APanelAtTwiceTheSheetsScaleRepeatsEachTexelAndKeepsTheKey()
    {
        var sheet = Sheet(4, 4, (x, y) => (byte)(x == 1 ? 0 : 255));
        var panel = new byte[8 * 8 * 4];
        CockpitPanels.Blit(sheet, 4, 4, 1, 0, 0, 2, 2, panel, 8, 8, 2, 1, 1, keyed: true);
        // texel (0, 0) covers the panel's (2..3, 2..3); texel (1, y) is keyed and leaves the panel alone
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(panel, 8, 8, 2, 2));
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(panel, 8, 8, 3, 3));
        Assert.Equal(((byte)0, (byte)1, (byte)255), At(panel, 8, 8, 2, 4));
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(panel, 8, 8, 4, 2));
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(panel, 8, 8, 1, 1));
    }

    [Fact]
    public void AnUnkeyedCopyClipsAtTheEdges()
    {
        var sheet = Sheet(4, 4, (x, y) => 0);
        var panel = new byte[4 * 4 * 4];
        CockpitPanels.Blit(sheet, 4, 4, 1, 2, 2, 4, 4, panel, 4, 4, 1, 3, 3, keyed: false);
        Assert.Equal(((byte)2, (byte)2, (byte)0), At(panel, 4, 4, 3, 3));
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(panel, 4, 4, 2, 2));
    }
}
