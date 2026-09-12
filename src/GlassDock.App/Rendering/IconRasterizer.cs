using GlassDock.Core.Applications;

namespace GlassDock.App.Rendering;

/// <summary>Area-filter native premultiplied BGRA pixels before large display reductions.</summary>
internal static class IconRasterizer
{
    public static ApplicationIcon Downsample(ApplicationIcon source, int width, int height)
    {
        width = Math.Clamp(width, 1, source.Width);
        height = Math.Clamp(height, 1, source.Height);
        if (width == source.Width && height == source.Height) return source;
        var pixels = new byte[width * height * 4];
        var scaleX = (double)source.Width / width;
        var scaleY = (double)source.Height / height;
        var area = scaleX * scaleY;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var left = x * scaleX;
            var right = (x + 1) * scaleX;
            var top = y * scaleY;
            var bottom = (y + 1) * scaleY;
            double blue = 0, green = 0, red = 0, alpha = 0;
            for (var sy = (int)top; sy < Math.Min(source.Height, (int)Math.Ceiling(bottom)); sy++)
            for (var sx = (int)left; sx < Math.Min(source.Width, (int)Math.Ceiling(right)); sx++)
            {
                var weight = (Math.Min(right, sx + 1) - Math.Max(left, sx)) *
                    (Math.Min(bottom, sy + 1) - Math.Max(top, sy));
                var input = (sy * source.Width + sx) * 4;
                blue += source.Pixels[input] * weight;
                green += source.Pixels[input + 1] * weight;
                red += source.Pixels[input + 2] * weight;
                alpha += source.Pixels[input + 3] * weight;
            }
            var output = (y * width + x) * 4;
            // Filter color and coverage together in premultiplied space: no dark halos.
            pixels[output] = (byte)Math.Clamp(Math.Round(blue / area), 0, 255);
            pixels[output + 1] = (byte)Math.Clamp(Math.Round(green / area), 0, 255);
            pixels[output + 2] = (byte)Math.Clamp(Math.Round(red / area), 0, 255);
            pixels[output + 3] = (byte)Math.Clamp(Math.Round(alpha / area), 0, 255);
        }
        return new(width, height, pixels);
    }
}
