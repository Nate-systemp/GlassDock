using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;

namespace GlassDock.App.Desktop;

internal static class UtilityPopupStyle
{
    public const double Gutter = 16;
    public const double Padding = 22;
    public static void Apply(GlassSurface glass, DesktopGlassBackdrop backdrop,
        GlassDock.Core.Settings.DockAppearanceSettings appearance)
    {
        var material = GlassDock.Core.Materials.UtilityMaterial.Create(appearance);
        glass.Apply(material with { BorderOpacity = 0, EdgeHighlight = 0 });
        backdrop.UseInnerEdge = true;
        backdrop.Apply(material);
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
