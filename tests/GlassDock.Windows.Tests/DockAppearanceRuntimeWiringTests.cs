using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockAppearanceRuntimeWiringTests
{
    [Fact]
    public void Applied_appearance_reconfigures_layout_magnification_and_main_glass_material()
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
        Assert.Contains("Appearance.TargetDockWidth(VisibleDockApplications.Count)", dock, StringComparison.Ordinal);
        Assert.Contains("new AdaptiveAppIcon(Appearance.IconSize, Appearance.MagnificationScale)", dock, StringComparison.Ordinal);
        Assert.Contains("icon.Configure(appearance.IconSize, appearance.MagnificationScale);", dock, StringComparison.Ordinal);
        Assert.Contains("animation.SetMaximumMagnificationScale(appearance.MagnificationScale);", dock, StringComparison.Ordinal);
        Assert.Contains("Appearance.ApplyTo(", dock, StringComparison.Ordinal);
        Assert.Contains("DockMaterialStylePresets.Create(Appearance.GlassMaterialMode)", dock, StringComparison.Ordinal);
        Assert.Contains("BorderThickness = expanded ? Appearance.BorderThickness : 1", dock, StringComparison.Ordinal);
        Assert.Contains("dockWaveRim.StrokeThickness = appearance.BorderThickness;", dock, StringComparison.Ordinal);
        Assert.Contains("dockWaveRim.Opacity = appearance.BorderOpacity;", dock, StringComparison.Ordinal);
        Assert.Contains("double targetHeight = 68", animation, StringComparison.Ordinal);
        Assert.Contains("maximumMagnificationScale - 1", animation, StringComparison.Ordinal);
    }
}
