using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockAppMenuWiringTests
{
    [Fact]
    public void AppMenuUsesDockGlassWithPreviewHoldAndStaleStateProtection()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var desktop = Path.Combine(directory.FullName, "src", "GlassDock.App", "Desktop");
        var window = File.ReadAllText(Path.Combine(desktop, "DockAppContextMenuWindow.cs"));
        var coordinator = File.ReadAllText(Path.Combine(desktop, "WindowPreviewCoordinator.cs"));
        Assert.Contains("UtilityPopupStyle.Apply(glass, backdrop, appearance, mode,", window);
        Assert.Contains("theme.StyleButton(button)", window);
        Assert.Contains("icon.SetIcon(item.Application.Icon)", window);
        Assert.True(window.IndexOf("host.Configure()", StringComparison.Ordinal) < window.IndexOf("SystemBackdrop = backdrop", StringComparison.Ordinal));
        Assert.Contains("WindowPreviewLayout.Position", window);
        Assert.Contains("WindowActivationState.Deactivated", window);
        Assert.Contains("VirtualKey.Escape", window);
        Assert.Contains("revision != generation", window);
        Assert.Contains("BeginContextMenu(appMenu)", coordinator);
        Assert.Contains("Hide(immediate: true)", coordinator);
        Assert.Contains("!snapshot.State.Matches(current.Application)", coordinator);
        Assert.Contains("appMenu?.ApplyAppearance(settings.Appearance", coordinator);
        Assert.Contains("appMenu?.Close()", coordinator);
        // The menu must remain visible when Snipping Tool takes focus, and
        // Clear-mode popup capture must be frozen before exposing its HWND.
        var dock = File.ReadAllText(Path.Combine(desktop, "DesktopOverlayWindow.cs"));
        var popup = File.ReadAllText(Path.Combine(directory.FullName, "src", "GlassDock.App", "Rendering", "PopupLiquidGlassSurface.cs"));
        Assert.Contains("snippingCaptureActive", window);
        Assert.Contains("await Task.Delay(180)", window);
        Assert.Contains("liquid.BeginScreenshotMode();", window);
        Assert.Contains("liquid.EndScreenshotMode();", window);
        Assert.Contains("public bool BeginScreenshotMode()", popup);
        Assert.Contains("liquid.BeginScreenshotMode()", popup);
        Assert.Contains("previews.BeginSnippingCapture();", dock);
        Assert.Contains("liquidGlass.BeginScreenshotMode();", dock);
        Assert.Contains("liquidGlass.EndScreenshotMode();", dock);
        Assert.Contains("previews.EndSnippingCapture();", dock);
        Assert.Contains("appMenu?.BeginSnippingCapture();", coordinator);
        Assert.Contains("appMenu?.EndSnippingCapture();", coordinator);
        Assert.True(dock.IndexOf("previews.BeginSnippingCapture();", StringComparison.Ordinal) <
                    dock.IndexOf("return liquidGlass.BeginScreenshotMode();", StringComparison.Ordinal));
        Assert.True(dock.IndexOf("liquidGlass.EndScreenshotMode();", StringComparison.Ordinal) <
                    dock.IndexOf("previews.EndSnippingCapture();", StringComparison.Ordinal));
        Assert.Contains("if (!snippingCaptureActive) CheckMenuSnapshot();", coordinator);
        Assert.Contains("if (e.WindowActivationState != WindowActivationState.Deactivated || snippingCaptureActive) return;", window);
    }

    [Fact]
    public void GlassAppMenuMatchesSilverJumpListReferenceWithoutChangingSolidThemes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var window = File.ReadAllText(Path.Combine(directory.FullName, "src", "GlassDock.App", "Desktop", "DockAppContextMenuWindow.cs"));

        // Geometry and hover are shared across all five appearance modes.
        Assert.Contains("MenuCornerRadius = 9;", window, StringComparison.Ordinal);
        Assert.Contains("MenuWidth = 258;", window, StringComparison.Ordinal);
        Assert.Contains("MenuRowHeight = 36;", window, StringComparison.Ordinal);
        Assert.Contains("MenuRowRadius = 5;", window, StringComparison.Ordinal);
        Assert.Contains("Height = MenuRowHeight - 4", window, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment = VerticalAlignment.Center", window, StringComparison.Ordinal);
        Assert.Contains("Segoe UI Variable Text", window, StringComparison.Ordinal);
        Assert.Contains("menuAccent.Color = global::Windows.UI.Color.FromArgb(255, 24, 126, 247);", window, StringComparison.Ordinal);
        Assert.Contains("referenceGlassFill.GradientStops[1].Color", window, StringComparison.Ordinal);
        Assert.Contains("chrome.Background = referenceGlassFill;", window, StringComparison.Ordinal);
        Assert.Contains("theme.Divider.Color = global::Windows.UI.Color.FromArgb(128, 103, 117, 137);", window, StringComparison.Ordinal);

        // Solid Dark/Light retain their original theme palette, screenshot
        // visibility stays wired, and all menu command callbacks are unchanged.
        Assert.Contains("chrome.Background = theme.Overlay;", window, StringComparison.Ordinal);
        Assert.Contains("root.RequestedTheme = mode == DockAppearanceMode.Dark ? ElementTheme.Dark : ElementTheme.Light;", window, StringComparison.Ordinal);
        Assert.Contains("liquid.BeginScreenshotMode();", window, StringComparison.Ordinal);
        Assert.Contains("liquid.EndScreenshotMode();", window, StringComparison.Ordinal);
        Assert.Contains("entry.Invoke?.Invoke();", window, StringComparison.Ordinal);
        // The dock must flatten its hover crest while a menu is open so
        // the two separately rounded windows never form a visual bridge.
        var dock = File.ReadAllText(Path.Combine(directory.FullName, "src", "GlassDock.App", "Desktop", "DesktopOverlayWindow.cs"));
        var holdStart = dock.IndexOf("private void OnInteractionHoldChanged()", StringComparison.Ordinal);
        var holdEnd = dock.IndexOf("private void RefreshHoverVisuals()", StringComparison.Ordinal);
        Assert.True(holdStart >= 0 && holdEnd > holdStart);
        var hold = dock[holdStart..holdEnd];
        Assert.Contains("dockWaveCurrentStrength = 0;", hold, StringComparison.Ordinal);
        Assert.Contains("dockWaveTargetStrength = 0;", hold, StringComparison.Ordinal);
        Assert.Contains("desktopBackdrop.ClearDockWave();", hold, StringComparison.Ordinal);
        Assert.Contains("UpdateLiquidGlass();", hold, StringComparison.Ordinal);
    }
}
