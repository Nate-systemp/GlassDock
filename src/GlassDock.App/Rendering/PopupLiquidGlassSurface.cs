using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;

namespace GlassDock.App.Rendering;

internal sealed class PopupLiquidGlassSurface : IDisposable
{
    private readonly Window window;
    private readonly Panel root;
    private readonly DesktopGlassBackdrop backdrop;
    private readonly DokyLiquidGlassSurface liquid;
    private bool disposed;
    private bool wasActive;
    private GlassDock.Core.Materials.LiquidGlassMaterial? appliedOptics;
    private bool useDockOptics;
    public bool UseDockOptics
    {
        get => useDockOptics;
        set { useDockOptics = value; liquid.PreserveDockBackdrop = value; appliedOptics = null; Update(); }
    }
    public FrameworkElement Element => liquid.Output;
    public float OuterRimStrokeScale { get => liquid.PopupRimStrokeScale; set => liquid.PopupRimStrokeScale = value; }
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
            liquid.Material = useDockOptics ? appliedOptics : GlassDock.Core.Materials.PopupMaterial.PopupOptics(appliedOptics);
        }
        var active = root.IsLoaded && window.AppWindow.IsVisible &&
            backdrop.PopupMode == DockAppearanceMode.Clear && bounds.Z > 0 && bounds.W > 0;
        if (!active && !wasActive) return;
        wasActive = active;
        liquid.Update(active, 1, scale > 0 ? scale : 1, bounds,
            new(bounds.X + bounds.Z / 2, Math.Max(1, bounds.Z / 2), 0, radius));
    }
    public bool BeginScreenshotMode() => !disposed && liquid.BeginScreenshotMode();
    public void EndScreenshotMode()
    {
        if (!disposed) liquid.EndScreenshotMode();
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
