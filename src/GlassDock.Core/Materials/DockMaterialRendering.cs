using GlassDock.Core.Settings;

namespace GlassDock.Core.Materials;

public static class DockMaterialRendering
{
    public static double BaseBlur(GlassMaterialMode? style, double blur) => style switch
    {
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
