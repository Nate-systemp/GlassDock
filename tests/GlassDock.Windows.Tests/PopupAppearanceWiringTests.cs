using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class PopupAppearanceWiringTests
{
    [Fact]
    public void Grid_utilities_preserve_the_individual_frosted_and_acrylic_material()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var button = File.ReadAllText(Path.Combine(directory.FullName,
            "src", "GlassDock.App", "Controls", "UtilityButton.cs"));
        var surface = File.ReadAllText(Path.Combine(directory.FullName,
            "src", "GlassDock.App", "Rendering", "UtilityGlassSurface.cs"));
        Assert.Contains("Glass.UseDockMaterial = !displayOn && (sharedGlass || !FloatingPresentation && RadioUtility);", button);
        Assert.Contains("var physical = UseDockMaterial ? RefractionLayer.ForNativeBackend(backdrop)", surface);
        Assert.Contains("if (UseDockMaterial && mode is DockAppearanceMode.Frosted or DockAppearanceMode.Acrylic) return;", surface);
        Assert.Contains("? material.SurfaceOpacity : 1;", surface);
        foreach (var name in new[] { "QuickSettingsGridButton.cs", "VerticalUtilityCapsule.cs" })
        {
            var control = File.ReadAllText(Path.Combine(directory.FullName,
                "src", "GlassDock.App", "Controls", name));
            Assert.Contains("Glass.UseDockMaterial = mode is DockAppearanceMode.Frosted or DockAppearanceMode.Acrylic;", control);
            Assert.Contains("? Microsoft.UI.Colors.Transparent :", control);
        }
        var popup = File.ReadAllText(Path.Combine(directory.FullName,
            "src", "GlassDock.App", "Desktop", "UtilityPopupWindow.cs"));
        var panel = File.ReadAllText(Path.Combine(directory.FullName,
            "src", "GlassDock.App", "Controls", "QuickSettingsPanel.cs"));
        var utilities = File.ReadAllText(Path.Combine(directory.FullName,
            "src", "GlassDock.App", "Desktop", "DesktopOverlayWindow.Utilities.cs"));
        Assert.Contains("transparentBackdrop.ApplyMainDock(style, backdrop);", popup);
        Assert.Contains("transparentBackdrop.SetSurfaceRegions([]);", popup);
        Assert.Contains("quick.UseNativeBackdrop = !transparentBackdrop.UseSolidSurface;", popup);
        Assert.Contains("transparentBackdrop.SetUtilitySurfaces(nativePanel.UseNativeBackdrop ? nativePanel.NativeGlassSurfaces(root) : []);", popup);
        Assert.Contains("glass.Active = visible && !native;", panel);
        Assert.Contains("if (native) glass.Hide();", panel);
        Assert.Contains("quickSettingsButton is { IsLoaded: true } grid && grid.Glass.UsesNativeDockBackdrop", utilities);
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
            "GlassDock.App", "Desktop", "PopupTheme.cs"));
        var backdrop = File.ReadAllText(Path.Combine(directory.FullName, "src",
            "GlassDock.App", "Rendering", "DesktopGlassBackdrop.cs"));

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
            "GlassDock.App", "Desktop", "PopupStyle.cs"));
        Assert.Contains("backdrop.ApplyMainDock(style, material", style, StringComparison.Ordinal);
        Assert.Contains("backdrop.UseInnerEdge = true", style, StringComparison.Ordinal);
        Assert.Contains("backdrop.SetSolidAppearance(mode", style, StringComparison.Ordinal);
    }

}
