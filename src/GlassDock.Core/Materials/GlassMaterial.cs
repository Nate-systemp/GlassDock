namespace GlassDock.Core.Materials;

/// <summary>Platform-independent experiment values; dimensions are device-independent pixels.</summary>
public sealed record GlassMaterial
{
    public double BlurAmount { get; init; } = 28;
    public double Opacity { get; init; } = 0.88;
    public double Saturation { get; init; } = 1.15;
    public double Brightness { get; init; } = 1.08;
    public uint Tint { get; init; } = 0xDCEAFF;
    public double BorderOpacity { get; init; } = 0.48;
    public double BorderThickness { get; init; } = 1;
    public double CornerRadius { get; init; } = 32;
    public double ShadowOpacity { get; init; } = 0.32;
    public double ShadowBlur { get; init; } = 40;
    public double ShadowOffset { get; init; } = 16;
    public double RefractionAmount { get; init; }
    public double EdgeHighlight { get; init; } = 0.12;

    /// <summary>Clamp finite input and replace nonfinite values before passing to GPU APIs.</summary>
    public GlassMaterial Normalize() => this with
    {
        BlurAmount = Bound(BlurAmount, 0, 64, 28),
        Opacity = Bound(Opacity, 0, 1, 0.88),
        Saturation = Bound(Saturation, 0, 2, 1.15),
        Brightness = Bound(Brightness, 0.25, 2, 1.08),
        Tint = Tint & 0xFFFFFF,
        BorderOpacity = Bound(BorderOpacity, 0, 1, 0.48),
        BorderThickness = Bound(BorderThickness, 0, 6, 1),
        CornerRadius = Bound(CornerRadius, 0, 100, 32),
        ShadowOpacity = Bound(ShadowOpacity, 0, 1, 0.32),
        ShadowBlur = Bound(ShadowBlur, 0, 100, 40),
        ShadowOffset = Bound(ShadowOffset, -40, 60, 16),
        RefractionAmount = Bound(RefractionAmount, 0, 1, 0),
        EdgeHighlight = Bound(EdgeHighlight, 0, 1, 0.12)
    };

    private static double Bound(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
