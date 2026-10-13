namespace GlassDock.Core.Desktop;

public static class FloatingUtilityPlacement
{
    /// <summary>
    /// Keep the floating Utilities pill entirely within the reserved top strip.
    /// Vertical dragging cannot escape into applications below the work area;
    /// horizontal dragging and left/right edge snapping remain available.
    /// All coordinates and dimensions are physical screen pixels.
    /// </summary>
    public static (double X, double Y) ClampToTopStrip(
        double x, double left, double top, double right, double bottom,
        double itemWidth, double itemHeight, double inset, bool snap)
    {
        inset = Math.Max(0, inset);
        var minX = left + inset;
        var maxX = Math.Max(minX, right - itemWidth - inset);
        x = Math.Clamp(double.IsFinite(x) ? x : maxX, minX, maxX);
        if (snap)
            x = x - minX <= maxX - x ? minX : maxX;
        // Center vertically in the reserved strip, never leave the actual
        // display or overlap the maximized application's workspace.
        var y = top + Math.Max(0, (bottom - top - itemHeight) / 2);
        return (x, y);
    }

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
