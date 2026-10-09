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

    public LiquidGlassMaterial Normalize()
    {
        static float Bound(float n, float fallback, float min, float max) => float.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
        return this with
        {
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
