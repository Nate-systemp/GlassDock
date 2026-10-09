using Xunit;

namespace GlassDock.Windows.Tests;

/// <summary>Guards the popup theme path from silently reverting to the old,
/// separate Material-only setting when appearance modes are changed.</summary>
public sealed class UtilityPopupAppearanceWiringTests
{
    [Fact]
    public void Liquid_popups_share_renderer_and_release_hidden_capture_without_sampling_controls()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var app = Path.Combine(directory.FullName, "src", "GlassDock.App");
        foreach (var name in new[] { "CalendarPopoverWindow", "SystemQuickSettingsWindow", "SystemTrayWindow",
            "DockAppContextMenuWindow", "DockStackWindow", "WindowPreviewWindow" })
            Assert.Contains("liquid = new(this, root, backdrop)", File.ReadAllText(Path.Combine(app, "Desktop", name + ".cs")));
        var adapter = File.ReadAllText(Path.Combine(app, "Rendering", "PopupLiquidGlassSurface.cs"));
        Assert.Contains("DokyLiquidGlassSurface liquid", adapter);
        Assert.Contains("window.AppWindow.IsVisible", adapter);
        Assert.Contains("backdrop.PopupMode == DockAppearanceMode.Clear", adapter);
        Assert.Contains("liquid.Dispose()", adapter);
        Assert.DoesNotContain("Thumbnail", File.ReadAllText(Path.Combine(app, "Rendering", "DokyLiquidGlassSurface.cs")));
        var previews = File.ReadAllText(Path.Combine(app, "Desktop", "WindowPreviewWindow.cs"));
        Assert.Equal(2, previews.Split("liquid.Attach();").Length - 1);
    }

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

        // The solid dock is #242424 dark and warm #D8CCB8 light. Don't let utility
        // popups quietly revert to a separate navy material/theme.
        var palette = File.ReadAllText(Path.Combine(directory.FullName, "src",
            "GlassDock.App", "Desktop", "DockControlPalette.cs"));
        Assert.Contains("DockControlPalette.SolidSurface(mode)", theme, StringComparison.Ordinal);
        Assert.Contains("Color.FromArgb(alpha, 36, 36, 36)", palette, StringComparison.Ordinal);
        Assert.Contains("Color.FromArgb(alpha, 216, 204, 184)", palette, StringComparison.Ordinal);
        Assert.Contains("DockControlPalette.SolidSurface(appearance, alpha)", backdrop, StringComparison.Ordinal);
        var lens = File.ReadAllText(Path.Combine(directory.FullName, "src",
            "GlassDock.App", "Rendering", "DokyLiquidGlassSurface.cs"));
        var surface = File.ReadAllText(Path.Combine(directory.FullName, "src",
            "GlassDock.App", "Controls", "GlassSurface.xaml.cs"));
        Assert.Contains("DockControlPalette.SolidSurface(appearanceMode)", lens, StringComparison.Ordinal);
        Assert.Contains("DockControlPalette.SolidSurface(plainAppearance,", surface, StringComparison.Ordinal);
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
