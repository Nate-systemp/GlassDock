using GlassDock.Core.Settings;
using Windows.UI;

namespace GlassDock.App.Desktop;

// The dock and its utility windows share these interaction colors. Material
// tint/blur/borders remain owned by DockAppearanceSettings and the glass graph.
internal static class DockControlPalette
{
    public const double ButtonRadius = 9;
    public static Color SolidSurface(DockAppearanceMode mode, byte alpha = 255) => mode == DockAppearanceMode.Light
        ? Color.FromArgb(alpha, 216, 204, 184) : Color.FromArgb(alpha, 36, 36, 36);
    public static Color Foreground(DockAppearanceMode mode) => mode == DockAppearanceMode.Light
        ? Color.FromArgb(255, 40, 40, 40) : Color.FromArgb(255, 240, 240, 240);
    public static Color Surface(DockAppearanceMode mode, byte alpha)
    {
        var shade = (byte)(mode == DockAppearanceMode.Light ? 0 : 255);
        return Color.FromArgb(alpha, shade, shade, shade);
    }
    public static Color Normal(DockAppearanceMode mode, bool selected) => Surface(mode, (byte)(selected ? 24 : 0));
    public static Color Hover(DockAppearanceMode mode, bool selected) => Surface(mode, (byte)(selected ? 34 : 20));
    public static Color Pressed(DockAppearanceMode mode, bool selected) => Surface(mode, (byte)(selected ? 44 : 30));
}
