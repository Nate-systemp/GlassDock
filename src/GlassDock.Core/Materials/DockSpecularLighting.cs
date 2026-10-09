using System.Numerics;

namespace GlassDock.Core.Materials;

public static class DockSpecularLighting
{
    /// <summary>Rotate opposing catches in normalized dock space, independent of aspect ratio and DPI.</summary>
    public static (Vector2 Start, Vector2 End) Gradient(double width, double height, double degrees)
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        var angle = (double.IsFinite(degrees) ? degrees : 45) * Math.PI / 180;
        var x = Math.Cos(angle); var y = Math.Sin(angle);
        var gx = x / width; var gy = y / height;
        var factor = (Math.Abs(x) + Math.Abs(y)) / (gx * gx + gy * gy);
        var delta = new Vector2((float)(factor * gx), (float)(factor * gy));
        var center = new Vector2((float)(width / 2), (float)(height / 2));
        return (center - delta / 2, center + delta / 2);
    }
}
