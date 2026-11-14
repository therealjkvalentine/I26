using I76.Formats.Assembly;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// A helicopter's rotor parts (RotorBlades): the Cobra's interior, which the cockpit (F1) draws, carries its main rotor again, so
/// the modern look has to replace that one too, not only the exterior's.
/// </summary>
public class RotorBladesTests
{
    [InstallFact]
    public void TheCobrasInteriorHasItsMainRotorAtTheExteriorsPlace()
    {
        var outside = new ModelAssembler(TestInstall.Vfs).Assemble("t11ug01.vcf")!;
        var inside = new ModelAssembler(TestInstall.Vfs) { Detail = '5' }.Assemble("t11ug01.vcf")!;
        var o = RotorBlades.Places(outside);
        var i = RotorBlades.Places(inside);
        Assert.Equal(2, o.Count);                  // UA11ROTM and the tail's UA11ROTB
        Assert.Single(i);                          // UA51ROTM
        Assert.Contains(o, p => System.Numerics.Vector3.Distance(p, i[0]) < 0.01f);
        Assert.All(inside.Parts.Where(RotorBlades.Is), p => Assert.All(p.Mesh.Faces, f => Assert.StartsWith("BLADE1", f.Texture, StringComparison.OrdinalIgnoreCase)));
    }

    [InstallFact]
    public void TheHueysInteriorHasNoRotor()
    {
        // its two rotors are the outside model's only: from its seat neither look draws one
        Assert.Equal(2, RotorBlades.Places(new ModelAssembler(TestInstall.Vfs).Assemble("t08uc01.vcf")!).Count);
        Assert.Empty(RotorBlades.Places(new ModelAssembler(TestInstall.Vfs) { Detail = '5' }.Assemble("t08uc01.vcf")!));
    }

    [InstallFact]
    public void ACarHasNoRotor()
    {
        Assert.Empty(RotorBlades.Places(new ModelAssembler(TestInstall.Vfs).Assemble("vppirna1.vcf")!));
        Assert.Empty(RotorBlades.Places(new ModelAssembler(TestInstall.Vfs) { Detail = '5' }.Assemble("vppirna1.vcf")!));
    }
}
