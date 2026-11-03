using I76.Formats.Audio;
using Xunit;

namespace I76.Formats.Tests;

public class SoundEqTests
{
    [Fact]
    public void The_defaults_trim_the_movies_top_and_lean_the_effects_a_db()
    {
        var (mLow, mHigh) = SoundEq.Parse(null, SoundEq.MovieDefault)!.Value;
        Assert.Equal(0f, mLow.Db);
        Assert.Equal(new SoundEq.Shelf(6000f, -6f), mHigh);
        var (gLow, gHigh) = SoundEq.Parse("", SoundEq.GameDefault)!.Value;
        Assert.Equal(new SoundEq.Shelf(150f, 1f), gLow);
        Assert.Equal(new SoundEq.Shelf(2500f, -1f), gHigh);
    }

    [Fact]
    public void Godots_shelf_gain_is_the_square_root_of_the_far_gain()
    {
        // a Godot shelf of gain g measured g squared far from its corner and g at it: -6 dB far is 0.708, 1.0 flat
        Assert.Equal(0.7079f, new SoundEq.Shelf(6000f, -6f).GodotGain, 3);
        Assert.Equal(1f, new SoundEq.Shelf(250f, 0f).GodotGain);
        Assert.Equal(1.0593f, new SoundEq.Shelf(150f, 1f).GodotGain, 3);
    }

    [Theory]
    [InlineData("off")]
    [InlineData(" Flat ")]
    [InlineData("none")]
    public void Off_leaves_the_bus_flat(string text) => Assert.Null(SoundEq.Parse(text, SoundEq.MovieDefault));

    [Fact]
    public void Two_points_of_its_own_in_either_order_or_the_default()
    {
        Assert.Equal((new SoundEq.Shelf(100f, 3f), new SoundEq.Shelf(8000f, -2f)), SoundEq.Parse("8000:-2, 100:3", SoundEq.GameDefault));
        Assert.Equal(SoundEq.Parse(null, SoundEq.GameDefault), SoundEq.Parse("loud please", SoundEq.GameDefault));
        Assert.Equal(SoundEq.Parse(null, SoundEq.GameDefault), SoundEq.Parse("100:3", SoundEq.GameDefault));   // one point is not two shelves
        Assert.Equal(SoundEq.MaxDb, SoundEq.Parse("100:40, 5000:0", SoundEq.GameDefault)!.Value.Low.Db);
    }
}
