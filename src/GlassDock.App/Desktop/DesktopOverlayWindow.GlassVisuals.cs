using GlassDock.App.Controls;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

public sealed partial class DesktopOverlayWindow
{
    // Visual rounding never changes the native hit region, reserved taskbar space, or animation endpoints.
    private double VisualDockCornerRadius => (settingsSession.Current.GlassVisuals?.DockMaterial.CornerRadius * DockScale) ?? ExpandedDockCornerRadius;

    private void ApplyButtonVisual(string id, Button button)
    {
        if (button.Content is not Grid content) return;
        var value = settingsSession.Current.GlassVisuals?.Component(id);
        var layer = content.Children.OfType<GlassSurface>().FirstOrDefault();
        if (value is null)
        {
            if (layer is not null) content.Children.Remove(layer);
            return; // The unduplicated dock material remains visible through the button.
        }
        if (layer is null)
        {
            layer = new GlassSurface { IsHitTestVisible = false };
            Canvas.SetZIndex(layer, -1);
            content.Children.Add(layer); // Keep the icon at index zero for existing interaction code.
        }
        layer.Opacity = value.SurfaceOpacity;
        layer.Apply(ComponentMaterial(value, settingsSession.Current.DockAppearanceMode, DockScale));
    }

    internal static GlassMaterial ComponentMaterial(LiquidGlassMaterial value, DockAppearanceMode mode, double scale = 1) =>
        DockMaterialStylePresets.Create(mode.GlassStyle() ?? GlassMaterialMode.Frosted) with
        {
            BlurAmount = value.BlurAmount, BlurQuality = (int)value.BlurQuality,
            Opacity = value.MaterialBlend, Saturation = value.Saturation,
            Brightness = value.Brightness, Contrast = value.Contrast,
            BorderOpacity = value.EdgeOpacity, BorderThickness = value.EdgeWidth * scale,
            CornerRadius = value.CornerRadius * scale, ShadowOpacity = 0, EdgeHighlight = 0
        };

    private void ApplyRunningVisual(Border running)
    {
        running.BorderThickness = new(0);
        running.BorderBrush = null;
        if (settingsSession.Current.GlassVisuals is not { } profile) return;
        var value = profile.Component("indicators");
        if (value is null) return;
        running.Opacity *= value.SurfaceOpacity;
        running.CornerRadius = new(Math.Min(value.CornerRadius * DockScale, running.Height / 2));
        running.BorderThickness = new(value.EdgeWidth * DockScale);
        running.BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb((byte)(255 * value.EdgeOpacity), 255, 255, 255));
    }
}
