namespace GlassDock.Core.Materials;

/// <summary>Optics in DIPs; independent of capture, HWNDs and the UI theme.</summary>
public sealed record LiquidGlassMaterial
{
    public float RefractionStrength { get; init; } = 8;
    public float RefractionFalloff { get; init; } = 2;
    public float EdgeThickness { get; init; } = 13;
    public float DiffusionAmount { get; init; } = .65f;
    // Maximum per-channel rim offset in DIPs; zero preserves neutral refraction.
    public float ChromaticDispersion { get; init; } = .65f;
    public float TintOpacity { get; init; } = .045f;
    public float SpecularIntensity { get; init; } = .16f;
    public float SpecularFalloff { get; init; } = 2.5f;
    public float SpecularAngleDegrees { get; init; } = 45;
    public float Splay { get; init; } = 1f;
    public float Magnification { get; init; } = 0f;
    public float EdgeFocus { get; init; } = 0.975f;
    public float RefractionSoftness { get; init; } = 0f;
    public float HighlightWidth { get; init; } = 1.4f;
    public float HighlightSoftness { get; init; } = 0.13f;
    public float HighlightSpread { get; init; } = 0.8f;
    public float HighlightPosition { get; init; } = 0.1f;
    public float InnerGlint { get; init; } = 0f;
    public float OuterGlint { get; init; } = 1f;
    public float TopLight { get; init; } = 1f;
    public float BottomLight { get; init; } = 1f;
    public float ReflectionIntensity { get; init; } = 0f;
    public float ReflectionSoftness { get; init; } = 0.25f;
    public float InnerShine { get; init; } = 1f;
    public float EdgeOpacity { get; init; } = 0f;
    public float EdgeWidth { get; init; } = 1f;
    public float Bevel { get; init; } = 0f;
    public float SurfaceOpacity { get; init; } = 1f;
    public float MaterialBlend { get; init; } = 1f;
    public float BlurAmount { get; init; } = 0f;
    public float BlurQuality { get; init; } = 1f;
    public float Saturation { get; init; } = 1f;
    public float Brightness { get; init; } = 1f;
    public float Contrast { get; init; } = 1f;
    public float CornerRadius { get; init; } = 34f;

    public LiquidGlassMaterial Normalize()
    {
        static float Bound(float n, float fallback, float min, float max) => float.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
        return this with
        {
            Splay = Bound(Splay, 1f, 0.25f, 3f),
            Magnification = Bound(Magnification, 0f, 0f, 0.25f),
            EdgeFocus = Bound(EdgeFocus, 0.975f, 0f, 1f),
            RefractionSoftness = Bound(RefractionSoftness, 0f, 0f, 1f),
            HighlightWidth = Bound(HighlightWidth, 1.4f, 0f, 6f),
            HighlightSoftness = Bound(HighlightSoftness, 0.13f, 0.02f, 0.5f),
            HighlightSpread = Bound(HighlightSpread, 0.8f, 0.1f, 1f),
            HighlightPosition = Bound(HighlightPosition, 0.1f, 0f, 1f),
            InnerGlint = Bound(InnerGlint, 0f, 0f, 2f),
            OuterGlint = Bound(OuterGlint, 1f, 0f, 2f),
            TopLight = Bound(TopLight, 1f, 0f, 2f),
            BottomLight = Bound(BottomLight, 1f, 0f, 2f),
            ReflectionIntensity = Bound(ReflectionIntensity, 0f, 0f, 0.4f),
            ReflectionSoftness = Bound(ReflectionSoftness, 0.25f, 0.03f, 1f),
            InnerShine = Bound(InnerShine, 1f, 0f, 2f),
            EdgeOpacity = Bound(EdgeOpacity, 0f, 0f, 1f),
            EdgeWidth = Bound(EdgeWidth, 1f, 0f, 6f),
            Bevel = Bound(Bevel, 0f, 0f, 1f),
            SurfaceOpacity = Bound(SurfaceOpacity, 1f, 0f, 1f),
            MaterialBlend = Bound(MaterialBlend, 1f, 0f, 1f),
            BlurAmount = Bound(BlurAmount, 0f, 0f, 60f),
            BlurQuality = MathF.Round(Bound(BlurQuality, 1f, 0f, 2f)),
            Saturation = Bound(Saturation, 1f, 0f, 2f),
            Brightness = Bound(Brightness, 1f, 0.25f, 2f),
            Contrast = Bound(Contrast, 1f, 0.25f, 2f),
            CornerRadius = Bound(CornerRadius, 34f, 0f, 34f),
            RefractionStrength = Bound(RefractionStrength, 8, 0, 20),
            RefractionFalloff = Bound(RefractionFalloff, 2, 1, 5),
            EdgeThickness = Bound(EdgeThickness, 13, 2, 32),
            DiffusionAmount = Bound(DiffusionAmount, .65f, 0, 3),
            ChromaticDispersion = Bound(ChromaticDispersion, .65f, 0, 1),
            TintOpacity = Bound(TintOpacity, .045f, 0, .2f),
            SpecularIntensity = Bound(SpecularIntensity, .16f, 0, .4f),
            SpecularAngleDegrees = Bound(SpecularAngleDegrees, 45, 0, 360),
            SpecularFalloff = Bound(SpecularFalloff, 2.5f, .5f, 8)
        };
    }
}
