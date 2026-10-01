using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockAppMenuTests
{
    private static DockApplication App(bool pinned, int count) => new("app", new(null, "app.exe"), "App",
        "app.exe", pinned, Enumerable.Range(1, count).Select(i => new ApplicationWindow(new(null, "app.exe"),
            "App", i, 10, 100, false, null)).ToArray(), null);

    [Theory]
    [InlineData(true, 0, true, false, true, false)]
    [InlineData(false, 1, true, true, true, false)]
    [InlineData(true, 1, true, true, true, false)]
    [InlineData(true, 3, true, true, true, true)]
    [InlineData(false, 1, false, false, false, false)]
    [InlineData(true, 0, false, false, true, false)]
    public void AvailabilityPreservesRunningLaunchAndPinCapabilities(bool pinned, int count, bool launch,
        bool newWindow, bool pin, bool multiple)
    {
        var state = DockAppMenuState.Create(App(pinned, count), launch, false, true);
        Assert.Equal(count > 0, state.IsRunning);
        Assert.Equal(newWindow, state.ShowNewWindow);
        Assert.Equal(pin, state.ShowPin);
        Assert.Equal(multiple, state.HasMultipleWindows);
        Assert.Equal(pinned ? "Unpin from Dock" : "Pin to Dock", state.PinLabel);
        Assert.False(state.CanElevate);
        Assert.True(state.CanLocate);
    }

    [Fact]
    public void StaleMenusInvalidateOnClosePinChangeOrReusedHandleButNotWindowOrdering()
    {
        var app = App(true, 2);
        var state = DockAppMenuState.Create(app, true, true, true);
        Assert.True(state.Matches(app with { Windows = app.Windows.Reverse().ToArray() }));
        Assert.False(state.Matches(app with { IsPinned = false }));
        Assert.False(state.Matches(app with { Windows = app.Windows.Take(1).ToArray() }));
        Assert.False(state.Matches(app with { Windows = [app.Windows[0] with { ProcessStartTicks = 200 }, app.Windows[1]] }));
        Assert.False(state.Matches(app with { Windows = [app.Windows[0] with { ProcessId = 20 }, app.Windows[1]] }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MenuFitsNegativeOriginMonitorFloatingAndPinnedWorkAreas(double dpi)
    {
        foreach (var pinned in new[] { false, true })
        foreach (var anchor in new[] { 0d, 700d, 1400d })
        {
            var area = new PreviewRect(-1920, -1080, 1920, pinned ? 980 : 1080);
            var bounds = WindowPreviewLayout.Position(area, new(-1920, -160, 1920, 160),
                dpi, anchor, 50, 312, 900);
            Assert.InRange(bounds.X, area.X, area.X + area.Width - bounds.Width);
            Assert.InRange(bounds.Y, area.Y, area.Y + area.Height - bounds.Height);
        }
    }
}
