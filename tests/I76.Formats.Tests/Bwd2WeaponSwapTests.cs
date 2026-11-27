using I76.Formats.Bwd2;
using I76.Formats.Vfs;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// The I76_VCF_SWAP capture hook's rewrite of a car's weapons (Bwd2File.WithWeapons). A text replace of "gfmedium.gdf" by the
/// shorter "ggchem.gdf" shortened the file by two bytes, so every chunk after the first WEPN started two bytes early ("bad chunk
/// 'PN..'") and the load of a Nitro arena with that car hung.
/// </summary>
public class Bwd2WeaponSwapTests
{
    private static string? NitroDir =>
        Environment.GetEnvironmentVariable("I76_NITRO_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    private static byte[] Chunk(string tag, byte[] payload)
    {
        var o = new byte[Bwd2File.HeaderSize + payload.Length];
        System.Text.Encoding.Latin1.GetBytes(tag).CopyTo(o, 0);
        BitConverter.GetBytes(o.Length).CopyTo(o, 4);
        payload.CopyTo(o, Bwd2File.HeaderSize);
        return o;
    }

    private static byte[] Wepn(uint slot, string gdf)
    {
        var p = new byte[4 + Bwd2File.FileField];
        BitConverter.GetBytes(slot).CopyTo(p, 0);
        System.Text.Encoding.Latin1.GetBytes(gdf).CopyTo(p, 4);
        return Chunk("WEPN", p);
    }

    private static byte[] Car() =>
        [.. Chunk("BWD2", new byte[4]), .. Wepn(1, "gfmedium.gdf"), .. Wepn(2, "gmlight.gdf"), .. Chunk("EXIT", [])];

    [Fact]
    public void AShorterWeaponNameKeepsTheFileAndItsChunks()
    {
        var car = Car();
        var swapped = Bwd2File.WithWeapons(car, new Dictionary<string, string> { ["GFMEDIUM"] = "ggchem" });
        Assert.Equal(car.Length, swapped.Length);
        var f = new Bwd2File(swapped);
        Assert.Equal(car.Length, f.Chunks.Sum(c => c.Size));
        Assert.Equal(["ggchem.gdf", "gmlight.gdf"], f.WeaponMounts().Select(w => w.Gizmo));
        Assert.Equal("gfmedium.gdf", new Bwd2File(car).WeaponMounts()[0].Gizmo);   // the original's bytes untouched
        // the hook's old text replace: two bytes short, the next chunk's header read two bytes into it
        var replaced = System.Text.Encoding.Latin1.GetBytes(System.Text.Encoding.Latin1.GetString(car).Replace("gfmedium.gdf", "ggchem.gdf"));
        Assert.StartsWith("bad chunk 'PN", Assert.Throws<InvalidDataException>(() => new Bwd2File(replaced)).Message);
    }

    [Fact]
    public void ANameThatDoesNotFitTheFieldIsRefused() =>
        Assert.Throws<ArgumentException>(() => Bwd2File.WithWeapons(Car(), new Dictionary<string, string> { ["gfmedium"] = "toolongname" }));

    [Fact]
    public void ABrokenChunkSaysItsTagInFull()
    {
        byte[] bad = [.. Chunk("BWD2", new byte[4]), (byte)'P', (byte)'N', 0x19, 0, 0xff, 0xff, 0, 0];
        var ex = Assert.Throws<InvalidDataException>(() => new Bwd2File(bad));
        Assert.Equal("bad chunk 'PN\\x19\\x00' size 65535 at offset 12", ex.Message);
    }

    /// <summary>The capture that hung: the Nitro Piranha with the chemical mortar where its flame thrower was.</summary>
    [Fact]
    public void TheNitroPiranhaTakesTheChemicalMortar()
    {
        if (NitroDir is null) return;
        var vfs = new AssetVfs(new GameInstall(NitroDir));
        var car = vfs.Read("vppirna1.vcf");
        Assert.Contains(new Bwd2File(car).WeaponMounts(), w => w.Gizmo.Equals("gfmedium.gdf", StringComparison.OrdinalIgnoreCase));
        var f = new Bwd2File(Bwd2File.WithWeapons(car, new Dictionary<string, string> { ["gfmedium"] = "ggchem" }));
        Assert.Equal(new Bwd2File(car).Chunks.Select(c => (c.Tag, c.Size)), f.Chunks.Select(c => (c.Tag, c.Size)));
        Assert.Contains(f.WeaponMounts(), w => w.Gizmo == "ggchem.gdf");
        Assert.DoesNotContain(f.WeaponMounts(), w => w.Gizmo.Equals("gfmedium.gdf", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(GizmoDef.Read("ggchem", vfs.Read("ggchem.gdf")));
    }
}
