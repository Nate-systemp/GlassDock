using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockAppearanceRuntimeWiringTests
{
    [Fact]
    public void Applied_appearance_reconfigures_layout_and_uses_opaque_plain_dock()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var dock = File.ReadAllText(Path.Combine(
            directory.FullName,
            "src",
            "GlassDock.App",
            "Desktop",
            "DesktopOverlayWindow.cs")).ReplaceLineEndings("\n");
        var animation = File.ReadAllText(Path.Combine(
            directory.FullName,
            "src",
            "GlassDock.App",
            "Desktop",
            "DockAnimationController.cs")).ReplaceLineEndings("\n");

        Assert.Contains("icons.Spacing = appearance.IconSpacing;", dock, StringComparison.Ordinal);
        // Responsive dock sizing must remain wired to the current appearance geometry
        // without relying on the legacy app-only TargetDockWidth cap.
        Assert.Contains("count * Appearance.ButtonWidth", dock, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0, count - 1) * Appearance.IconSpacing", dock, StringComparison.Ordinal);
        Assert.Contains("windowManager.CurrentHostWidthDips", dock, StringComparison.Ordinal);
        Assert.Contains("applicationWidth + UtilityClusterWidth", dock, StringComparison.Ordinal);
        Assert.Contains("new AdaptiveAppIcon(Appearance.IconSize, Appearance.MagnificationScale, showTile: false)", dock, StringComparison.Ordinal);
        Assert.Contains("icon.Configure(appearance.IconSize, appearance.MagnificationScale);", dock, StringComparison.Ordinal);
        Assert.Contains("animation.SetMaximumMagnificationScale(appearance.MagnificationScale);", dock, StringComparison.Ordinal);
        Assert.Contains("UseSolidSurface = true", dock, StringComparison.Ordinal);
        Assert.Contains("desktopBackdrop.SetSolidAppearance(mode, opacity, cornerRadius);", dock, StringComparison.Ordinal);
        Assert.Contains("const double opacity = 1;", dock, StringComparison.Ordinal);
        Assert.Contains("dockWaveRim.StrokeThickness = 0;", dock, StringComparison.Ordinal);
        Assert.Contains("cachedSystemQuickSettingsWindow?.ApplyAppearance(appearance, dockAppearance);", dock, StringComparison.Ordinal);
        Assert.Contains("cachedSystemTrayWindow?.ApplyAppearance(appearance, dockAppearance);", dock, StringComparison.Ordinal);
        Assert.Contains("cachedCalendarPopoverWindow?.ApplyAppearance(appearance, dockAppearance);", dock, StringComparison.Ordinal);
        Assert.Contains("double targetHeight = 68", animation, StringComparison.Ordinal);
        Assert.Contains("maximumMagnificationScale - 1", animation, StringComparison.Ordinal);
    }
}
