using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class MonitorRelocationLifetimeTests
{
    [Theory]
    [InlineData(DockDisplayMode.Primary)]
    [InlineData(DockDisplayMode.Pointer)]
    [InlineData(DockDisplayMode.Foreground)]
    public void Invalid_window_never_moves_or_touches_popup_owner(DockDisplayMode mode)
    {
        using var manager = new WindowsOverlayManager(0);
        var dismissed = false;
        Assert.False(manager.RepositionIfMonitorChanged(0, mode, () => dismissed = true));
        Assert.False(dismissed);
        manager.Dispose();
        Assert.False(manager.RepositionIfMonitorChanged(0, mode, () => dismissed = true));
        Assert.False(dismissed);
    }
}
