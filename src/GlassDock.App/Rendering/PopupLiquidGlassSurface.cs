using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;

namespace GlassDock.App.Rendering;

/// <summary>Opt-in lifecycle adapter. Content and native thumbnails stay above the glass.</summary>
internal sealed class PopupLiquidGlassSurface : IDisposable
{
    private readonly Window window;
    private readonly Panel root;
    private readonly DesktopGlassBackdrop backdrop;
    private readonly DokyLiquidGlassSurface liquid;
    private bool disposed;
    private bool wasActive;
    private GlassDock.Core.Materials.LiquidGlassMaterial? appliedOptics;
    public FrameworkElement Element => liquid.Output;

    public PopupLiquidGlassSurface(Window window, Panel root, DesktopGlassBackdrop backdrop)
    {
        this.window = window; this.root = root; this.backdrop = backdrop;
        liquid = new(WinRT.Interop.WindowNative.GetWindowHandle(window), window.DispatcherQueue,
            () => backdrop.DockGeometry, backdrop.SetLiquidActive, popup: true);
        liquid.StatusChanged += (_, _) => StartupDiagnostics.Write($"Popup Liquid [{window.Title}]: {liquid.Status}");
        Attach();
        backdrop.PopupSurfaceChanged += Changed;
        backdrop.RenderingModeChanged += Changed;
        root.Loaded += Loaded;
        window.AppWindow.Changed += WindowChanged;
        window.Closed += Closed;
    }

    // Preview paging replaces its card children; preserve this single retained layer.
    public void Attach()
    {
        if (!root.Children.Contains(Element)) root.Children.Insert(0, Element);
    }
    private void Loaded(object sender, RoutedEventArgs args) => Update();
    private void Changed(object? sender, EventArgs args) => Update();
    private void WindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidVisibilityChange || args.DidPositionChange || args.DidSizeChange) Update();
    }
    private void Closed(object sender, WindowEventArgs args) => Dispose();
    private void Update()
    {
        if (disposed) return;
        var (bounds, radius, scale) = backdrop.PopupGeometry;
        if (!ReferenceEquals(appliedOptics, backdrop.PopupOptics))
        {
            appliedOptics = backdrop.PopupOptics;
            liquid.Material = GlassDock.Core.Materials.UtilityMaterial.PopupOptics(appliedOptics);
        }
        var active = root.IsLoaded && window.AppWindow.IsVisible &&
            backdrop.PopupMode == DockAppearanceMode.Clear && bounds.Z > 0 && bounds.W > 0;
        if (!active && !wasActive) return;
        wasActive = active;
        // Root already carries the popup's fade/transform: do not apply opacity twice.
        liquid.Update(active, 1, scale > 0 ? scale : 1, bounds,
            new(bounds.X + bounds.Z / 2, Math.Max(1, bounds.Z / 2), 0, radius));
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        backdrop.PopupSurfaceChanged -= Changed;
        backdrop.RenderingModeChanged -= Changed;
        root.Loaded -= Loaded;
        window.AppWindow.Changed -= WindowChanged;
        window.Closed -= Closed;
        liquid.Dispose();
    }
}
