namespace GlassDock.Core.Desktop;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public static class DesktopPlacement
{
    public static PixelRect BottomCenter(PixelRect monitor, double width, double height, double margin, double scale)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0 || !double.IsFinite(scale) || scale <= 0 ||
            !double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0 ||
            !double.IsFinite(margin))
            throw new ArgumentOutOfRangeException(nameof(monitor));
        var w = Math.Min(monitor.Width, (int)Math.Round(width * scale));
        var h = Math.Min(monitor.Height, (int)Math.Round(height * scale));
        var gap = (int)Math.Round(Math.Clamp(margin, 0, 100) * scale);
        return new(monitor.X + (monitor.Width - w) / 2,
            Math.Max(monitor.Y, monitor.Y + monitor.Height - h - gap), w, h);
    }
}
