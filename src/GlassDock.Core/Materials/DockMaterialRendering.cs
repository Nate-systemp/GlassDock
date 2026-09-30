using GlassDock.Core.Settings;

namespace GlassDock.Core.Materials;

/// <summary>Main-dock optical branches; null preserves the laboratory/popup graph.</summary>
public static class DockMaterialRendering
{
    public static double BaseBlur(GlassMaterialMode? style, double blur) => style switch
    {
        // Frosted diffuses both branches. Acrylic keeps a lightly diffused base
        // under its saturated/tinted layer. Clear admits the sharp backdrop.
        GlassMaterialMode.Acrylic => Math.Min(blur, DockMaterialStylePresets.Create(GlassMaterialMode.Clear).BlurAmount),
        GlassMaterialMode.Clear => 0,
        _ => blur
    };

    public static double SpecularAlpha(double position, double opacity)
    {
        var t = Math.Clamp(position / .78, 0, 1);
        var fade = 1 - t * t * (3 - 2 * t);
        return .9 * Math.Sqrt(Math.Clamp(opacity, 0, 1)) * fade;
    }
}
