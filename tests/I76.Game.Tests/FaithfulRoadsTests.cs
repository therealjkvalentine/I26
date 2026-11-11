using System.Numerics;
using I76.Formats.Assembly;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The 1997 look's roads where a pack serves the modern look's (<see cref="FaithfulRoads"/>, MissionBuilder.BakeFaithfulRoads): the
/// originals', chunk for chunk as a start without the pack draws them. Synthetic road textures and quads; the bake is RoadRaster's own.
/// </summary>
public class FaithfulRoadsTests
{
    private const float Chunk = 160f;
    private const int Res = 64;

    /// <summary>A w x h texture in a pattern of its own (opaque), as an original or a pack's upscale of it.</summary>
    private static RoadRaster.Tex Tex(int w, int h, int seed)
    {
        var p = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { p[i * 4] = (byte)(seed * 40 + i); p[i * 4 + 1] = (byte)(seed * 7 + i * 3); p[i * 4 + 2] = (byte)(i * 5); p[i * 4 + 3] = 255; }
        return new RoadRaster.Tex(w, h, p);
    }

    /// <summary>A quad from (x0, z0) to (x1, z1), 10 units wide along x.</summary>
    private static RoadRaster.FlatQuad Road(float x0, float x1, float z, int texture)
    {
        Vector2 a = new(x0, z - 5), b = new(x1, z - 5), c = new(x1, z + 5), d = new(x0, z + 5);
        return new RoadRaster.FlatQuad(a, b, c, d, texture, Vector2.Min(a, c), Vector2.Max(a, c));
    }

    private static Dictionary<(int, int), byte[]> Bake(List<RoadRaster.FlatQuad> quads, Dictionary<int, RoadRaster.Tex?> tex, IEnumerable<(int X0, int Z0)> chunks)
    {
        var made = new Dictionary<(int, int), byte[]>();
        foreach (var o in chunks)
            if (RoadRaster.Bake(quads, new Vector2(o.X0 * 5f, o.Z0 * 5f), Chunk, Res, i => tex.GetValueOrDefault(i)) is { } layer) made[o] = layer;
        return made;
    }

    [Fact]
    public void APackServesTheRoadsWhenAnyTextureIsNotTheOriginalsOwn()
    {
        var road = Tex(16, 16, 1); var top = Tex(8, 8, 2); var colour = Tex(1, 1, 3);
        var originals = new Dictionary<int, RoadRaster.Tex?> { [0] = road, [1] = top, [2] = colour, [3] = null };
        // no pack, the 1997 look, the HD row off, the map editor: the factory hands back the originals' very objects
        Assert.False(FaithfulRoads.PackServes(new Dictionary<int, RoadRaster.Tex?>(originals), originals));
        // a pack's upscale of one road piece's top alone is enough
        Assert.True(FaithfulRoads.PackServes(new Dictionary<int, RoadRaster.Tex?>(originals) { [1] = Tex(32, 32, 2) }, originals));
        // an equal copy is still not the original's own (a served texture is never compared by its texels)
        Assert.True(FaithfulRoads.PackServes(new Dictionary<int, RoadRaster.Tex?>(originals) { [0] = Tex(16, 16, 1) }, originals));
    }

    [Fact]
    public void EachChunkTakesThePlainBakeWhereFillsReachItElseTheFilledOne()
    {
        var filled = new Dictionary<(int, int), string> { [(0, 0)] = "f00", [(32, 0)] = "f32", [(64, 0)] = "f64" };
        var plain = new Dictionary<(int, int), string> { [(32, 0)] = "p32" };
        // (64, 0) is reached by a fill and its plain bake has no road: no layer there, as Layer97 gives none
        var chosen = FaithfulRoads.Choose(filled, plain, [(32, 0), (64, 0)]);
        Assert.Equal(new Dictionary<(int, int), string> { [(0, 0)] = "f00", [(32, 0)] = "p32" }, chosen);
    }

    [Fact]
    public void The1997LooksLayersFromTheOriginalsAreWhatAStartWithoutThePackBakes()
    {
        // a road across two chunks, a gap fill (the modern look's) in the second; an original road texture and a pack's 4x upscale of it
        var flat = new List<RoadRaster.FlatQuad> { Road(10, 310, 80, 0) };
        var fills = new List<RoadRaster.FlatQuad> { Road(250, 300, 120, 0) };
        var original = new Dictionary<int, RoadRaster.Tex?> { [0] = Tex(16, 16, 1) };
        var upscale = new Dictionary<int, RoadRaster.Tex?> { [0] = Tex(64, 64, 9) };
        (int, int)[] chunks = [(0, 0), (32, 0)];
        (int, int)[] reached = [(32, 0)];   // the fill's chunk (x 160..320)

        // without the pack: Layer97 is the filled bake outside the fill's chunk and the plain one in it
        var noPack = FaithfulRoads.Choose(Bake([.. flat, .. fills], original, chunks), Bake(flat, original, reached), reached);
        // with it: the modern look's from the upscale, the 1997 look's from the originals the same way (BakeFaithfulRoads)
        var modern = Bake([.. flat, .. fills], upscale, chunks);
        var faithful = FaithfulRoads.Choose(Bake([.. flat, .. fills], original, chunks), Bake(flat, original, reached), reached);

        Assert.Equal(noPack.Keys.OrderBy(k => k), faithful.Keys.OrderBy(k => k));
        foreach (var (o, layer) in noPack) Assert.True(layer.AsSpan().SequenceEqual(faithful[o]), $"chunk {o}");
        // the pack's layer is another picture: the 1997 look must not take it
        Assert.False(modern[(0, 0)].AsSpan().SequenceEqual(faithful[(0, 0)]));
        // and a chunk no fill reaches is the same picture with or without the fills: one bake serves it in either look
        Assert.True(Bake([.. flat, .. fills], original, [(0, 0)])[(0, 0)].AsSpan().SequenceEqual(Bake(flat, original, [(0, 0)])[(0, 0)]));
        // the fill's own chunk differs: the 1997 look keeps the gap, as the original draws it
        Assert.False(Bake([.. flat, .. fills], original, reached)[(32, 0)].AsSpan().SequenceEqual(faithful[(32, 0)]));
    }
}
