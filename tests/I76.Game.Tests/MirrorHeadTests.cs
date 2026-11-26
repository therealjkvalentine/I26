using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The door mirrors' placement in the modern look ([port] side_mirror_head): the heads where the original's art places them, as the
/// 1997 look draws them, unless the key asks for them turned to the driver. Without the engine.
/// </summary>
public class MirrorHeadTests
{
    [Fact]
    public void HeadsSitWhereTheArtPutsThemByDefault()
    {
        Assert.False(new ShellSettings().SideMirrorHeadTurned);
        Assert.False(MirrorAim.HeadTurned(null));
        Assert.False(MirrorAim.HeadTurned(""));
        Assert.False(MirrorAim.HeadTurned("art"));
        Assert.True(MirrorAim.HeadTurned("turned"));
        Assert.True(MirrorAim.HeadTurned(" Turned "));
    }

    [Fact]
    public void ThePicturesAimDoesNotDependOnTheHead()
    {
        // the picture looks back along the flank by the same rule whether or not the head is turned: only the housing's pose changes
        var cam = new System.Numerics.Vector3(-0.95f, 1.1f, 0.6f);
        float yaw = MirrorAim.YawDegrees(true, cam, 0.9f, -2.4f, 40f);
        Assert.InRange(yaw, 5f, 30f);
        Assert.Equal(yaw, MirrorAim.YawDegrees(true, cam, 0.9f, -2.4f, 40f));
    }
}
