using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class PinDockRuntimeWiringTests
{
    [Fact]
    public void Pin_dock_uses_bottom_edge_clip_and_blocks_collapse_paths()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var dock = File.ReadAllText(Path.Combine(
            directory.FullName, "src", "GlassDock.App", "Desktop", "DesktopOverlayWindow.cs"))
            .ReplaceLineEndings("\n");
        var settings = File.ReadAllText(Path.Combine(
            directory.FullName, "src", "GlassDock.Core", "Settings", "GlassDockSettings.cs"))
            .ReplaceLineEndings("\n");
        var settingsUi = File.ReadAllText(Path.Combine(
            directory.FullName, "src", "GlassDock.App", "Desktop", "SettingsWindow.xaml"))
            .ReplaceLineEndings("\n");
        var overlayManager = File.ReadAllText(Path.Combine(
            directory.FullName, "src", "GlassDock.Windows", "Desktop", "WindowsOverlayManager.cs"))
            .ReplaceLineEndings("\n");

        Assert.Contains("public bool PinDock { get; init; }", settings, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PinDockToggle\"", settingsUi, StringComparison.Ordinal);
        Assert.Contains("private const double PinnedDockClipDepth = ExpandedDockCornerRadius;", dock, StringComparison.Ordinal);
        Assert.Contains("var targetSurfaceBottom = pin ? -PinnedDockClipDepth : BottomMargin;", dock, StringComparison.Ordinal);
        Assert.Contains("var targetContentBottom = pin ? 0 : BottomMargin;", dock, StringComparison.Ordinal);
        Assert.Contains("var targetSurfaceHeight = ExpandedDockHeight + (pin ? PinnedDockClipDepth : 0);", dock, StringComparison.Ordinal);
        Assert.Contains("if (DockPinLock || previews.HoldsDock || SystemPopupOpen) return;", dock, StringComparison.Ordinal);
        Assert.Contains("if (DockPinLock || collapseDelay is { IsCancellationRequested: false }", dock, StringComparison.Ordinal);
        Assert.Contains("if (DockPinLock || pillHideDelay is not null", dock, StringComparison.Ordinal);
        Assert.Contains("await ApplyPinDockModeAsync(pin: true, animate: true);", dock, StringComparison.Ordinal);
        Assert.Contains("windowManager.SetReservedBottomSpace(ExpandedDockHeight);", dock, StringComparison.Ordinal);
        Assert.Contains("windowManager.ClearBottomWorkAreaReservation();", dock, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.SHAppBarMessage(AbmNew, ref registration)", overlayManager, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.SHAppBarMessage(AbmQueryPos, ref data);", overlayManager, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.SHAppBarMessage(AbmSetPos, ref data);", overlayManager, StringComparison.Ordinal);
        Assert.Contains("preexistingBottomWorkAreaPixels", overlayManager, StringComparison.Ordinal);
        Assert.Contains("desiredTotalPixels - preexistingBottomWorkAreaPixels", overlayManager, StringComparison.Ordinal);
        Assert.Contains("info.Monitor.Bottom - info.Work.Bottom", overlayManager, StringComparison.Ordinal);
        Assert.Contains("ClearBottomWorkAreaReservation();", overlayManager, StringComparison.Ordinal);
    }
}
