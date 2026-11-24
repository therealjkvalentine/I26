using I76.Formats.Assembly;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The road layer with a texture pack: its upscales of the roads bake the modern look's layer at twice the resolution, covering the
/// same ground. Tiny synthetic textures only. (The play fork serves the pack's textures to the bake itself, TextureFactory and
/// MissionBuilder.RoadTextures, so upstream's tests of its second texture set do not apply here; FaithfulRoadsTests has the fork's.)
/// </summary>
public class HdRoadLayerTests
{
    private static RoadRaster.Tex Flat(int w, int h, byte v) { var b = new byte[w * h * 4]; Array.Fill(b, v); return new RoadRaster.Tex(w, h, b); }

    [Fact]
    public void AnUpscaledRoadBakesTheSameRoadAtTwiceTheResolution()
    {
        // a 4-texel road and its 4x upscale, flat colour: a quad baked at 2048 from the upscale covers the same ground as at 1024
        var quad = new RoadRaster.FlatQuad(new(10, 0), new(0, 0), new(0, 40), new(10, 40), 0, new(0, 0), new(10, 40));
        var lo = RoadRaster.Bake([quad], new(0, 0), 80, 64, _ => Flat(4, 4, 200))!;
        var hi = RoadRaster.Bake([quad], new(0, 0), 80, 128, _ => Flat(16, 16, 200))!;
        int Covered(byte[] l) { int n = 0; for (int i = 3; i < l.Length; i += 4) if (l[i] > 127) n++; return n; }
        Assert.InRange(Covered(hi), Covered(lo) * 4 - 2 * 128, Covered(lo) * 4 + 2 * 128);   // within a texel row of the edges
    }
}
