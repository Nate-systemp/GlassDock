using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;

namespace GlassDock.App.Desktop;

internal static class PopupStyle
{
    public const double Gutter = 16;
    public const double CompactMenuWidth = DockAppContextMenuWindow.MenuWidth;
    public const double CompactFontSize = 13 * DockAppContextMenuWindow.MenuSizeScale;

    public static void Apply(GlassSurface glass, DesktopGlassBackdrop backdrop,
        DockAppearanceSettings appearance, DockAppearanceMode mode = DockAppearanceMode.Dark,
        double cornerRadius = 28)
    {
        var material = PopupMaterial.CreateForPopup(appearance, mode) with { CornerRadius = cornerRadius };
        glass.Apply(material with { BorderOpacity = 0, EdgeHighlight = 0 });
        backdrop.UseInnerEdge = true;
        backdrop.UseDockSpecular = mode == DockAppearanceMode.Clear;
        backdrop.SpecularHighlightAngle = appearance.SpecularHighlightAngle;
        if (mode.GlassStyle() is { } style)
        {
            backdrop.ApplyMainDock(style, material with { EdgeHighlight = 0 });
        }
        else
        {
            backdrop.SetSolidAppearance(mode, 1, cornerRadius);
            backdrop.Apply(material with { BorderOpacity = 0, EdgeHighlight = 0 });
        }
        backdrop.SetPopupMode(mode, appearance.LiquidOptics);
    }

}
