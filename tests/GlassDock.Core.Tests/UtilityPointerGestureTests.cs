using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilityPointerGestureTests
{
    [Fact]
    public void Secondary_release_and_context_notifications_open_details_once_without_toggle()
    {
        var gesture = new UtilityPointerGesture();
        gesture.Press(false, true);
        Assert.True(gesture.ReleaseSecondary());
        Assert.False(gesture.RequestDetails());
        gesture.Release();
        Assert.True(gesture.SuppressPrimary);
        gesture.Press(false, true);
        Assert.True(gesture.ReleaseSecondary());
    }

    [Fact]
    public void Primary_click_remains_available_after_secondary_or_cancelled_drag()
    {
        var gesture = new UtilityPointerGesture();
        gesture.Press(false, true); gesture.ReleaseSecondary(); gesture.Release();
        gesture.Press(true, false);
        Assert.False(gesture.SuppressPrimary);
        Assert.False(gesture.ReleaseSecondary());
        gesture.Release(); Assert.False(gesture.SuppressPrimary);
        gesture.Press(true, false);
        gesture.BeginDrag(); gesture.Cancel();
        Assert.True(gesture.SuppressPrimary);
        gesture.Press(true, false);
        Assert.False(gesture.SuppressPrimary);
    }

    [Fact]
    public void Long_press_cannot_also_drag_or_toggle_or_request_details_twice()
    {
        var gesture = new UtilityPointerGesture(); gesture.Press(true, false);
        Assert.True(gesture.Hold()); Assert.False(gesture.Hold());
        Assert.False(gesture.BeginDrag()); gesture.Release();
        Assert.True(gesture.SuppressPrimary);
        gesture.Keyboard(); Assert.False(gesture.SuppressPrimary);
    }

    [Fact]
    public void Drag_and_chord_do_not_toggle_or_open_details()
    {
        var gesture = new UtilityPointerGesture(); gesture.Press(true, false);
        Assert.True(gesture.BeginDrag()); Assert.False(gesture.Hold());
        Assert.False(gesture.RequestDetails()); Assert.True(gesture.SuppressPrimary);
        gesture.Cancel(); Assert.False(gesture.ReleaseSecondary());
        gesture.Press(true, true);
        Assert.False(gesture.PrimaryDown); Assert.False(gesture.SecondaryDown);
        Assert.False(gesture.Hold()); Assert.True(gesture.SuppressPrimary);
        Assert.False(gesture.RequestDetails());
    }
}
