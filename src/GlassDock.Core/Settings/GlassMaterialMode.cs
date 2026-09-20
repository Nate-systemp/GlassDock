using System.Diagnostics;
using GlassDock.Core.Materials;

namespace GlassDock.Core.Settings;

public enum GlassMaterialMode
{
    Frosted,
    Acrylic,
    Clear
}

/// <summary>Named main-dock material styles for the existing glass pipeline.</summary>
public static class DockMaterialStylePresets
{
    public static GlassMaterial Create(GlassMaterialMode mode) => Normalize(mode) switch
    {
        GlassMaterialMode.Frosted => new()
        {
            BlurAmount = GlassDockSettings.DefaultGlassBlurAmount,
            Opacity = GlassDockSettings.DefaultDockOpacity,
            Saturation = 1.15,
            Brightness = 1.08,
            Tint = 0xDCEAFF,
            BorderThickness = GlassDockSettings.DefaultBorderThickness,
            BorderOpacity = GlassDockSettings.DefaultBorderOpacity
        },
        GlassMaterialMode.Acrylic => new()
        {
            BlurAmount = 10,
            Opacity = 0.58,
            Saturation = 1.22,
            Brightness = 1.06,
            Tint = 0xD8E9FF,
            BorderThickness = 0.8,
            BorderOpacity = 0.48
        },
        GlassMaterialMode.Clear => new()
        {
            BlurAmount = 4,
            Opacity = 0.42,
            Saturation = 1.06,
            Brightness = 1.03,
            Tint = 0xE8F3FF,
            BorderThickness = 0.5,
            BorderOpacity = 0.25
        },
        _ => throw new UnreachableException()
    };

    public static GlassMaterialMode Normalize(GlassMaterialMode mode) =>
        Enum.IsDefined(mode) ? mode : GlassMaterialMode.Frosted;
}
