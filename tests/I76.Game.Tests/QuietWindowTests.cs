using I76.Game.Boot;
using Xunit;

namespace I76.Game.Tests;

/// <summary>I76_QUIET_WINDOW: the value's modes, the window's size from the command line or shell.cfg, and where it goes.</summary>
public class QuietWindowTests
{
    [Theory]
    [InlineData(null, QuietWindow.Mode.Off)]
    [InlineData("", QuietWindow.Mode.Off)]
    [InlineData(" 0 ", QuietWindow.Mode.Off)]
    [InlineData("off", QuietWindow.Mode.Off)]
    [InlineData("FALSE", QuietWindow.Mode.Off)]
    [InlineData("no", QuietWindow.Mode.Off)]
    [InlineData("1", QuietWindow.Mode.Behind)]
    [InlineData("on", QuietWindow.Mode.Behind)]
    [InlineData("behind", QuietWindow.Mode.Behind)]
    [InlineData("yes", QuietWindow.Mode.Behind)]
    [InlineData("min", QuietWindow.Mode.Minimised)]
    [InlineData("Minimised", QuietWindow.Mode.Minimised)]
    [InlineData("minimized", QuietWindow.Mode.Minimised)]
    public void ParsesTheModes(string? value, QuietWindow.Mode want) => Assert.Equal(want, QuietWindow.Parse(value));

    [Fact] public void TheCommandLinesResolutionWins()
    {
        Assert.Equal((3440, 1440), QuietWindow.StartSize(["--path", "game", "--resolution", "3440x1440"], "1920x1080"));
        Assert.Equal((800, 600), QuietWindow.StartSize(["--resolution", "1024x768", "--resolution", "800x600"], ""));   // the last one, as Godot reads it
    }

    [Fact] public void ThenShellCfgsResolutionThenTheDefault()
    {
        Assert.Equal((1920, 1080), QuietWindow.StartSize(["--path", "game", "--windowed"], "1920x1080"));
        Assert.Equal((1920, 1080), QuietWindow.StartSize(["--resolution", "huge"], "1920x1080"));   // a malformed one is passed over
        Assert.Equal((1280, 720), QuietWindow.StartSize(["--path", "game"], ""));   // the desktop (empty) is no size for a quiet window
        Assert.Equal((1280, 720), QuietWindow.StartSize([], null));
        Assert.Equal((1280, 720), QuietWindow.StartSize(["--resolution"], "junk"));
    }

    [Fact] public void OnlyTheEnginesArgumentsCount()
    {
        Assert.Equal((1280, 720), QuietWindow.StartSize(["--path", "game", "--", "--resolution", "800x600"], null));
        Assert.Equal((1280, 720), QuietWindow.StartSize(["--path", "game", "++", "--resolution", "800x600"], null));
    }

    [Fact] public void AWindowThatFitsIsCentredWithItsFrame()
    {
        var (pos, borderless) = QuietWindow.Place((1280, 720), (0, 0, 3440, 1400), (0, 0));
        Assert.Equal((1080, 340), pos);
        Assert.False(borderless);
    }

    [Fact] public void ADesktopSizedWindowIsBorderlessAtTheScreensCorner()
    {
        var (pos, borderless) = QuietWindow.Place((3440, 1440), (0, 0, 3440, 1400), (0, 0));   // a taskbar takes 40 px of the usable area
        Assert.Equal((0, 0), pos);
        Assert.True(borderless);
        Assert.Equal((-3440, 0), QuietWindow.Place((3440, 1440), (-3440, 0, 3440, 1400), (-3440, 0)).Pos);   // a second screen's corner
    }

    [Fact] public void MinimisedIsBehindForARunThatNeedsItsFrames()
    {
        Assert.Equal(QuietWindow.Mode.Behind, QuietWindow.ForRun(QuietWindow.Mode.Minimised, needsFrames: true));   // a minimised window draws nothing
        Assert.Equal(QuietWindow.Mode.Minimised, QuietWindow.ForRun(QuietWindow.Mode.Minimised, needsFrames: false));
        Assert.Equal(QuietWindow.Mode.Behind, QuietWindow.ForRun(QuietWindow.Mode.Behind, needsFrames: true));
        Assert.Equal(QuietWindow.Mode.Off, QuietWindow.ForRun(QuietWindow.Mode.Off, needsFrames: true));
    }
}
