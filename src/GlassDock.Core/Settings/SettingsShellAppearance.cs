namespace GlassDock.Core.Settings;

public static class SettingsShellAppearance
{
    public static (DockAppearanceMode Mode, DockAppearanceSettings Appearance) Resolve(
        DockAppearanceMode selected, DockAppearanceSettings appearance)
    {
        if (selected != DockAppearanceMode.Clear) return (selected, appearance);
        var frosted = DockMaterialStylePresets.Create(GlassMaterialMode.Frosted);
        return (DockAppearanceMode.Frosted, appearance with
        {
            GlassMaterialMode = GlassMaterialMode.Frosted,
            GlassBlurAmount = frosted.BlurAmount,
            DockOpacity = frosted.Opacity
        });
    }
}
