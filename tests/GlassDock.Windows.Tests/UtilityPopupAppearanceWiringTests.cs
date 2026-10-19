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

        Assert.Contains("cachedSystemQuickSettingsWindow?.ApplyAppearance(appearance, utilityMode)", owner,
            StringComparison.Ordinal);
        Assert.Contains("cachedSystemTrayWindow?.ApplyAppearance(appearance, utilityMode)", owner,
            StringComparison.Ordinal);
        Assert.Contains("cachedCalendarPopoverWindow?.ApplyAppearance(appearance, utilityMode)", owner,
            StringComparison.Ordinal);
        Assert.Contains("mode.GlassStyle()", style, StringComparison.Ordinal);

        Assert.Contains("DockControlPalette.Foreground(mode)", theme, StringComparison.Ordinal);
        Assert.Contains("DockControlPalette.Hover(mode, true)", theme, StringComparison.Ordinal);
        Assert.Contains("DockControlPalette.Hover(mode, selected)", owner, StringComparison.Ordinal);
        Assert.Contains("_ => Color.FromArgb(0, 0, 0, 0)", theme, StringComparison.Ordinal);
    }
    [Fact]
    public void Floating_pill_and_all_utility_popups_follow_the_current_dock_appearance()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var desktop = Path.Combine(directory.FullName, "src", "GlassDock.App", "Desktop");
        var pill = File.ReadAllText(Path.Combine(desktop, "FloatingUtilitiesWindow.cs"));
        var owner = File.ReadAllText(Path.Combine(desktop, "DesktopOverlayWindow.cs"));

        // The current settings are passed into the floating pill on initial
        // creation and every subsequent SettingsChanged notification.
        Assert.Contains("floatingUtilities.Apply(current, Appearance);", owner, StringComparison.Ordinal);
        Assert.Contains("var mode = value.DockAppearanceMode;", pill, StringComparison.Ordinal);
        Assert.Contains("theme.Apply(mode);", pill, StringComparison.Ordinal);
        Assert.Contains("root.RequestedTheme = mode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;", pill, StringComparison.Ordinal);
        Assert.Contains("UtilityPopupStyle.ApplyDockMatched(glass, backdrop, appearance, mode,", pill, StringComparison.Ordinal);
        Assert.DoesNotContain("PillMode", pill, StringComparison.Ordinal);

        // Every placement (Inside, Floating, Split) resolves to the same mode.
        Assert.Contains("private DockAppearanceMode UtilityPopupMode(DockAppearanceMode dockMode) => dockMode;", owner, StringComparison.Ordinal);
        Assert.DoesNotContain("utilitiesPlacement == UtilitiesPlacement.InsideDock ? dockMode : DockAppearanceMode.Frosted", owner, StringComparison.Ordinal);
        Assert.Contains("UtilityPopupMode(settingsSession.Current.DockAppearanceMode)", owner, StringComparison.Ordinal);
    }

    [Fact]
    public void All_utility_placements_use_the_same_control_center_layout_without_changing_popup_anchors()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var desktop = Path.Combine(directory!.FullName, "src", "GlassDock.App", "Desktop");
        var owner = File.ReadAllText(Path.Combine(desktop, "DesktopOverlayWindow.cs"));
        var quickSettings = File.ReadAllText(Path.Combine(desktop, "SystemQuickSettingsWindow.cs"));

        // One compact UI is used for Inside Dock, Floating Button, and Split Dock.
        Assert.Contains("quickSettings.SetDetachedLayout(true);", owner, StringComparison.Ordinal);
        Assert.DoesNotContain("SetDetachedLayout(utilitiesPlacement != UtilitiesPlacement.InsideDock)", owner,
            StringComparison.Ordinal);
        Assert.Contains("if (value) BuildDetachedModules();", quickSettings, StringComparison.Ordinal);

        // The floating pill opens below/right, while dock-embedded buttons
        // still open above the dock, despite sharing the same content layout.
        Assert.Contains("belowRight: true", owner, StringComparison.Ordinal);
        Assert.Contains("bool belowRight = false", quickSettings, StringComparison.Ordinal);
        Assert.Contains("detached ? UnifiedPanelHeight : PanelHeight, belowRight", quickSettings,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_control_center_uses_the_redesigned_glass_tiles_and_native_sliders()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var desktop = Path.Combine(directory!.FullName, "src", "GlassDock.App", "Desktop");
        var popup = File.ReadAllText(Path.Combine(desktop, "SystemQuickSettingsWindow.cs"));
        var shared = File.ReadAllText(Path.Combine(desktop, "SystemQuickSettingsWindow.Detached.cs"));
        var theme = File.ReadAllText(Path.Combine(desktop, "UtilityPopupTheme.cs"));

        Assert.Contains("UnifiedPanelWidth = 294, UnifiedPanelHeight = 470", popup);
        Assert.Contains("new[] { 22d, 108d, 40d, 80d, 48d, 48d, 24d }", shared);
        Assert.Contains("BuildMediaCard()", shared);
        Assert.Contains("AddHorizontalSlider(brightness", shared);
        Assert.Contains("AddHorizontalSlider(volumeSlider", shared);
        Assert.Contains("slider.Orientation = Orientation.Horizontal", shared);
        Assert.Contains("slider.Opacity = 1", shared);
        Assert.Contains("pair.Key.ValueChanged -= handler", shared);
        Assert.Contains("theme.EnabledIndicator", popup);
        Assert.Contains("DockControlPalette.SolidSurface(mode)", theme);
    }

    [Fact]
    public void Calendar_and_hidden_tray_already_share_their_windows_across_placements()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var owner = File.ReadAllText(Path.Combine(directory!.FullName, "src", "GlassDock.App",
            "Desktop", "DesktopOverlayWindow.cs"));
        // All placements create one shared calendar UI, using the Split Dock's
        // compact vertical layout. The Split placement retains its own anchor.
        Assert.Contains("new CalendarPopoverWindow(", owner, StringComparison.Ordinal);
        Assert.Contains("compactSplit: true", owner, StringComparison.Ordinal);
        Assert.DoesNotContain("compactSplit: utilitiesPlacement == UtilitiesPlacement.SplitDock", owner,
            StringComparison.Ordinal);
        Assert.Contains("calendarWindow.PositionNearSplitClock", owner, StringComparison.Ordinal);

        // There is only one Hidden Tray popup class and one creation path across
        // Inside Dock, Floating Button, and Split Dock. Retain its main-dock
        // appearance theme and existing tray activation/drag interactions.
        var tray = File.ReadAllText(Path.Combine(directory.FullName, "src", "GlassDock.App",
            "Desktop", "SystemTrayWindow.cs"));
        Assert.Equal(1, owner.Split("new SystemTrayWindow(").Length - 1);
        Assert.Contains("UtilityPopupStyle.Apply(glass, backdrop, appearance, mode)", tray,
            StringComparison.Ordinal);
        Assert.Contains("trayGrid.ChildrenTransitions", tray, StringComparison.Ordinal);
        Assert.Contains("private const double PanelWidth = 352", tray, StringComparison.Ordinal);
        Assert.Contains("private const double PanelHeight = 406", tray, StringComparison.Ordinal);
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
