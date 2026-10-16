namespace GlassDock.Core.Desktop;

/// <summary>Distance-based retarget timing; normal endpoint choreography is unchanged.</summary>
public static class DockTransitionTiming
{
    public static double Progress(double width, double height, double expandedWidth, double expandedHeight, double scale = 1) =>
        Math.Clamp((width - 120 * scale) / Math.Max(1, expandedWidth - 120 * scale), 0, 1);

    public static double Duration(bool expanded, double progress) =>
        Math.Clamp((expanded ? 380 : 300) * Math.Abs((expanded ? 1 : 0) - progress),
            45, expanded ? 380 : 300);
}
