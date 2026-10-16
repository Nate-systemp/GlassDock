using System.Numerics;

namespace GlassDock.Core.Desktop;

/// <summary>Exact physical glyph-surface geometry; independent of root visibility notifications.</summary>
public readonly record struct UtilitiesGlyphGeometry(float Dpi, float BleedPixels, Vector2 PixelSize)
{
    public static UtilitiesGlyphGeometry Create(double width, double height, double dpi, float bleed)
    {
        var raster = (float)Math.Max(1, dpi);
        var margin = MathF.Ceiling(bleed * raster);
        return new(raster, margin, new(
            MathF.Max(1, MathF.Ceiling((float)width * raster)) + margin * 2,
            MathF.Max(1, MathF.Ceiling((float)height * raster)) + margin * 2));
    }
}
