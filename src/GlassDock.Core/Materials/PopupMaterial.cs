using GlassDock.Core.Settings;

namespace GlassDock.Core.Materials;

public static class PopupMaterial
{
    public static LiquidGlassMaterial FrostedUtilityOptics(LiquidGlassMaterial dockOptics)
    {
        var frosted = GlassVisualProfile.BuiltIn(DockAppearanceMode.Frosted).DockMaterial;
        return (dockOptics with
        {
            BlurAmount = Math.Max(dockOptics.BlurAmount, frosted.BlurAmount),
            MaterialBlend = frosted.MaterialBlend, SurfaceOpacity = frosted.SurfaceOpacity,
            Saturation = frosted.Saturation, Brightness = frosted.Brightness,
            SpecularIntensity = Math.Min(dockOptics.SpecularIntensity, frosted.SpecularIntensity)
        }).Normalize();
    }

    public const float LiquidBackdropBlur = 2.5f;
    public const float LiquidBackdropExposure = 0f;
    public static LiquidGlassMaterial PopupOptics(LiquidGlassMaterial dockOptics) =>
        (dockOptics with { SpecularIntensity = .26f }).Normalize();
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
