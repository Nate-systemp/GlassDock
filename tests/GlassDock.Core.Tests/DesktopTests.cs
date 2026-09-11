using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DesktopTests
{
    [Fact]
    public void Interrupted_collapse_cannot_complete_a_new_expansion()
    {
        var state = new DockStateMachine();
        state.Show();
        state.Enter();
        state.Complete(state.Expand());
        Assert.Equal(DockState.Expanded, state.State);
        var stale = state.Collapse();
        state.Enter();
        var current = state.Expand();
        state.Complete(stale);
        Assert.Equal(DockState.Expanding, state.State);
        state.Complete(current);
        Assert.Equal(DockState.Expanded, state.State);
    }

    [Fact]
    public void Exit_invalidates_pending_animation()
    {
        var state = new DockStateMachine();
        state.Show();
        state.Enter();
        var animation = state.Expand();
        state.Hide();
        state.Complete(animation);
        Assert.Equal(DockState.Hidden, state.State);
    }

    [Fact]
    public void Normal_lifecycle_returns_to_indicator()
    {
        var state = new DockStateMachine();
        state.Show();
        Assert.Equal(DockState.Idle, state.State);
        state.Enter();
        Assert.Equal(DockState.Hovering, state.State);
        state.Complete(state.Expand());
        state.Complete(state.Collapse());
        Assert.Equal(DockState.Idle, state.State);
    }

    [Fact]
    public void Placement_uses_screen_coordinates_and_dpi_not_app_window()
    {
        var actual = DesktopPlacement.BottomCenter(new PixelRect(-1920, 0, 1920, 1080), 640, 144, 8, 1.25);
        Assert.Equal(new PixelRect(-1360, 890, 800, 180), actual);
    }

    [Fact]
    public void Placement_stays_within_small_monitor()
    {
        var actual = DesktopPlacement.BottomCenter(new PixelRect(100, 100, 320, 200), 640, 144, 100, 2);
        Assert.Equal(new PixelRect(100, 100, 320, 200), actual);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_dpi_is_rejected(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DesktopPlacement.BottomCenter(new(0, 0, 1920, 1080), 640, 144, 24, scale));
}
