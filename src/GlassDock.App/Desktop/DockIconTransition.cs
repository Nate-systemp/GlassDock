using GlassDock.Core.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

/// <summary>Retained icon presentation derived from the dock's animated geometry, with no clock of its own.</summary>
internal sealed class DockIconTransition : IDisposable
{
    private readonly Panel icons;
    private readonly FrameworkElement surface;
    private readonly CompositeTransform transform = new();
    private double expandedWidth = 560, expandedHeight = 68;
    private readonly long widthToken, heightToken, iconHeightToken;
    private bool disposed;

    public void SetExpandedSize(double width, double height)
    {
        expandedWidth = width;
        expandedHeight = height;
        Refresh();
    }

    public DockIconTransition(Panel icons, FrameworkElement surface)
    {
        this.icons = icons;
        this.surface = surface;
        widthToken = surface.RegisterPropertyChangedCallback(FrameworkElement.WidthProperty, (_, _) => Refresh());
        heightToken = surface.RegisterPropertyChangedCallback(FrameworkElement.HeightProperty, (_, _) => Refresh());
        iconHeightToken = icons.RegisterPropertyChangedCallback(FrameworkElement.HeightProperty, (_, _) => Refresh());
        icons.RenderTransformOrigin = new(.5, 1);
        icons.RenderTransform = transform;
        Refresh();
    }

    private void Refresh()
    {
        if (disposed) return;
        var height = double.IsFinite(icons.Height) && icons.Height > 0 ? icons.Height : 68;
        var width = double.IsFinite(surface.Width) ? surface.Width : surface.ActualWidth;
        var dockHeight = double.IsFinite(surface.Height) ? surface.Height : surface.ActualHeight;
        var motion = DockIconMotion.FromGeometry(width, dockHeight, expandedWidth, expandedHeight, height);
        transform.ScaleX = transform.ScaleY = motion.Scale;
        transform.TranslateY = motion.OffsetY;
        icons.Opacity = motion.Opacity;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        surface.UnregisterPropertyChangedCallback(FrameworkElement.WidthProperty, widthToken);
        surface.UnregisterPropertyChangedCallback(FrameworkElement.HeightProperty, heightToken);
        icons.UnregisterPropertyChangedCallback(FrameworkElement.HeightProperty, iconHeightToken);
    }
}
