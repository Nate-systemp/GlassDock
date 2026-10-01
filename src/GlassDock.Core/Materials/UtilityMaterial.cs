using GlassDock.Core.Settings;

namespace GlassDock.Core.Materials;

public static class UtilityMaterial
{
    /// <summary>
    /// Uses the dock's selected preset and user values without a second popup
    /// blur/tint/opacity policy. Only the panel geometry differs from the pill.
    /// </summary>
    public static GlassMaterial CreateForPopup(
        DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        var style = mode.GlassStyle() ?? GlassMaterialMode.Frosted;
        return Create(appearance with { GlassMaterialMode = style });
    }

    public static GlassMaterial Create(DockAppearanceSettings appearance) =>
        appearance.ApplyTo(DockMaterialStylePresets.Create(appearance.GlassMaterialMode), true) with
        {
            CornerRadius = 28,
            BorderThickness = appearance.BorderThickness,
            BorderOpacity = appearance.BorderOpacity,
            EdgeHighlight = 0
        };
}
