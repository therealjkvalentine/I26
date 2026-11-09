using I76.Formats.Assembly;

namespace I76.Formats.Imaging;

/// <summary>
/// The lighting calibration's result (<see cref="LightingCalibration.Result"/>) kept in the bake cache, so a mission started again,
/// in this run or the next, skips the solve (a day's is a few tenths of a second, a night's moon several seconds on a worker). The
/// key is everything the solve reads: the ground's texels, the terrain's content, the light ramp, the preset's light, the sun's and
/// the sky's light, the curve, and this library's build (a change to the arithmetic is a new key). The value is the result's nine
/// numbers.
/// </summary>
public static class CalibrationCache
{
    public const string Kind = "calibration";

    /// <summary>The library's build: its module's id, the same for the same code.</summary>
    private static readonly string Code = typeof(LightingCalibration).Module.ModuleVersionId.ToString("N");

    /// <summary>The key of a solve from these inputs (as <see cref="LightingCalibration.Cells"/>, ObjectCells and Solve or NightMoon take them).</summary>
    public static string KeyOf(ReadOnlySpan<byte> texels, ulong terrain, IReadOnlyList<byte>? luma, System.Numerics.Vector3 direction, double intensity,
        double ambient, bool day, LightingCalibration.Rgb sun, LightingCalibration.Rgb skyUp, LightingCalibration.Rgb skyDown, LightingCalibration.ToneCurve curve)
    {
        using var k = new BakeCache.Key(Kind);
        k.Add(Code).Add(texels).Add((long)terrain).Add(luma is null ? -1 : luma.Count);
        if (luma is not null) k.Add(new ReadOnlySpan<byte>([.. luma]));
        k.Add(direction.X).Add(direction.Y).Add(direction.Z).Add(new ReadOnlySpan<double>([intensity, ambient])).Add(day).Add((int)curve);
        foreach (var c in new[] { sun, skyUp, skyDown }) k.Add(new ReadOnlySpan<double>([c.R, c.G, c.B]));
        return k.Finish();
    }

    /// <summary>The result stored under <paramref name="key"/>, or null (none, or not readable).</summary>
    public static LightingCalibration.Result? Read(BakeCache? cache, string key)
    {
        if (cache?.Read(Kind, key) is not [{ Data.Length: 9 * sizeof(double) } blob]) return null;
        var v = new double[9];
        Buffer.BlockCopy(blob.Data, 0, v, 0, blob.Data.Length);
        if (v.Any(x => !double.IsFinite(x))) return null;
        return new LightingCalibration.Result(v[0], v[1], new(v[2], v[3], v[4]), new(v[5], v[6], v[7]), v[8]);
    }

    /// <summary>Stores the result under <paramref name="key"/> (failures ignored).</summary>
    public static void Write(BakeCache? cache, string key, LightingCalibration.Result r)
    {
        if (cache is null) return;
        double[] v = [r.Exposure, r.Sky, r.GroundTint.R, r.GroundTint.G, r.GroundTint.B, r.ObjectTint.R, r.ObjectTint.G, r.ObjectTint.B, r.Moon];
        var data = new byte[v.Length * sizeof(double)];
        Buffer.BlockCopy(v, 0, data, 0, data.Length);
        cache.Write(Kind, key, [new BakeCache.Blob(0, 0, data)]);
    }

    /// <summary>The stored result for <paramref name="key"/>, else <paramref name="solve"/>'s, stored. <paramref name="hit"/>: it was stored.</summary>
    public static LightingCalibration.Result GetOrSolve(BakeCache? cache, string key, Func<LightingCalibration.Result> solve, out bool hit)
    {
        if (Read(cache, key) is { } r) { hit = true; return r; }
        hit = false;
        r = solve();
        Write(cache, key, r);
        return r;
    }
}
