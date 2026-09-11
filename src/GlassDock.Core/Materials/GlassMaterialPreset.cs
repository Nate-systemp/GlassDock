namespace GlassDock.Core.Materials;

public enum GlassMaterialPreset { Frosted, Clear, Refractive }

public static class GlassMaterialPresets
{
    public static GlassMaterial Create(GlassMaterialPreset preset) => preset switch
    {
        GlassMaterialPreset.Frosted => new(),
        GlassMaterialPreset.Clear => new()
        {
            BlurAmount = 5, Opacity = 0.65, Saturation = 1.05, Brightness = 1.02,
            BorderOpacity = 0.32, ShadowOpacity = 0.18, ShadowBlur = 26,
            ShadowOffset = 10, EdgeHighlight = 0.07
        },
        // Lighting experiment only. Refraction stays zero because this backend cannot bend rays.
        GlassMaterialPreset.Refractive => new()
        {
            BlurAmount = 10, Opacity = 0.74, Saturation = 1.25, Brightness = 1.12,
            Tint = 0xD9FFF6, BorderOpacity = 0.7, BorderThickness = 1.5,
            ShadowOpacity = 0.38, ShadowBlur = 48, ShadowOffset = 20,
            CornerRadius = 40, EdgeHighlight = 0.3
        },
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };
}
