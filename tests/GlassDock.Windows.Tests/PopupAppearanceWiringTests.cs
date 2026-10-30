using Xunit;

namespace GlassDock.Windows.Tests;

/// <summary>Guards the popup theme path from silently reverting to the old,
/// separate Material-only setting when appearance modes are changed.</summary>
public sealed class PopupAppearanceWiringTests
{
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

        // The solid dock and popup surfaces share one palette.
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
