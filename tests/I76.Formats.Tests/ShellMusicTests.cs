using I76.Formats.Audio;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>What the shell and the 3D garage play: the menu's track, never the title's or the ending's.</summary>
public class ShellMusicTests
{
    [Fact]
    public void TheMenuTrackIsTheDiscs15OrNitros10()
    {
        Assert.Equal(15, ShellMusic.MenuTrack(nitro: false));
        Assert.Equal(10, ShellMusic.MenuTrack(nitro: true));
    }

    [Fact]
    public void TheGarageRadioPlaysI76sMissionTracksOnly()
    {
        var tracks = ShellMusic.RadioTracks(17, nitro: false).ToList();
        Assert.Equal(Enumerable.Range(2, 13), tracks);   // 2..14
        Assert.DoesNotContain(15, tracks);
        Assert.DoesNotContain(16, tracks);
        Assert.DoesNotContain(17, tracks);
    }

    [Fact]
    public void TheGarageRadioLeavesOutNitrosMenuAndCredits()
    {
        var tracks = ShellMusic.RadioTracks(11, nitro: true).ToList();
        Assert.Equal([2, 3, 5, 6, 7, 8, 9, 11], tracks);
    }

    [Fact]
    public void AShortDiscGivesWhatItHas() => Assert.Equal([2, 3, 4, 5], ShellMusic.RadioTracks(5, nitro: false));
}
