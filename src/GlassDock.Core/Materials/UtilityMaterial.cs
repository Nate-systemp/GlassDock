using GlassDock.Core.Settings;

namespace GlassDock.Core.Materials;

public static class UtilityMaterial
{
    public static GlassMaterial Create(DockAppearanceSettings appearance) =>
        appearance.ApplyTo(DockMaterialStylePresets.Create(appearance.GlassMaterialMode), true) with
        {
            CornerRadius = 24,
            BorderThickness = appearance.BorderThickness,
            BorderOpacity = appearance.BorderOpacity,
            EdgeHighlight = 0,
            ShadowOpacity = .24,
            ShadowBlur = 20,
            ShadowOffset = 4
        };
}
