using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilitiesGlyphGeometryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Physical_geometry_preserves_original_rounding(double dpi)
    {
        var geometry = UtilitiesGlyphGeometry.Create(73.3, 21.7, dpi, 6);
        var raster = (float)dpi;
        var bleed = MathF.Ceiling(6 * raster);
        Assert.Equal(MathF.Ceiling(73.3f * raster) + bleed * 2, geometry.PixelSize.X);
        Assert.Equal(MathF.Ceiling(21.7f * raster) + bleed * 2, geometry.PixelSize.Y);
        Assert.Equal(geometry, UtilitiesGlyphGeometry.Create(73.3, 21.7, dpi, 6));
    }

    [Fact]
    public void Dpi_or_physical_extent_changes_invalidate_but_same_pixel_extent_does_not()
    {
        var original = UtilitiesGlyphGeometry.Create(73.1, 21.1, 1, 6);
        Assert.Equal(original, UtilitiesGlyphGeometry.Create(73.2, 21.2, 1, 6));
        Assert.NotEqual(original, UtilitiesGlyphGeometry.Create(74.1, 21.1, 1, 6));
        Assert.NotEqual(original, UtilitiesGlyphGeometry.Create(73.1, 22.1, 1, 6));
        Assert.NotEqual(original, UtilitiesGlyphGeometry.Create(73.1, 21.1, 1.25, 6));
    }
}
