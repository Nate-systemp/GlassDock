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
        // Material/visual mode is derived from the authoritative Appearance choice,
        // never from a second user-facing Material selector. Solid modes are
        // overlaid by the XAML chrome; glass modes get their own compositor preset.
        var material = PopupMaterial.CreateForPopup(appearance, mode) with { CornerRadius = cornerRadius };
        glass.Apply(material with { BorderOpacity = 0, EdgeHighlight = 0 });
        // Keep the edge pipeline available for live appearance switching. All
        // three glass modes use the SAME optical branch as the main dock,
        // including Clear's geometry-aware edge treatment.
        backdrop.UseInnerEdge = true;
        // Clear fallback uses the same thin directional catches as its GPU rim.
        backdrop.UseDockSpecular = mode == DockAppearanceMode.Clear;
        backdrop.SpecularHighlightAngle = appearance.SpecularHighlightAngle;
        if (mode.GlassStyle() is { } style)
        {
            backdrop.ApplyMainDock(style, material with { EdgeHighlight = 0 });
        }
        else
        {
            // The retained overlay palette is fully opaque for the two solid
            // dock finishes. Keep the shared compositor alive behind it so
            // switching Dark/Light -> glass doesn't require window recreation.
            backdrop.SetSolidAppearance(mode, 1, cornerRadius);
            backdrop.Apply(material with { BorderOpacity = 0, EdgeHighlight = 0 });
        }
        backdrop.SetPopupMode(mode, appearance.LiquidOptics);
    }

}
