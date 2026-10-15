using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class MonitorRelocationLifetimeTests
{
    [Fact]
    public void Settings_monitor_mode_changes_defer_native_positioning_to_relocation_timer()
    {
        // The direct settings event previously invoked SetWindowPos before the
        // guarded monitor-change path could discard Clear's previous frame.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "src/GlassDock.App/Desktop/DesktopOverlayWindow.cs"));
        var start = source.IndexOf("private void ApplyDisplayMode(DockDisplayMode displayMode)", StringComparison.Ordinal);
        var end = source.IndexOf("public void ShowSettings()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var body = source[start..end];
        Assert.DoesNotContain("windowManager.Position(", body);
        Assert.Contains("MonitorTrackingInterval(displayMode)", body);
        Assert.Contains("liquidGlass.DeactivateCapture();", source);
        Assert.Contains("invalidate old monitor capture", source);
        var managerCode = File.ReadAllText(Path.Combine(root.FullName, "src/GlassDock.Windows/Desktop/WindowsOverlayManager.cs"));
        Assert.Contains("if (currentMonitor != 0 && currentMonitor != target)", managerCode);
        Assert.Contains("beforeCrossMonitorMove?.Invoke();", managerCode);
    }

    [Theory]
    [InlineData(DockDisplayMode.Primary)]
    [InlineData(DockDisplayMode.Pointer)]
    [InlineData(DockDisplayMode.Foreground)]
    public void Invalid_window_never_moves_or_touches_popup_owner(DockDisplayMode mode)
    {
        using var manager = new WindowsOverlayManager(0);
        var dismissed = false;
        var captureInvalidated = false;
        Assert.False(manager.RepositionIfMonitorChanged(0, mode,
            () => dismissed = true, () => captureInvalidated = true));
        Assert.False(dismissed);
        Assert.False(captureInvalidated);
        manager.Dispose();
        Assert.False(manager.RepositionIfMonitorChanged(0, mode,
            () => dismissed = true, () => captureInvalidated = true));
        Assert.False(dismissed);
        Assert.False(captureInvalidated);
    }
}
