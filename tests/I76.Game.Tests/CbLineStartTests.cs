using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Nitro P01's opening CB line was heard from part-way through: said at tick 0 during the load, while the game's buses were held at
/// -80 dB behind the card, it played on silent. A line started under the hold waits for its end, then plays from where the radio's
/// clock is in it (CbLineStart; the sim no longer ticks before tick 0 there, so that is its beginning: the switch guard checks it).
/// </summary>
public class CbLineStartTests
{
    [Fact]
    public void ALineStartedUnderTheHoldWaits()
    {
        Assert.True(CbLineStart.Waits(gameSoundHeld: true));
        Assert.False(CbLineStart.Waits(gameSoundHeld: false));
    }

    [Theory]
    [InlineData(0, 0, 0f)]          // said at tick 0, the hold ends before tick 1: from its beginning
    [InlineData(450, 0, 0.45f)]     // the radio's clock already 450 ms into it: the audio keeps with it
    [InlineData(1000, 1200, 0f)]    // never before the line's start
    public void TheSoundStartsWhereTheRadiosClockIs(double nowMs, double startedMs, float seconds) =>
        Assert.Equal(seconds, CbLineStart.Offset(nowMs, startedMs), 3);
}
