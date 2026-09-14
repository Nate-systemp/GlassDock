using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class WindowPreviewTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    public void HoverIsIgnoredUntilExpansionFinishesThenCurrentCardCanBeSelected(int count)
    {
        var session = new WindowPreviewSession();
        session.Show(session.Begin(App(Enumerable.Range(1, count).Select(i => Window(i)).ToArray())));
        session.Expand();
        foreach (var progress in new[] { 0d, .5, .999 })
        {
            Assert.False(session.CompleteTransition(progress));
            session.Select(1);
            Assert.Null(session.SelectedWindow);
            Assert.Equal(WindowPreviewState.Expanding, session.State);
        }
        Assert.True(session.CompleteTransition(1));
        Assert.False(session.CompleteTransition(1)); // Completion only triggers the stationary-pointer hit test once.
        session.Select(count);
        Assert.Equal(count, session.SelectedWindow);
        session.Select(1);
        Assert.Equal(1, session.SelectedWindow);
        session.Select(null);
        Assert.Equal(WindowPreviewState.Expanded, session.State);
    }

    [Fact]
    public void CollapseDuringExpansionRejectsLateCompletionAndSupportsReentry()
    {
        var session = new WindowPreviewSession();
        session.Show(session.Begin(App(Window(1), Window(2))));
        session.Expand(); session.CompleteTransition(.4);
        session.Collapse();
        Assert.False(session.CompleteTransition(1));
        session.Select(1);
        Assert.Null(session.SelectedWindow);
        Assert.Equal(WindowPreviewState.Collapsing, session.State);
        session.Expand();
        Assert.True(session.CompleteTransition(1));
        session.Select(2);
        Assert.Equal(2, session.SelectedWindow);
        session.Collapse(); session.CompleteTransition(0);
        Assert.Equal(WindowPreviewState.Compact, session.State);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    public void AllWindowsAreReachableAndPagesResetSelection(int count)
    {
        foreach (var availableWidth in new[] { 600d, 1000d, 1896d })
        {
            var session = new WindowPreviewSession();
            session.Show(session.Begin(App(Enumerable.Range(1, count).Select(i => Window(i)).ToArray())));
            session.Expand(); session.CompleteTransition(1);
            var visited = new List<long>();
            var page = 0;
            do
            {
                var layout = WindowPreviewLayout.Create(count - page, availableWidth, 700, true);
                var compact = WindowPreviewLayout.Create(count - page, availableWidth, 700, false);
                Assert.Equal(compact.Cards.Count, layout.Cards.Count);
                var windows = session.Windows.Skip(page).Take(layout.Capacity).ToArray();
                Assert.Equal(windows.Length, layout.Cards.Count);
                visited.AddRange(windows.Select(w => w.Handle));
                session.Select(windows[0].Handle);
                session.Select(null);
                page = WindowPreviewLayout.NextPage(page, count, layout.Capacity);
                Assert.Null(session.SelectedWindow);
                Assert.True(layout.Width <= availableWidth);
            } while (page != 0);
            Assert.Equal(Enumerable.Range(1, count).Select(i => (long)i), visited);
        }
        Assert.Equal(count, WindowPreviewLayout.Create(count, 1896, 700, true).Cards.Count);
    }

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
        session.Expand(); session.CompleteTransition(1);
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
        session.Show(revision); session.Expand(); session.CompleteTransition(1); session.Select(2);
        session.Collapse();
        Assert.Equal(WindowPreviewState.Collapsing, session.State);
        session.CompleteTransition(0);
        Assert.Equal(WindowPreviewState.Compact, session.State);
        Assert.Null(session.SelectedWindow);
        Assert.Equal("app:APP", session.ApplicationId);
        Assert.Equal(revision, session.Revision);
        Assert.Equal(new long[] { 1, 2 }, session.Windows.Select(window => window.Handle));
        session.Expand(); session.CompleteTransition(1);
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
        session.Show(revision); session.Expand(); session.CompleteTransition(1); session.Activate(1);
        session.Collapse();
        Assert.Equal(WindowPreviewState.Activating, session.State);
    }

    [Fact]
    public void ClosingSelectedWindowUpdatesWithoutReorderingSurvivors()
    {
        var session = new WindowPreviewSession();
        session.Show(session.Begin(App(Window(1), Window(2), Window(3))));
        session.Expand(); session.CompleteTransition(1); session.Select(2);
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
        Assert.True(layout.Capacity >= 1);
        Assert.True(layout.Width <= width);
        Assert.True(layout.Height <= height);
        Assert.All(layout.Cards, card => Assert.True(card.X >= 0 && card.X + card.Width <= layout.Width));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void LayoutFitsMonitorWorkAreaAfterDpiConversion(double scale)
    {
        foreach (var expanded in new[] { false, true })
        {
            var layout = WindowPreviewLayout.Create(8, 1680 / scale - 24, 1050 / scale - 24, expanded);
            Assert.True(Math.Ceiling(layout.Width * scale) <= 1680);
            Assert.True(Math.Ceiling(layout.Height * scale) <= 1050);
            Assert.All(layout.Cards, card => Assert.True(card.Y + card.Height <= layout.Height - 34));
        }
    }
}
