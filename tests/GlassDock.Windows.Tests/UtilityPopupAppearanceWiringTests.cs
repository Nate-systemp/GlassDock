using Xunit;

namespace GlassDock.Windows.Tests;

/// <summary>Guards the popup theme path from silently reverting to the old,
/// separate Material-only setting when appearance modes are changed.</summary>
public sealed class UtilityPopupAppearanceWiringTests
{
    [Fact]
    public void All_popup_windows_receive_the_current_dock_appearance()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var desktop = Path.Combine(directory.FullName, "src", "GlassDock.App", "Desktop");
        var owner = File.ReadAllText(Path.Combine(desktop, "DesktopOverlayWindow.cs"));
        var theme = File.ReadAllText(Path.Combine(desktop, "UtilityPopupTheme.cs"));
        var style = File.ReadAllText(Path.Combine(desktop, "UtilityPopupStyle.cs"));

        foreach (var target in new[] { "SystemQuickSettingsWindow", "SystemTrayWindow", "CalendarPopoverWindow" })
        {
            var popup = File.ReadAllText(Path.Combine(desktop, target + ".cs"));
            Assert.Contains("ApplyAppearance(DockAppearanceSettings appearance, DockAppearanceMode mode)", popup,
                StringComparison.Ordinal);
            Assert.Contains("UtilityPopupStyle.Apply(glass, backdrop, appearance, mode)", popup,
                StringComparison.Ordinal);
        }

        Assert.Contains("cachedSystemQuickSettingsWindow?.ApplyAppearance(appearance, dockAppearance)", owner,
            StringComparison.Ordinal);
        Assert.Contains("cachedSystemTrayWindow?.ApplyAppearance(appearance, dockAppearance)", owner,
            StringComparison.Ordinal);
        Assert.Contains("cachedCalendarPopoverWindow?.ApplyAppearance(appearance, dockAppearance)", owner,
            StringComparison.Ordinal);
        Assert.Contains("mode.GlassStyle()", style, StringComparison.Ordinal);

        Assert.Contains("DockControlPalette.Foreground(mode)", theme, StringComparison.Ordinal);
        Assert.Contains("DockControlPalette.Hover(mode, true)", theme, StringComparison.Ordinal);
        Assert.Contains("DockControlPalette.Hover(mode, selected)", owner, StringComparison.Ordinal);
        Assert.Contains("_ => Color.FromArgb(0, 0, 0, 0)", theme, StringComparison.Ordinal);
    }
    [Fact]
    public void Solid_popup_palette_matches_the_main_dock_finishes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var theme = File.ReadAllText(Path.Combine(directory.FullName, "src",
            "GlassDock.App", "Desktop", "UtilityPopupTheme.cs"));
        var backdrop = File.ReadAllText(Path.Combine(directory.FullName, "src",
            "GlassDock.App", "Rendering", "DesktopGlassBackdrop.cs"));

        // The solid dock is #242424 dark and #F3F3F3 light. Don't let utility
        // popups quietly revert to a separate navy material/theme.
        Assert.Contains("Color.FromArgb(255, 36, 36, 36)", theme, StringComparison.Ordinal);
        Assert.Contains("Color.FromArgb(255, 243, 243, 243)", theme, StringComparison.Ordinal);
        Assert.Contains("alpha, 36, 36, 36", backdrop, StringComparison.Ordinal);
        Assert.Contains("243, 243, 243", backdrop, StringComparison.Ordinal);
    }

    [Fact]
    public void Popup_glass_uses_the_same_material_branch_as_the_main_dock()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var style = File.ReadAllText(Path.Combine(directory.FullName, "src",
            "GlassDock.App", "Desktop", "UtilityPopupStyle.cs"));
        Assert.Contains("backdrop.ApplyMainDock(style, material", style, StringComparison.Ordinal);
        Assert.Contains("backdrop.UseInnerEdge = true", style, StringComparison.Ordinal);
        Assert.Contains("backdrop.SetSolidAppearance(mode", style, StringComparison.Ordinal);
    }

}
