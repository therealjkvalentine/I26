using System.Numerics;
using I76.Formats.Assembly;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// The road frame (RoadRaster.BakeFrame, Road Detail's wear lanes): u across the road from the quad's right edge (0) to its left (1), the
/// road's axis as a doubled angle, so a road run either way gives the same texel; nothing where no quad is.
/// </summary>
public class RoadFrameTests
{
    // the engine's quad is
    // (right_j, left_j, left_j+1, right_j+1) with u 0 on the right and 1 on the left, whichever way that faces
    private static RoadRaster.FlatQuad Quad(Vector2 rightJ, Vector2 leftJ, Vector2 leftJ1, Vector2 rightJ1)
        => new(rightJ, leftJ, leftJ1, rightJ1, 0, Vector2.Min(Vector2.Min(rightJ, leftJ), Vector2.Min(leftJ1, rightJ1)), Vector2.Max(Vector2.Max(rightJ, leftJ), Vector2.Max(leftJ1, rightJ1)));

    private static (float U, byte G, byte B, byte A) At(byte[] img, int res, float size, float x, float z)
    {
        int i = (int)(x / size * res), j = (int)(z / size * res), d = (j * res + i) * 4;
        return (img[d] / 255f, img[d + 1], img[d + 2], img[d + 3]);
    }

    [Fact]
    public void AcrossRunsFromTheRightEdgeToTheLeftAndTheAxisIgnoresTheDirection()
    {
        const int res = 160; const float size = 80f;   // half a metre a texel
        var north = Quad(new(55, 20), new(45, 20), new(45, 40), new(55, 40));
        var img = RoadRaster.BakeFrame([north], Vector2.Zero, size, res)!;
        var right = At(img, res, size, 54.6f, 30);
        var left = At(img, res, size, 45.4f, 30);
        var mid = At(img, res, size, 50.2f, 30);
        Assert.Equal(128, mid.A);
        Assert.InRange(right.U, 0.0f, 0.08f);
        Assert.InRange(left.U, 0.92f, 1.0f);
        Assert.InRange(mid.U, 0.45f, 0.55f);
        // along z: angle 90 degrees, cos 2a = -1, sin 2a = 0
        Assert.InRange((int)mid.G, 0, 1);
        Assert.InRange((int)mid.B, 126, 129);
        // the same road run the other way (its right edge now at x 45) has the same axis
        var south = Quad(new(45, 40), new(55, 40), new(55, 20), new(45, 20));
        var back = At(RoadRaster.BakeFrame([south], Vector2.Zero, size, res)!, res, size, 50.2f, 30);
        Assert.Equal((mid.G, mid.B), (back.G, back.B));
        // off the road: nothing
        Assert.Equal(0, At(img, res, size, 30, 30).A);
    }

    [Fact]
    public void ADiagonalRoadHasItsDoubledAngle()
    {
        const int res = 128; const float size = 64f;
        // run along (1, 1)/sqrt 2: 45 degrees, cos 2a = 0, sin 2a = 1
        var q = Quad(new(22, 18), new(18, 22), new(38, 42), new(42, 38));
        var c = At(RoadRaster.BakeFrame([q], Vector2.Zero, size, res)!, res, size, 30.2f, 30.2f);
        Assert.Equal(128, c.A);
        Assert.InRange((int)c.G, 126, 129);
        Assert.InRange((int)c.B, 254, 255);
    }

    [Fact]
    public void TheCoverRisesSmoothlyTowardAJunction()
    {
        const int res = 160; const float size = 80f;
        var road = Quad(new(55, 0), new(45, 0), new(45, 80), new(55, 80));
        // a road piece's top centred on (50, 70)
        var tri = new RoadRaster.FlatTri(new(45, 65), new(55, 65), new(50, 80), Vector2.Zero, Vector2.Zero, Vector2.Zero, 0, new(45, 65), new(55, 80));
        var img = RoadRaster.BakeFrame([road], Vector2.Zero, size, res, tris: [tri])!;
        var far = At(img, res, size, 50.2f, 10);
        var mid = At(img, res, size, 50.2f, 55);
        var close = At(img, res, size, 50.2f, 69);
        Assert.Equal(128, far.A);
        Assert.InRange((int)mid.A, 140, 230);
        Assert.InRange((int)close.A, 245, 255);
    }

    [Fact]
    public void NoQuadInTheSquareIsNoImage()
        => Assert.Null(RoadRaster.BakeFrame([Quad(new(5, 2), new(-5, 2), new(-5, 12), new(5, 12))], new Vector2(1000, 1000), 64f, 64));
}
