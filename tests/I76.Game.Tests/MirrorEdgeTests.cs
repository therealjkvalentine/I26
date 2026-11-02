using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's smooth mirror outline (MirrorEdge) and the counters' digits on the housing's drums.</summary>
public class MirrorEdgeTests
{
    private static readonly byte[] Cream = [240, 224, 176, 255], Frame = [16, 16, 16, 255], Glass = [1, 1, 1, 0];

    /// <summary>A w × h art: the surround where <paramref name="kind"/> gives 0, frame 1, glass 2.</summary>
    private static byte[] Art(int w, int h, System.Func<int, int, int> kind)
    {
        var a = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                System.Array.Copy(kind(x, y) switch { 0 => Cream, 1 => Frame, _ => Glass }, 0, a, (y * w + x) * 4, 4);
        return a;
    }

    /// <summary>Where row Y of a field channel crosses 128, in original pixels (linear between the field's texels).</summary>
    private static float Crossing(byte[] f, int fw, int Y, int ch)
    {
        for (int X = 1; X < fw; X++)
        {
            int a = f[(Y * fw + X - 1) * 2 + ch], b = f[(Y * fw + X) * 2 + ch];
            if ((a < 128) != (b < 128)) return (X - 0.5f + (127.5f - a) / (b - a)) / MirrorEdge.FieldScale;
        }
        return float.NaN;
    }

    [Fact]
    public void AStraightEdgeStaysOnThePixelBoundary()
    {
        const int w = 24, h = 8;
        var f = MirrorEdge.Field(Art(w, h, (x, _) => x < 8 ? 0 : 1), w, h);
        for (int Y = 0; Y < h * MirrorEdge.FieldScale; Y++) Assert.InRange(Crossing(f, w * MirrorEdge.FieldScale, Y, 0), 7.9f, 8.1f);
    }

    [Fact]
    public void AStaircaseComesOutAStraightSlope()
    {
        // one-pixel steps every two rows: the art's edge as drawn zigzags half a pixel each side of the slope x = 4 + y/2
        const int w = 32, h = 32;
        var f = MirrorEdge.Field(Art(w, h, (x, y) => x < 4 + y / 2 ? 0 : 1), w, h);
        float worst = 0;
        for (int Y = 6 * MirrorEdge.FieldScale; Y < 26 * MirrorEdge.FieldScale; Y++)
        {
            float y = (Y + 0.5f) / MirrorEdge.FieldScale;
            float ideal = 4 + (y - 1) / 2;   // the line through the steps' midpoints (the steps are up to half a pixel off it)
            worst = System.MathF.Max(worst, System.MathF.Abs(Crossing(f, w * MirrorEdge.FieldScale, Y, 0) - ideal));
        }
        Assert.True(worst < 0.15f, $"off the slope by {worst} px");
    }

    [Fact]
    public void TheCrossHairOnTheGlassIsNoHoleInIt()
    {
        // a one-pixel opaque line across the glass: the glass channel stays glass over it
        const int w = 32, h = 16;
        var f = MirrorEdge.Field(Art(w, h, (x, y) => x < 2 || x >= 30 || y < 2 || y >= 14 ? 1 : x == 16 ? 1 : 2), w, h);
        int fw = w * MirrorEdge.FieldScale;
        for (int X = 15 * MirrorEdge.FieldScale; X < 18 * MirrorEdge.FieldScale; X++) Assert.True(f[(8 * MirrorEdge.FieldScale * fw + X) * 2 + 1] > 200);
    }

    [Fact]
    public void TheFillGivesTheSurroundAndTheGlassTheFramesColour()
    {
        const int w = 8, h = 4;
        var art = Art(w, h, (x, _) => x < 3 ? 0 : x < 5 ? 1 : 2);
        var fill = MirrorEdge.Fill(art, w, h, 1);
        for (int x = 0; x < w; x++) Assert.Equal(new byte[] { 16, 16, 16, 255 }, fill[(x * 4)..(x * 4 + 4)]);
    }

    [Fact]
    public void TheModernLookDrawsTheDigitsOnTheHousingsDrums() =>
        Assert.Equal((0, -1), (Cockpit.DigitShift(faithful: true), Cockpit.DigitShift(faithful: false)));
}
