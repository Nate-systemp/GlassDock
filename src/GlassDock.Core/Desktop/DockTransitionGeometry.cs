namespace GlassDock.Core.Desktop;

/// <summary>One reversible silhouette shared by the pill, material and icon row.</summary>
public readonly record struct DockTransitionGeometry(double Width, double Height, double CornerRadius, double MaterialBlend)
{
    public static DockTransitionGeometry At(double progress, double expandedWidth, double expandedHeight)
    {
        var p = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        var width = 120 + (expandedWidth - 120) * p;
        var height = 5 + (expandedHeight - 5) * p;
        var blend = Math.Clamp(p / .15, 0, 1);
        return new(width, height, Math.Min(34, Math.Min(width, height) / 2), blend * blend * (3 - 2 * blend));
    }
}
