namespace GlassDock.Core.Desktop;

public static class FullscreenPolicy
{
    public static bool CoversMonitor(bool eligible, bool sameMonitor, bool minimized,
        double left, double top, double right, double bottom,
        double monitorLeft, double monitorTop, double monitorRight, double monitorBottom) =>
        eligible && sameMonitor && !minimized &&
        left <= monitorLeft + 1 && top <= monitorTop + 1 &&
        right >= monitorRight - 1 && bottom >= monitorBottom - 1;
}
