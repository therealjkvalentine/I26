using Godot;
using I76.Game.Boot;
using Xunit;

namespace I76.Game.Tests;

/// <summary>I76_MOOD, the modern look's mood prototypes (MainMood.cs): its key list and the sun colour kept at the default's luminance.</summary>
public class MoodTests
{
    [Fact]
    public void Keys_split_on_ampersands_and_keep_commas_and_paths_in_values()
    {
        var m = Main.ParseMood("cal=card&amb=0.5,0.6,0.7&skytex=C:/x/sky-snap4.png&& SAT=0.6");
        Assert.Equal("card", m["cal"]);
        Assert.Equal("0.5,0.6,0.7", m["amb"]);
        Assert.Equal("C:/x/sky-snap4.png", m["skytex"]);
        Assert.Equal("0.6", m["sat"]);   // keys are lower case
        Assert.Equal(4, m.Count);
    }

    [Fact]
    public void An_empty_mood_has_no_keys_so_nothing_changes()
    {
        Assert.Empty(Main.ParseMood(""));
        Assert.Empty(Main.ParseMood("&"));
    }

    [Fact]
    public void A_sun_colour_from_the_sky_keeps_the_default_suns_luminance()
    {
        var sun0 = new Color(1f, 0.96f, 0.88f);
        var c = Main.AtLumaOf(new Color(0.9f, 0.45f, 0.2f), sun0);
        Assert.Equal(Main.Luma(sun0), Main.Luma(c), 3);
        Assert.True(c.R > c.G && c.G > c.B);   // the hue stays the cloud's
    }
}
