using I76.Formats.Assembly;
using I76.Formats.Imaging;
using Xunit;

namespace I76.Formats.Tests;

public sealed class CalibrationCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "i76-calcache-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static readonly byte[] Texels = [.. Enumerable.Range(0, 8 * 8 * 3).Select(i => (byte)(i * 7))];
    private static readonly byte[] Luma = [.. Enumerable.Range(0, 64).Select(i => (byte)(i * 4))];
    private static readonly LightingCalibration.Rgb Sun = new(1.2, 1.1, 0.9), Up = new(0.2, 0.25, 0.3), Down = new(0.1, 0.1, 0.08);

    private static string Key(byte[]? texels = null, ulong terrain = 42, byte[]? luma = null, float y = -0.7f, double intensity = 0.8, double ambient = 0,
        bool day = true, LightingCalibration.Rgb? sun = null, LightingCalibration.ToneCurve curve = LightingCalibration.ToneCurve.Card) =>
        CalibrationCache.KeyOf(texels ?? Texels, terrain, luma ?? Luma, new(0.3f, y, 0.6f), intensity, ambient, day, sun ?? Sun, Up, Down, curve);

    [Fact]
    public void TheKeyIsTheSameForTheSameInputs() => Assert.Equal(Key(), Key());

    [Fact]
    public void EveryInputChangesTheKey()
    {
        var other = (byte[])Texels.Clone();
        other[5] ^= 1;
        var keys = new[]
        {
            Key(), Key(texels: other), Key(terrain: 43), Key(luma: [.. Luma.Take(32)]), Key(y: -0.6f), Key(intensity: 0.7), Key(ambient: 0.1),
            Key(day: false), Key(sun: new(1.2, 1.1, 0.91)), Key(curve: LightingCalibration.ToneCurve.Filmic),
            CalibrationCache.KeyOf(Texels, 42, null, new(0.3f, -0.7f, 0.6f), 0.8, 0, true, Sun, Up, Down, LightingCalibration.ToneCurve.Card),
        };
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void ASolveIsKeptAndReadBackExactly()
    {
        var cache = new BakeCache(_dir);
        var r = new LightingCalibration.Result(1.234567890123, 0.4321, new(1.01, 0.99, 0.97), new(1.1, 1.0, 0.9), 0.0625);
        int solves = 0;
        var first = CalibrationCache.GetOrSolve(cache, Key(), () => { solves++; return r; }, out bool hit1);
        // a new process: a new cache object over the same folder
        var second = CalibrationCache.GetOrSolve(new BakeCache(_dir), Key(), () => { solves++; return LightingCalibration.Result.None; }, out bool hit2);
        Assert.False(hit1);
        Assert.True(hit2);
        Assert.Equal(1, solves);
        Assert.Equal(r, first);
        Assert.Equal(r, second);
    }

    [Fact]
    public void AnotherKeyOrNoCacheSolvesAgain()
    {
        var cache = new BakeCache(_dir);
        CalibrationCache.Write(cache, Key(), LightingCalibration.Result.None with { Moon = 0.5 });
        Assert.Null(CalibrationCache.Read(cache, Key(day: false)));
        int solves = 0;
        CalibrationCache.GetOrSolve(null, Key(), () => { solves++; return LightingCalibration.Result.None; }, out bool hit);
        Assert.False(hit);
        Assert.Equal(1, solves);
    }

    [Fact]
    public void ADamagedEntryIsSolvedAgain()
    {
        var cache = new BakeCache(_dir);
        CalibrationCache.Write(cache, Key(), LightingCalibration.Result.None with { Moon = 0.5 });
        var file = Directory.GetFiles(Path.Combine(_dir, CalibrationCache.Kind)).Single();
        var bytes = File.ReadAllBytes(file);
        bytes[^6] ^= 0xff;
        File.WriteAllBytes(file, bytes);
        var r = CalibrationCache.GetOrSolve(cache, Key(), () => LightingCalibration.Result.None, out bool hit);
        Assert.False(hit);
        Assert.Equal(LightingCalibration.Result.None, r);
    }
}
