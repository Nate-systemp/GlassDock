namespace GlassDock.Core.Desktop;

public readonly record struct DockIconMotion(double Scale, double OffsetY, double Opacity)
{
    public static DockIconMotion FromGeometry(double width, double height,
        double expandedWidth, double expandedHeight, double contentHeight) =>
        At(DockTransitionTiming.Progress(width, height, expandedWidth, expandedHeight), contentHeight) with
        {
            Scale = Math.Clamp(Math.Min(width / Math.Max(1, expandedWidth), height / Math.Max(1, expandedHeight)), 0, 1)
        };

    /// <summary>Fade immediately with geometry; bottom-anchored scaling moves icons down into the pill.</summary>
    public static DockIconMotion At(double progress, double contentHeight)
    {
        var p = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        return new(p, 0, p * p * (3 - 2 * p));
    }
}
