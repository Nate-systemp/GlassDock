namespace GlassDock.Core.Materials;

/// <summary>Restrained optics for the Clear Control Center, independent of dock optics.</summary>
public static class ControlCenterGlassMaterial
{
    // Independent GPU blur radii in DIPs. Keep the panel clearer than opaque acrylic,
    // but diffuse recognizable wallpaper/window details under its contents.
    public const float PanelBlur = 12f;
    public const float ControlBlur = 6.5f;

    public static LiquidGlassMaterial Panel(LiquidGlassMaterial source) => Create(source, false);
    public static LiquidGlassMaterial Control(LiquidGlassMaterial source) => Create(source, true);

    private static LiquidGlassMaterial Create(LiquidGlassMaterial source, bool control)
    {
        var valid = source.Normalize();
        return valid with
        {
            RefractionStrength = Math.Min(valid.RefractionStrength * (control ? .2f : .35f), control ? 3 : 5),
            EdgeThickness = control ? 4 : 6,
            DiffusionAmount = .35f,
            ChromaticDispersion = .05f,
            TintOpacity = 0,
            SpecularIntensity = control ? .11f : .18f,
            SpecularFalloff = .8f
        };
    }
}
