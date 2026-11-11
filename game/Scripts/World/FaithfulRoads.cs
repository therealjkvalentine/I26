namespace I76.Game.World;

/// <summary>
/// Which road layers the 1997 look draws where a texture pack serves the modern look's roads (MissionBuilder.BakeFaithfulRoads): the
/// originals', chunk for chunk as a start in the 1997 look or without the pack makes them. Pure managed code (no engine), so it is
/// tested on its own.
/// </summary>
public static class FaithfulRoads
{
    /// <summary>
    /// A pack serves the roads (their textures, the road pieces' tops or the ground decals): some texture as served is not the original's
    /// own (TextureFactory serves the very object of the original where it has no upscale or the look draws none).
    /// </summary>
    public static bool PackServes<T>(IReadOnlyDictionary<int, T?> served, IReadOnlyDictionary<int, T?> originals) where T : class =>
        served.Any(kv => !ReferenceEquals(kv.Value, originals.GetValueOrDefault(kv.Key)));

    /// <summary>
    /// The 1997 look's layer of each chunk, as MissionBuilder's Layer97 picks it without a pack: the bake without the gap fills where
    /// they reach the chunk (<paramref name="plainAt"/>; none there when that bake has nothing), else the one with them, which is the
    /// same picture where no fill reaches.
    /// </summary>
    public static Dictionary<(int, int), T> Choose<T>(IReadOnlyDictionary<(int, int), T> filled, IReadOnlyDictionary<(int, int), T> plain,
        IEnumerable<(int X0, int Z0)> plainAt)
    {
        var own = new HashSet<(int, int)>(plainAt.Select(o => (o.X0, o.Z0)));
        var layers = filled.Where(kv => !own.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (o, layer) in plain) if (own.Contains(o)) layers[o] = layer;
        return layers;
    }
}
