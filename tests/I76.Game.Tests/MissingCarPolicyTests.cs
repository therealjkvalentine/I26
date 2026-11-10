using I76.Game.Boot;
using Xunit;

namespace I76.Game.Tests;

/// <summary>A car asked for that is not in the install: a scripted run stops with a message, an interactive launch plays the mission's own car; a car that loads is untouched.</summary>
public class MissingCarPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACarThatLoadsIsLoadedWhateverTheLaunch(bool scripted)
        => Assert.Equal(MissingCarPolicy.Outcome.Load, MissingCarPolicy.Decide("vpstang1.vcf", exists: true, scripted));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoCarAskedIsNeverAnError(string? requested)
    {
        Assert.Equal(MissingCarPolicy.Outcome.Load, MissingCarPolicy.Decide(requested, exists: false, scripted: true));
        Assert.Null(MissingCarPolicy.FileName(requested));
    }

    [Fact]
    public void AMissingCarFailsAScriptedRunAndFallsBackInAnInteractiveOne()
    {
        Assert.Equal(MissingCarPolicy.Outcome.Fail, MissingCarPolicy.Decide("vpstang1.vcf", exists: false, scripted: true));
        Assert.Equal(MissingCarPolicy.Outcome.Fallback, MissingCarPolicy.Decide("vpstang1.vcf", exists: false, scripted: false));
    }

    [Fact]
    public void TheFileNameGetsItsExtensionAndTheMessageNamesFileAndPlace()
    {
        Assert.Equal("vpstang1.vcf", MissingCarPolicy.FileName("vpstang1"));
        Assert.Equal("VPSTANG1.VCF", MissingCarPolicy.FileName("VPSTANG1.VCF"));
        var msg = MissingCarPolicy.Message("vpstang1.vcf", "the install (X)", MissingCarPolicy.Outcome.Fail);
        Assert.Contains("vpstang1.vcf", msg);
        Assert.Contains("the install (X)", msg);
        Assert.Contains("stopping", msg);
        Assert.Contains("mission's own car", MissingCarPolicy.Message("a.vcf", "p", MissingCarPolicy.Outcome.Fallback));
    }
}
