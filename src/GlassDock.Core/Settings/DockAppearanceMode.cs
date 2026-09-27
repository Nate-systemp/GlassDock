namespace GlassDock.Core.Settings;

/// <summary>Persistent main-dock surface selection.</summary>
public enum DockAppearanceMode
{
    Dark = 0,
    Light = 1,
    Frosted = 2,
    Acrylic = 3,
    Clear = 4
}

public static class DockAppearanceModes
{
    public static GlassMaterialMode? GlassStyle(this DockAppearanceMode mode) => mode switch
    {
        DockAppearanceMode.Frosted => GlassMaterialMode.Frosted,
        DockAppearanceMode.Acrylic => GlassMaterialMode.Acrylic,
        DockAppearanceMode.Clear => GlassMaterialMode.Clear,
        _ => null
    };
}
