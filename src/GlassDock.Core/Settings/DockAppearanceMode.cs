namespace GlassDock.Core.Settings;

public enum DockAppearanceMode
{
    Dark = 0,
    Light = 1,
    Frosted = 2,
    Clear = 4
}

public static class DockAppearanceModes
{
    // Value 3 was Acrylic. Keep persisted mode numbers stable.
    public static DockAppearanceMode Normalize(this DockAppearanceMode mode, DockAppearanceMode fallback) => mode switch
    {
        (DockAppearanceMode)3 => DockAppearanceMode.Frosted,
        _ when Enum.IsDefined(mode) => mode,
        _ => fallback
    };

    public static GlassMaterialMode? GlassStyle(this DockAppearanceMode mode) => mode switch
    {
        DockAppearanceMode.Frosted => GlassMaterialMode.Frosted,
        DockAppearanceMode.Clear => GlassMaterialMode.Clear,
        _ => null
    };
}
