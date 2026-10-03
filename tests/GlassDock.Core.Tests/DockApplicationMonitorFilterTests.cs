using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockApplicationMonitorFilterTests
{
    private static readonly ApplicationIdentity PinnedIdentity = new("app:pinned", @"C:\Pinned.exe");
    private static readonly ApplicationIdentity RunningIdentity = new("app:running", @"C:\Running.exe");

    [Fact]
    public void Pinned_apps_remain_visible_but_running_windows_are_monitor_local()
    {
        var pinned = new DockApplication(
            PinnedIdentity.Key,
            PinnedIdentity,
            "Pinned",
            @"C:\Pinned.exe",
            true,
            [Window(PinnedIdentity, 10), Window(PinnedIdentity, 20)],
            null);
        var running = new DockApplication(
            RunningIdentity.Key,
            RunningIdentity,
            "Running",
            null,
            false,
            [Window(RunningIdentity, 20)],
            null);

        var filtered = DockApplicationMonitorFilter.ForMonitor(
            new ApplicationSnapshot([pinned, running]),
            window => window.Handle == 10);

        var localPinned = Assert.Single(filtered.Applications);
        Assert.True(localPinned.IsPinned);
        Assert.Equal(10, Assert.Single(localPinned.Windows).Handle);
    }

    [Fact]
    public void Unpinned_running_apps_appear_only_on_the_monitor_that_owns_a_window()
    {
        var running = new DockApplication(
            RunningIdentity.Key,
            RunningIdentity,
            "Running",
            null,
            false,
            [Window(RunningIdentity, 20), Window(RunningIdentity, 30)],
            null);

        var local = DockApplicationMonitorFilter.ForMonitor(
            new ApplicationSnapshot([running], "warning"),
            window => window.Handle == 30);
        var absent = DockApplicationMonitorFilter.ForMonitor(
            new ApplicationSnapshot([running], "warning"),
            window => window.Handle == 99);

        Assert.Equal("warning", local.Warning);
        Assert.Equal(30, Assert.Single(Assert.Single(local.Applications).Windows).Handle);
        Assert.Empty(absent.Applications);
        Assert.Equal("warning", absent.Warning);
    }

    private static ApplicationWindow Window(ApplicationIdentity identity, long handle) =>
        new(identity, "Window", handle, 1, 1, false, null);
}
