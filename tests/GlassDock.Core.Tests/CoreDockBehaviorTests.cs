using GlassDock.Core.Applications;
using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class CoreDockBehaviorTests
{
    [Theory]
    [InlineData(1, true, false, DockWindowClickAction.Minimize)]
    [InlineData(1, false, false, DockWindowClickAction.Activate)]
    [InlineData(1, false, true, DockWindowClickAction.Restore)]
    [InlineData(1, true, true, DockWindowClickAction.Restore)]
    [InlineData(2, true, false, DockWindowClickAction.Activate)]
    [InlineData(2, false, true, DockWindowClickAction.Restore)]
    public void Click_policy_preserves_group_selection(int count, bool active, bool minimized, DockWindowClickAction expected) =>
        Assert.Equal(expected, DockWindowClick.Resolve(count, active, minimized));

    [Theory]
    [InlineData(true, true, false, 0, 1080, true)]
    [InlineData(true, true, false, 0, 1040, false)]
    [InlineData(true, false, false, 0, 1080, false)]
    [InlineData(true, true, true, 0, 1080, false)]
    [InlineData(false, true, false, 0, 1080, false)]
    [InlineData(true, true, false, 20, 1080, false)]
    public void Fullscreen_requires_foreground_monitor_coverage(bool eligible, bool same, bool minimized,
        double top, double bottom, bool expected) =>
        Assert.Equal(expected, FullscreenPolicy.CoversMonitor(eligible, same, minimized, -1920, top, 0, bottom, -1920, 0, 0, 1080));

    [Fact]
    public void Fullscreen_entry_exit_does_not_change_logical_state()
    {
        var state = new DockStateMachine(); state.Show(); state.Complete(state.Expand());
        var revision = state.Revision;
        foreach (var fullscreen in new[] { true, false, true, false })
        {
            Assert.Equal(fullscreen, FullscreenPolicy.CoversMonitor(true, true, false, 0, 0, 1920, fullscreen ? 1080 : 1040, 0, 0, 1920, 1080));
            Assert.Equal(DockState.Expanded, state.State);
            Assert.Equal(revision, state.Revision);
        }
    }
}
