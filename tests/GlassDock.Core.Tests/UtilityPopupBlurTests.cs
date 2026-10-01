using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilityPopupBlurTests
{
    private static DockAppearanceSettings Appearance(double blur) => new(
        GlassMaterialMode.Clear,
        GlassDockSettings.DefaultIconSize,
        GlassDockSettings.DefaultMagnificationScale,
        GlassDockSettings.DefaultIconSpacing,
        blur,
        GlassDockSettings.DefaultDockOpacity,
        GlassDockSettings.DefaultBorderThickness,
        GlassDockSettings.DefaultBorderOpacity);

    [Theory]
    [InlineData(DockAppearanceMode.Frosted, 0)]
    [InlineData(DockAppearanceMode.Acrylic, 0)]
    [InlineData(DockAppearanceMode.Frosted, 20)]
    [InlineData(DockAppearanceMode.Acrylic, 10)]
    public void Glass_popups_use_the_exact_dock_blur(
        DockAppearanceMode mode, double blur)
    {
        var result = UtilityMaterial.CreateForPopup(Appearance(blur), mode);
        Assert.Equal(blur, result.BlurAmount);
        Assert.Equal(GlassDockSettings.DefaultDockOpacity, result.Opacity);
    }

    [Theory]
    [InlineData(DockAppearanceMode.Frosted)]
    [InlineData(DockAppearanceMode.Acrylic)]
    public void Stronger_user_blur_is_preserved(DockAppearanceMode mode)
    {
        Assert.Equal(52, UtilityMaterial.CreateForPopup(Appearance(52), mode).BlurAmount);
    }

    [Theory]
    [InlineData(DockAppearanceMode.Dark)]
    [InlineData(DockAppearanceMode.Light)]
    [InlineData(DockAppearanceMode.Clear)]
    public void Solid_and_clear_popups_are_unchanged(DockAppearanceMode mode)
    {
        var result = UtilityMaterial.CreateForPopup(Appearance(3), mode);
        Assert.Equal(3, result.BlurAmount);
        Assert.Equal(GlassDockSettings.DefaultDockOpacity, result.Opacity);
    }

    [Theory]
    [InlineData(DockAppearanceMode.Frosted, GlassMaterialMode.Frosted)]
    [InlineData(DockAppearanceMode.Acrylic, GlassMaterialMode.Acrylic)]
    [InlineData(DockAppearanceMode.Clear, GlassMaterialMode.Clear)]
    public void Appearance_not_an_old_separate_material_setting_chooses_the_preset(
        DockAppearanceMode mode, GlassMaterialMode preset)
    {
        var result = UtilityMaterial.CreateForPopup(Appearance(52), mode);
        var expected = DockMaterialStylePresets.Create(preset);
        Assert.Equal(expected.Tint, result.Tint);
        Assert.Equal(expected.Saturation, result.Saturation);
        Assert.Equal(expected.ShadowOpacity, result.ShadowOpacity);
        Assert.Equal(expected.ShadowBlur, result.ShadowBlur);
        Assert.Equal(expected.ShadowOffset, result.ShadowOffset);
    }
}
