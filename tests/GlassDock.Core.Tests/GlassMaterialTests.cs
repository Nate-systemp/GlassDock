using GlassDock.Core.Materials;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class GlassMaterialTests
{
    [Theory]
    [InlineData(GlassMaterialPreset.Frosted)]
    [InlineData(GlassMaterialPreset.Clear)]
    [InlineData(GlassMaterialPreset.Refractive)]
    public void Presets_are_valid_without_claiming_refraction(GlassMaterialPreset preset)
    {
        var material = GlassMaterialPresets.Create(preset);
        Assert.Equal(material, material.Normalize());
        Assert.Equal(0, material.RefractionAmount);
    }

    [Fact]
    public void Clear_preserves_more_detail_and_refractive_strengthens_lighting()
    {
        var frosted = GlassMaterialPresets.Create(GlassMaterialPreset.Frosted);
        var clear = GlassMaterialPresets.Create(GlassMaterialPreset.Clear);
        var refractive = GlassMaterialPresets.Create(GlassMaterialPreset.Refractive);
        Assert.True(clear.BlurAmount < frosted.BlurAmount);
        Assert.True(clear.Opacity < frosted.Opacity);
        Assert.True(clear.ShadowOpacity < frosted.ShadowOpacity);
        Assert.True(refractive.EdgeHighlight > frosted.EdgeHighlight);
    }

    [Fact]
    public void Invalid_input_is_bounded_before_native_rendering()
    {
        var normalized = new GlassMaterial
        {
            BlurAmount = -1, Opacity = 2, Saturation = -5, Brightness = 0,
            Tint = 0xFFFFFFFF, BorderOpacity = -1, BorderThickness = 99,
            CornerRadius = -50, ShadowOpacity = 8, ShadowBlur = 200,
            ShadowOffset = -100, RefractionAmount = 2, EdgeHighlight = -1
        }.Normalize();
        Assert.Equal(0, normalized.BlurAmount);
        Assert.Equal(1, normalized.Opacity);
        Assert.Equal(0, normalized.Saturation);
        Assert.Equal(.25, normalized.Brightness);
        Assert.Equal(0xFFFFFFu, normalized.Tint);
        Assert.Equal(0, normalized.BorderOpacity);
        Assert.Equal(6, normalized.BorderThickness);
        Assert.Equal(0, normalized.CornerRadius);
        Assert.Equal(1, normalized.ShadowOpacity);
        Assert.Equal(100, normalized.ShadowBlur);
        Assert.Equal(-40, normalized.ShadowOffset);
        Assert.Equal(1, normalized.RefractionAmount);
        Assert.Equal(0, normalized.EdgeHighlight);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Nonfinite_values_recover_to_finite_defaults(double value)
    {
        var actual = new GlassMaterial
        {
            BlurAmount = value, Opacity = value, Saturation = value, Brightness = value,
            BorderOpacity = value, BorderThickness = value, CornerRadius = value,
            ShadowOpacity = value, ShadowBlur = value, ShadowOffset = value,
            RefractionAmount = value, EdgeHighlight = value
        }.Normalize();
        Assert.Equal(new GlassMaterial(), actual);
        Assert.Equal(actual, actual.Normalize());
    }

    [Fact]
    public void Unknown_preset_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GlassMaterialPresets.Create((GlassMaterialPreset)99));
}
