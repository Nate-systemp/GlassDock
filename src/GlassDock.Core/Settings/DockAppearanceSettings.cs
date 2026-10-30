using GlassDock.Core.Materials;

namespace GlassDock.Core.Settings;

public readonly record struct DockAppearanceSettings(
    GlassMaterialMode GlassMaterialMode,
    double IconSize,
    double MagnificationScale,
    double IconSpacing,
    double GlassBlurAmount,
    double DockOpacity,
    double BorderThickness,
    double BorderOpacity)
{
    public double ClearRefractionStrength { get; init; } = 12;
    public double SpecularHighlightAngle { get; init; } = 45;
    public LiquidGlassMaterial? VisualMaterial { get; init; }
    public LiquidGlassMaterial LiquidOptics => VisualMaterial ?? new LiquidGlassMaterial
    {
        RefractionStrength = (float)ClearRefractionStrength,
        SpecularAngleDegrees = (float)SpecularHighlightAngle
    }.Normalize();
    private const double HorizontalPadding = 36;
    private const double MagnificationSigma = 52;

    public double LayoutScale { get; init; } = 1;
    public bool FitHoverSlots { get; init; }
    public double ButtonWidth => FitHoverSlots ? Math.Max(IconSize + 12 * LayoutScale, IconSize * 1.375 + 2 * LayoutScale - IconSpacing) : IconSize + 12 * LayoutScale;
    public double ButtonHeight => IconSize + 16 * LayoutScale;

    public DockAppearanceSettings ForDock(double scale) => this with
    {
        LayoutScale = scale, FitHoverSlots = true, IconSize = IconSize * scale, IconSpacing = IconSpacing * scale,
        BorderThickness = BorderThickness * scale
    };

    public double TargetDockWidth(int iconCount)
    {
        if (iconCount <= 0)
            return 100;

        var target = iconCount * ButtonWidth +
            (iconCount - 1) * IconSpacing +
            HorizontalPadding;
        return Math.Clamp(target, 100, 560);
    }

    public double ScaleAtDistance(double distance)
    {
        if (!double.IsFinite(distance))
            return 1;

        var influence = Math.Exp(
            -(distance * distance) /
            (2 * MagnificationSigma * MagnificationSigma));
        var scale = 1 + (MagnificationScale - 1) * influence;
        return scale < 1.003 ? 1 : Math.Min(MagnificationScale, scale);
    }

    public GlassMaterial ApplyTo(GlassMaterial material, bool expanded) => material with
    {
        BlurAmount = VisualMaterial?.BlurAmount ?? GlassBlurAmount,
        Opacity = expanded ? VisualMaterial?.MaterialBlend ?? DockOpacity : material.Opacity,
        Saturation = VisualMaterial?.Saturation ?? material.Saturation,
        Brightness = VisualMaterial?.Brightness ?? material.Brightness,
        Contrast = VisualMaterial?.Contrast ?? material.Contrast,
        BlurQuality = (int)(VisualMaterial?.BlurQuality ?? material.BlurQuality)
    };
}
