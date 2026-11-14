using I76.Game;
using I76.Sim.Views;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The wheel's zoom lasts as long as the view it was made in: a mission or a switch into a view starts at the original's framing.</summary>
[Collection("MouseCameraStatics")]
public class ViewZoomResetTests
{
    [Fact] public void ResetAllZoomsRestoresEveryViewAndCountsAnEpoch()
    {
        MouseCamera.WheelView = ViewZoom.Kind.Chase;
        MouseCamera.Wheel(3);
        MouseCamera.WheelView = ViewZoom.Kind.Overhead;
        MouseCamera.Wheel(-2);
        Assert.NotEqual(ViewZoom.Default, MouseCamera.ZoomOf(ViewZoom.Kind.Chase));
        Assert.NotEqual(ViewZoom.Default, MouseCamera.ZoomOf(ViewZoom.Kind.Overhead));
        int epoch = MouseCamera.ZoomEpoch;
        MouseCamera.ResetAllZooms();
        for (int i = 0; i < ViewZoom.Count; i++) Assert.Equal(ViewZoom.Default, MouseCamera.Zooms[i]);
        Assert.Equal(epoch + 1, MouseCamera.ZoomEpoch);
        MouseCamera.WheelView = null;
    }

    [Fact] public void TheWheelStillZoomsWithinAView()
    {
        MouseCamera.ResetAllZooms();
        MouseCamera.WheelView = ViewZoom.Kind.Chase;
        MouseCamera.Wheel(1);
        Assert.True(MouseCamera.ZoomOf(ViewZoom.Kind.Chase) < 1f);
        MouseCamera.ResetZoom(ViewZoom.Kind.Chase);
        Assert.Equal(1f, MouseCamera.ZoomOf(ViewZoom.Kind.Chase));
        MouseCamera.WheelView = null;
    }

    /// <summary>#190: the Escape menu (and its Options pages) over the F2 view is not a switch of view; leaving the view for another, or a cut-scene, is.</summary>
    [Fact] public void AMenuOverTheViewDoesNotStartTheZoomOver()
    {
        var t = new ZoomViewTracker();
        Assert.True(t.Switched(ViewZoom.Kind.Chase, false));    // into F2: the original's framing
        Assert.False(t.Switched(ViewZoom.Kind.Chase, false));
        Assert.False(t.Switched(null, true));                   // the Escape menu, the Options pages
        Assert.False(t.Switched(null, true));
        Assert.False(t.Switched(ViewZoom.Kind.Chase, false));   // closed: the same view, its zoom kept
        Assert.False(t.Switched(null, false));                  // a cut-scene takes the camera
        Assert.True(t.Switched(ViewZoom.Kind.Chase, false));    // and hands it back: a new start, as before
        Assert.True(t.Switched(ViewZoom.Kind.Overhead, false)); // another view
        Assert.False(t.Switched(null, true));
        Assert.True(t.Switched(ViewZoom.Kind.Chase, false));    // a view switched while the menu was up (none can be): still a switch
    }

    [Fact] public void SideMouseButtonsResolveToMouseDeviceAndNotJoystick()
    {
        Assert.Contains("Mouse4Btn", Controls.MouseNames);
        Assert.Contains("Mouse5Btn", Controls.MouseNames);
        Assert.Equal(("Mouse", "Mouse4Btn"), Controls.InputOf("Mouse4Btn"));
        Assert.Equal(("Mouse", "Mouse5Btn"), Controls.InputOf("Mouse5Btn"));
        Assert.Equal(("joystick1", "Button4"), Controls.InputOf("Button4"));
    }

    [Fact] public void WheelCycleWeaponDisablesWheelZoomWhenEnabled()
    {
        MouseCamera.ResetAllZooms();
        MouseCamera.WheelView = ViewZoom.Kind.Chase;
        MouseCamera.WheelCycleWeapon = true;
        try
        {
            Assert.False(MouseCamera.WheelActive);
            MouseCamera.Wheel(2);
            Assert.Equal(1f, MouseCamera.ZoomOf(ViewZoom.Kind.Chase));
        }
        finally
        {
            MouseCamera.WheelCycleWeapon = false;
            MouseCamera.WheelView = null;
        }
        MouseCamera.WheelView = ViewZoom.Kind.Chase;
        Assert.True(MouseCamera.WheelActive);
        MouseCamera.WheelView = null;
    }
}
