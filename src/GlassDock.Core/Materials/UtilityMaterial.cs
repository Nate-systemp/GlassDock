using GlassDock.Core.Settings;

namespace GlassDock.Core.Materials;

public static class UtilityMaterial
{
    // Clear popup readability pass; foreground controls/thumbnails are never inputs.
    public const float LiquidBackdropBlur = 8;
    public const float LiquidBackdropExposure = -1.25f;
    public static LiquidGlassMaterial PopupOptics(LiquidGlassMaterial dockOptics) =>
        (dockOptics with { SpecularIntensity = .26f }).Normalize();
    // A dashboard repeats small neighboring surfaces. Dock-sized shadows overlap
    // each other here, so only their elevation is reduced; optical settings stay shared.
    public static GlassMaterial ForDashboardCard(GlassMaterial material, DockAppearanceMode mode) =>
        mode.GlassStyle() is null ? material : material with
        {
            ShadowOpacity = material.ShadowOpacity * .15,
            ShadowBlur = material.ShadowBlur * .40,
            ShadowOffset = material.ShadowOffset * .25
        };

    public static double DashboardContrast(DockAppearanceMode mode, double opacity) =>
        (mode switch
        {
            DockAppearanceMode.Frosted => .16,
            DockAppearanceMode.Acrylic => .12,
            DockAppearanceMode.Clear => .08,
            _ => 1
        }) * (mode.GlassStyle() is null ? 1 : Math.Clamp(opacity, 0, 1));

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
