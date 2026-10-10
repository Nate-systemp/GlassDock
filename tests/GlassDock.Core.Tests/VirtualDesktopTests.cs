using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class VirtualDesktopTests
{
    private static readonly ApplicationIdentity Identity = new("test.app", @"C:\test.exe");
    private static ApplicationWindow Window(long handle, bool current) =>
        new(Identity, "Test", handle, 10, 20, false, null) { IsOnCurrentDesktop = current };

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(false, 2, true)]
    [InlineData(false, 1, false)]
    [InlineData(false, 3, false)]
    [InlineData(false, 4, false)]
    [InlineData(null, 0, true)]
    [InlineData(null, 2, false)]
    public void Discovery_distinguishes_shell_cloaking_from_suspended_apps(bool? current, int flags, bool expected) =>
        Assert.Equal(expected, VirtualDesktopPolicy.Track(current, flags));

    [Fact]
    public void All_desktop_pinned_window_remains_visible_and_unknown_api_keeps_visible_windows()
    {
        Assert.True(VirtualDesktopPolicy.IsCurrent(false, 0));
        Assert.True(VirtualDesktopPolicy.IsCurrent(null, 0));
        Assert.False(VirtualDesktopPolicy.IsCurrent(false, 2));
    }

    [Fact]
    public void Switch_refreshes_indicators_and_previews_without_removing_pin()
    {
        PinnedApplication[] pins = [new(Identity, "Test", "test.exe", null)];
        var before = DockApplicationCollection.Combine(pins, VirtualDesktopPolicy.Filter([Window(1, true), Window(2, false)], false));
        Assert.Equal(1, Assert.Single(Assert.Single(before).Windows).Handle);
        var after = DockApplicationCollection.Combine(pins, VirtualDesktopPolicy.Filter([Window(1, false), Window(2, true)], false));
        Assert.Equal(before[0].Id, after[0].Id);
        Assert.Equal(2, Assert.Single(after[0].Windows).Handle);
        var emptyDesktop = DockApplicationCollection.Combine(pins, VirtualDesktopPolicy.Filter([Window(1, false)], false));
        Assert.True(Assert.Single(emptyDesktop).IsPinned);
        Assert.False(emptyDesktop[0].IsRunning);
        Assert.Empty(emptyDesktop[0].Windows);
    }

    [Fact]
    public void All_desktops_keeps_mru_and_deduplicates_without_combining_profiles()
    {
        var a = Window(1, true);
        var b = Window(2, false) with { Identity = new("test.otherProfile", @"C:\test.exe") };
        var apps = DockApplicationCollection.Combine([], VirtualDesktopPolicy.Filter([a, b, b], true));
        Assert.Equal(2, apps.Count);
        Assert.All(apps, app => Assert.Single(app.Windows));
        Assert.Equal(a, DockWindowClick.SelectWindow([a, b], w => w.IsOnCurrentDesktop, _ => false));
        Assert.Null(DockWindowClick.SelectWindow([b], w => w.IsOnCurrentDesktop, _ => false));
    }

    [Fact]
    public void Repeated_switch_and_removal_do_not_retain_old_windows()
    {
        for (var i = 0; i < 20; i++)
        {
            var visible = VirtualDesktopPolicy.Filter([Window(1, i % 2 == 0), Window(2, i % 2 != 0)], false);
            Assert.Equal(i % 2 == 0 ? 1 : 2, Assert.Single(visible).Handle);
        }
        Assert.Empty(VirtualDesktopPolicy.Filter([], true));
    }
}
