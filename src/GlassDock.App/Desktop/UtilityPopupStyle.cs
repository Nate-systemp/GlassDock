using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace GlassDock.App.Desktop;

internal static class UtilityPopupStyle
{
    public const double Gutter = 16;
    public const double Padding = 20;

    public static void Apply(GlassSurface glass, DesktopGlassBackdrop backdrop,
        DockAppearanceSettings appearance, DockAppearanceMode mode = DockAppearanceMode.Dark)
    {
        // Material/visual mode is derived from the authoritative Appearance choice,
        // never from a second user-facing Material selector. Solid modes are
        // overlaid by the XAML chrome; glass modes get their own compositor preset.
        var material = UtilityMaterial.CreateForPopup(appearance, mode);
        glass.Apply(material with { BorderOpacity = 0, EdgeHighlight = 0 });
        // Keep the edge pipeline available for live appearance switching. All
        // three glass modes use the SAME optical branch as the main dock,
        // including Clear's geometry-aware edge treatment.
        backdrop.UseInnerEdge = true;
        // Match the dock's diffusion exactly, including Acrylic's base layer.
        backdrop.UsePopupBlur = false;
        // The utility HWND is configured with the dock's layered DWM client.
        backdrop.UseHostBackdropForPopup = false;
        if (mode.GlassStyle() is { } style)
        {
            backdrop.ApplyMainDock(style, material with { EdgeHighlight = 0 });
        }
        else
        {
            // The retained overlay palette is fully opaque for the two solid
            // dock finishes. Keep the shared compositor alive behind it so
            // switching Dark/Light -> glass doesn't require window recreation.
            backdrop.SetSolidAppearance(mode, 1, 28);
            backdrop.Apply(material with { BorderOpacity = 0, EdgeHighlight = 0 });
        }
        // Rebind only when the optional diagnostic source changes. Never
        // detach the live Window.SystemBackdrop (previous theme-switch crash).
        backdrop.RefreshPopupBackdropSource();
    }

    public static void Position(AppWindow popup, AppWindow owner, double scale,
        double anchorX, double dockTop, double panelWidth, double panelHeight)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        var area = DisplayArea.GetFromWindowId(owner.Id, DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min(area.Width, (int)Math.Round((panelWidth + Gutter * 2) * scale));
        var height = Math.Min(area.Height, (int)Math.Round((panelHeight + Gutter * 2) * scale));
        var x = owner.Position.X + (int)Math.Round(anchorX * scale) - width / 2;
        var y = owner.Position.Y + (int)Math.Round(dockTop * scale) - height + (int)Math.Round(6 * scale);
        var bounds = new RectInt32(Math.Clamp(x, area.X, area.X + area.Width - width),
            Math.Clamp(y, area.Y, area.Y + area.Height - height), width, height);
        if (popup.Position.X != bounds.X || popup.Position.Y != bounds.Y ||
            popup.Size.Width != bounds.Width || popup.Size.Height != bounds.Height)
            popup.MoveAndResize(bounds);
    }
}
