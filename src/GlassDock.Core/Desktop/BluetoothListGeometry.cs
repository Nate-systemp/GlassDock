namespace GlassDock.Core.Desktop;

public static class BluetoothListGeometry
{
    public const double RowHeight = 48;
    public const int VisibleRows = 5;

    public static double ViewportHeight(int count, double availableHeight) =>
        Math.Min(Math.Min(Math.Max(0, count), VisibleRows) * RowHeight, Math.Max(0, availableHeight));

    public static double EdgeProgress(double top, double height, double offset, double viewport)
    {
        if (height <= 0 || viewport <= 0) return 0;
        var visible = Math.Max(0, Math.Min(top + height, offset + viewport) - Math.Max(top, offset));
        var t = Math.Clamp(visible / Math.Min(RowHeight, height), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
