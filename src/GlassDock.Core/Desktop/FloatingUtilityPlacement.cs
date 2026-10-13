namespace GlassDock.Core.Desktop;

public static class FloatingUtilityPlacement
{
    public static bool IsDrag(double deltaX, double deltaY, double scale) =>
        deltaX * deltaX + deltaY * deltaY >= 36 * scale * scale;

    public static (double X, double Y) Clamp(double x, double y, double left, double top, double width, double height, double size, bool snap)
        => Clamp(x, y, left, top, width, height, size, size, snap);

    public static (double X, double Y) Clamp(double x, double y, double left, double top, double width, double height, double itemWidth, double itemHeight, bool snap)
    {
        var right = left + Math.Max(0, width - itemWidth);
        var bottom = top + Math.Max(0, height - itemHeight);
        x = Math.Clamp(double.IsFinite(x) ? x : right, left, right);
        y = Math.Clamp(double.IsFinite(y) ? y : bottom, top, bottom);
        if (snap)
        {
            var distances = new[] { x - left, right - x, y - top, bottom - y };
            switch (Array.IndexOf(distances, distances.Min()))
            {
                case 0: x = left; break;
                case 1: x = right; break;
                case 2: y = top; break;
                case 3: y = bottom; break;
            }
        }
        return (x, y);
    }
}
