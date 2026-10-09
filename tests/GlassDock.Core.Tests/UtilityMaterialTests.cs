using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilityMaterialTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 45)]
    [InlineData(20, 270)]
    public void Popup_optics_follow_saved_dock_controls(double refraction, double angle)
    {
        var session = new GlassDockSettingsSession(new()
        { ClearRefractionStrength = refraction, SpecularHighlightAngle = angle });
        var optics = session.Appearance.LiquidOptics;
        Assert.Equal((float)refraction, optics.RefractionStrength);
        Assert.Equal((float)angle, optics.SpecularAngleDegrees);
        Assert.Equal(new LiquidGlassMaterial().ChromaticDispersion, optics.ChromaticDispersion);
        Assert.Equal(new LiquidGlassMaterial().DiffusionAmount, optics.DiffusionAmount);
    }

    [Fact]
    public void Popup_optics_normalize_untrusted_appearance_values()
    {
        var appearance = new GlassDockSettingsSession(new()).Appearance with
        { ClearRefractionStrength = double.PositiveInfinity, SpecularHighlightAngle = double.NaN };
        Assert.Equal(new LiquidGlassMaterial(), appearance.LiquidOptics);
    }

    [Fact]
    public void Popup_rim_is_more_visible_without_changing_refraction_or_highlight_direction()
    {
        var dock = new LiquidGlassMaterial { RefractionStrength = 17, SpecularAngleDegrees = 135 };
        var popup = UtilityMaterial.PopupOptics(dock);
        Assert.Equal(.26f, popup.SpecularIntensity);
        Assert.Equal(dock, popup with { SpecularIntensity = dock.SpecularIntensity });
    }

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
