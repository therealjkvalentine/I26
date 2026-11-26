using I76.Formats.Assembly;

namespace I76.Game.World;

/// <summary>
/// Heat Shimmer's paved-road mask (Fires + Road, HeatTune.ByGround: the ripple's full share): where the ground is asphalt, a square of the map round the camera
/// at <see cref="Metres"/> a texel, baked from the same road quads, piece tops and textures the terrain's road layers are baked from
/// (RoadRaster), each texture's texels counted paved by the terrain shader's own test (grey, not a dirt track's colour: saturation of
/// the linear colour under 0.05, gone by 0.2; bright paint, the yellow dash, counts as road). The shimmer pass reads it at a pixel's
/// world position, so no extra target is drawn and the terrain shader is untouched. No Godot here: the tests bake it.
/// </summary>
public static class HeatRoadMask
{
    /// <summary>The square's side (m) and texels a side: 4 km at 2 m a texel (a road is four or more texels wide), 4 MB as R8.</summary>
    public const float Size = 4096f;
    public const int Res = 2048;
    public const float Metres = Size / Res;
    /// <summary>Baked again once the camera is this far (m) from the square's centre: the ripple reaches past 1.4 km on every side then.</summary>
    public const float Recentre = 600f;

    private static float Linear(byte c) { float s = c / 255f; return s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f); }
    private static float Smooth(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3f - 2f * t); }

    /// <summary>How much of a road texel (sRGB bytes) is paved: the terrain shader's dirt test (terrain_modern.gdshaderinc) turned round.</summary>
    public static float Paved(byte r, byte g, byte b)
    {
        float lr = Linear(r), lg = Linear(g), lb = Linear(b);
        float sat = MathF.Max(lr, MathF.Max(lg, lb)) - MathF.Min(lr, MathF.Min(lg, lb));
        float dirt = Smooth(0.05f, 0.2f, sat);
        float lum = 0.2126f * lr + 0.7152f * lg + 0.0722f * lb;
        dirt *= 1f - Smooth(0.2f, 0.35f, lum);   // bright saturated paint is not a dirt track (Road Detail's rule)
        return 1f - dirt;
    }

    /// <summary>A road texture with its colour dropped and its alpha times <see cref="Paved"/>: baked, the layer's alpha is paved coverage.</summary>
    public static RoadRaster.Tex PavedTexture(RoadRaster.Tex t)
    {
        var o = new byte[t.Rgba.Length];
        for (int i = 0; i < o.Length; i += 4)
        {
            o[i] = o[i + 1] = o[i + 2] = 255;
            o[i + 3] = (byte)MathF.Round(t.Rgba[i + 3] * Paved(t.Rgba[i], t.Rgba[i + 1], t.Rgba[i + 2]));
        }
        return new RoadRaster.Tex(t.Width, t.Height, o);
    }

    /// <summary>The square's corner (game frame, X and Z) for a camera at <paramref name="x"/>, <paramref name="z"/>, on the texel grid.</summary>
    public static System.Numerics.Vector2 CornerFor(float x, float z) =>
        new(MathF.Floor((x - Size / 2) / Metres) * Metres, MathF.Floor((z - Size / 2) / Metres) * Metres);

    /// <summary>The mask (one byte a texel, rows along +Z, columns along +X, game frame) of the square from <paramref name="corner"/>; null if no paved road is in it.</summary>
    public static byte[]? Bake(IReadOnlyList<RoadRaster.FlatQuad> quads, IReadOnlyList<RoadRaster.FlatTri>? tris, Func<int, RoadRaster.Tex?> paved,
        System.Numerics.Vector2 corner, float size = Size, int res = Res)
    {
        var rgba = RoadRaster.Bake(quads, corner, size, res, paved, tris: tris);
        if (rgba is null) return null;
        var m = new byte[res * res];
        bool any = false;
        for (int i = 0; i < m.Length; i++) { m[i] = rgba[i * 4 + 3]; any |= m[i] != 0; }
        return any ? m : null;
    }
}
