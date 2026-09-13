using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class WindowPreviewTests
{
    private static ApplicationWindow Window(long handle, bool active = false, long recent = 0) =>
        new(new("App", "app.exe"), "App", handle, 42, 100, active, null, $"Window {handle}", false, recent);
    private static DockApplication App(params ApplicationWindow[] windows) => new("app:APP", new("App", "app.exe"), "App", "app.exe", true, windows, null);

    [Fact]
    public void NoWindowsNeverCreatesAPreview()
    {
        var session = new WindowPreviewSession();
        var revision = session.Begin(App());
        Assert.Equal(WindowPreviewState.Hidden, session.State);
        Assert.False(session.Show(revision));
    }

    [Fact]
    public void ForegroundThenRecentActivityDeterminesFrontWindow()
    {
        var session = new WindowPreviewSession();
        session.Begin(App(Window(1, recent: 50), Window(2, active: true), Window(3, recent: 100)));
        Assert.Equal(new long[] { 2, 3, 1 }, session.Windows.Select(window => window.Handle));
        session.Begin(App(Window(1, recent: 50), Window(2), Window(3, recent: 100)));
        Assert.Equal(3, session.Windows[0].Handle);
    }

    [Fact]
    public void RapidIconSwitchInvalidatesDelayedShow()
    {
        var session = new WindowPreviewSession();
        var stale = session.Begin(App(Window(1)));
        var latest = session.Begin(App(Window(2)));
        Assert.False(session.Show(stale));
        Assert.True(session.Show(latest));
        Assert.Equal(WindowPreviewState.Compact, session.State);
    }

    [Fact]
    public void HoverSelectsWithoutActivatingAndClickTargetsSpecificWindow()
    {
        var session = new WindowPreviewSession();
        session.Show(session.Begin(App(Window(1), Window(2))));
        session.Expand();
        session.Select(2);
        Assert.Equal(WindowPreviewState.WindowHovered, session.State);
        Assert.Equal(2, session.Activate(2)!.Handle);
        Assert.Equal(WindowPreviewState.Activating, session.State);
    }

    [Fact]
    public void CollapseClearsSelectionButPreservesTheCompactPreviewAndItsOrder()
    {
        var session = new WindowPreviewSession();
        var revision = session.Begin(App(Window(1), Window(2)));
        session.Show(revision); session.Expand(); session.Select(2);
        session.Collapse();
        Assert.Equal(WindowPreviewState.Compact, session.State);
        Assert.Null(session.SelectedWindow);
        Assert.Equal("app:APP", session.ApplicationId);
        Assert.Equal(revision, session.Revision);
        Assert.Equal(new long[] { 1, 2 }, session.Windows.Select(window => window.Handle));
        session.Expand();
        session.Select(1);
        Assert.Equal(WindowPreviewState.WindowHovered, session.State);
        Assert.Equal(1, session.Activate(1)!.Handle);
    }

    [Fact]
    public void CollapseCannotResurrectHiddenOrWaitingPreviewsOrInterruptActivation()
    {
        var session = new WindowPreviewSession();
        session.Collapse();
        Assert.Equal(WindowPreviewState.Hidden, session.State);
        var revision = session.Begin(App(Window(1)));
        session.Collapse();
        Assert.Equal(WindowPreviewState.Waiting, session.State);
        session.Show(revision); session.Expand(); session.Activate(1);
        session.Collapse();
        Assert.Equal(WindowPreviewState.Activating, session.State);
    }

    [Fact]
    public void ClosingSelectedWindowUpdatesWithoutReorderingSurvivors()
    {
        var session = new WindowPreviewSession();
        session.Show(session.Begin(App(Window(1), Window(2), Window(3))));
        session.Expand(); session.Select(2);
        session.Refresh([Window(3, active: true), Window(1)]);
        Assert.Equal(new long[] { 1, 3 }, session.Windows.Select(window => window.Handle));
        Assert.Null(session.SelectedWindow);
        Assert.Null(session.Activate(2));
        session.Refresh([]);
        Assert.Equal(WindowPreviewState.Hidden, session.State);
    }

    [Theory]
    [InlineData(320, 240)]
    [InlineData(640, 480)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public void ExpandedPreviewsStayBoundedWithManyWindows(double width, double height)
    {
        var layout = WindowPreviewLayout.Create(20, width, height, true);
        Assert.InRange(layout.Capacity, 1, 4);
        Assert.True(layout.Width <= width);
        Assert.True(layout.Height <= height);
        Assert.All(layout.Cards, card => Assert.True(card.X >= 0 && card.X + card.Width <= layout.Width));
    }
}
