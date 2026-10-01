using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilityMaterialTests
{
    [Theory]
    [InlineData(GlassMaterialMode.Frosted)]
    [InlineData(GlassMaterialMode.Acrylic)]
    [InlineData(GlassMaterialMode.Clear)]
    public void Popup_inherits_dock_material_and_user_overrides(GlassMaterialMode mode)
    {
        var session = new GlassDockSettingsSession(new()
        {
            GlassMaterialMode = mode, GlassBlurAmount = 9, DockOpacity = .52,
            BorderThickness = .6, BorderOpacity = .3
        });
        var material = UtilityMaterial.Create(session.Appearance);
        var preset = DockMaterialStylePresets.Create(mode);
        Assert.Equal(9, material.BlurAmount);
        Assert.Equal(.52, material.Opacity);
        Assert.Equal(.6, material.BorderThickness);
        Assert.Equal(.3, material.BorderOpacity);
        Assert.Equal(preset.Tint, material.Tint);
        Assert.Equal(preset.Saturation, material.Saturation);
        Assert.Equal(28, material.CornerRadius);
        Assert.Equal(preset.ShadowOpacity, material.ShadowOpacity);
        Assert.Equal(preset.ShadowBlur, material.ShadowBlur);
        Assert.Equal(0, material.EdgeHighlight);
        var borderless = UtilityMaterial.Create(session.Appearance with { BorderThickness = 0, BorderOpacity = 0 });
        Assert.Equal(0, borderless.BorderThickness);
        Assert.Equal(0, borderless.BorderOpacity);
    }
}
