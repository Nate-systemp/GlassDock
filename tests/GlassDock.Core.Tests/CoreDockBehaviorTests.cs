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

    [Fact]
    public void Window_selection_skips_stale_handles_and_chooses_foreground()
    {
        var stale = Window(1, 500);
        var recent = Window(2, 300);
        var foreground = Window(3, 100);
        var windows = new[] { stale, recent, foreground };
        var selected = DockWindowClick.SelectWindow(windows,
            window => window.Handle != stale.Handle,
            window => window.Handle == foreground.Handle);
        Assert.Equal(foreground.Handle, selected?.Handle);
    }

    [Fact]
    public void Window_selection_uses_recency_and_never_selects_unavailable_windows()
    {
        var old = Window(1, 10);
        var recent = Window(2, 50);
        var windows = new[] { old, recent };
        Assert.Equal(recent.Handle, DockWindowClick.SelectWindow(windows, _ => true, _ => false)?.Handle);
        Assert.Null(DockWindowClick.SelectWindow(windows, _ => false, _ => false));
        Assert.Null(DockWindowClick.SelectWindow(Array.Empty<ApplicationWindow>(), _ => true, _ => false));
    }

    [Fact]
    public void Window_selection_preserves_order_for_ties()
    {
        var first = Window(1, 10);
        var second = Window(2, 10);
        Assert.Equal(first.Handle, DockWindowClick.SelectWindow([first, second], _ => true, _ => false)?.Handle);
    }

    private static ApplicationWindow Window(int handle, long activated) =>
        new(new ApplicationIdentity("tests.app", @"C:\Tests\App.exe"), "Test", handle, 100, 200, false, null,
            "Test window", false, activated);

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
