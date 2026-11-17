using I76.Formats.Vegetation;
using I76.Game.Shell;
using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>[port] bush_knock: the knock a car going through a bush makes, the review's A to G (C by default) or off, with its Developer row.</summary>
public class BushKnockKeyTests
{
    [Theory]
    [InlineData(null, "c")]
    [InlineData("", "c")]
    [InlineData("c", "c")]
    [InlineData("A", "a")]
    [InlineData(" g ", "g")]
    [InlineData("OFF", "off")]
    [InlineData("h", "c")]
    [InlineData("loud", "c")]
    public void TheKeyReadsALetterOrOffAndAnythingElseAsTheDefault(string? text, string read) =>
        Assert.Equal(read, ShellSettings.ParseBushKnock(text));

    [Fact]
    public void TheDefaultIsTheReviewsC()
    {
        Assert.Equal("c", new ShellSettings().BushKnock);
        Assert.Equal(ShellSettings.DefaultBushKnock, BrushKnock.DefaultVariant.ToString());
    }

    [Fact]
    public void TheRowOffersOffThenEachVariantAndSetsTheKey()
    {
        var e = Assert.Single(DevSettings.All, d => d.Key == "bush_knock");
        Assert.Equal(DevWhen.Live, e.When);
        Assert.Equal(new[] { "off", "a*", "b*", "c*", "d*", "e*", "f*", "g*" }, e.Values);
        var s = new ShellSettings();
        Assert.Equal("c*", e.Values[e.Get(s)]);
        e.Set(s, 0);
        Assert.Equal("off", s.BushKnock);
        e.Set(s, 5);
        Assert.Equal("e", s.BushKnock);
        s.BushKnock = "G";   // set by hand in the file's case
        Assert.Equal("g*", e.Values[e.Get(s)]);
        Assert.Contains("off", DevTips.Of("bush_knock"));
    }
}
