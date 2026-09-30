using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class DockMaterialRenderingTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(400)]
    public void Partial_rim_uses_the_lifted_wave_instead_of_a_flat_resting_line(double center)
    {
        var outline = GlassDock.Core.Desktop.DockWaveGeometry.Create(0, 50, 500, 60, 28, center, 60, 18, 1);
        var slice = GlassDock.Core.Desktop.DockEdgeSlice.Upper(outline, center - 20, center + 20);
        Assert.Equal(center - 20, slice.Start.X, 5);
        Assert.Equal(center + 20, slice.Segments[^1].End.X, 5);
        Assert.True(slice.Start.Y < 50);
        Assert.Contains(slice.Segments, s => Math.Abs(s.End.Y - 32) < .001);
    }

    [Theory]
    [InlineData(GlassMaterialMode.Frosted, 20, .78, 20)]
    [InlineData(GlassMaterialMode.Acrylic, 10, .58, 4)]
    [InlineData(GlassMaterialMode.Clear, 4, .42, 0)]
    public void Styles_keep_absolute_settings_and_distinct_backdrop_branches(GlassMaterialMode style, double blur, double opacity, double baseBlur)
    {
        var preset = DockMaterialStylePresets.Create(style);
        var appearance = new DockAppearanceSettings(style, 28, 1.24, 6, blur, opacity, .5, .25);
        var material = appearance.ApplyTo(preset, true);
        Assert.Equal(blur, material.BlurAmount);
        Assert.Equal(opacity, material.Opacity);
        Assert.Equal(preset.Saturation, material.Saturation);
        Assert.Equal(preset.Tint, material.Tint);
        Assert.Equal(baseBlur, DockMaterialRendering.BaseBlur(style, material.BlurAmount));
    }

    [Fact]
    public void Popup_and_laboratory_base_blur_is_unchanged() => Assert.Equal(22, DockMaterialRendering.BaseBlur(null, 22));

    [Fact]
    public void Clear_lighting_fades_smoothly_to_zero_before_bottom_and_respects_disabled_border()
    {
        var previous = DockMaterialRendering.SpecularAlpha(0, .25);
        Assert.InRange(previous, .4, .5);
        for (var i = 1; i <= 100; i++)
        {
            var alpha = DockMaterialRendering.SpecularAlpha(i / 100d, .25);
            Assert.InRange(alpha, 0, previous);
            Assert.True(previous - alpha < .02);
            previous = alpha;
        }
        Assert.Equal(0, DockMaterialRendering.SpecularAlpha(.8, 1));
        Assert.Equal(0, DockMaterialRendering.SpecularAlpha(1, 1));
        Assert.Equal(0, DockMaterialRendering.SpecularAlpha(0, 0));
    }
}
