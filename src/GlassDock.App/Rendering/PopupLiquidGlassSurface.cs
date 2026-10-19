using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using System.Numerics;

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
    private bool useDockOptics;
    public bool UseDockOptics
    {
        get => useDockOptics;
        set { useDockOptics = value; liquid.PreserveDockBackdrop = value; appliedOptics = null; Update(); }
    }
    public FrameworkElement Element => liquid.Output;
    public float OuterRimStrokeScale { get => liquid.PopupRimStrokeScale; set => liquid.PopupRimStrokeScale = value; }
    public bool UseMaterialLighting
    {
        get => liquid.UseMaterialLighting;
        set
        {
            if (liquid.UseMaterialLighting == value) return;
            liquid.UseMaterialLighting = value;
            appliedOptics = null;
            if (value)
            {
                root.AddHandler(UIElement.PointerMovedEvent, pointerMoved, true);
                root.PointerExited += PointerExited;
            }
            else
            {
                root.RemoveHandler(UIElement.PointerMovedEvent, pointerMoved);
                root.PointerExited -= PointerExited;
                liquid.SetMaterialPointer(null);
            }
            Update();
        }
    }
    private readonly PointerEventHandler pointerMoved;
    private void PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(root).Position;
        liquid.SetMaterialPointer(new Vector2((float)point.X, (float)point.Y));
    }
    private void PointerExited(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(root).Position;
        if (point.X < 0 || point.Y < 0 || point.X >= root.ActualWidth || point.Y >= root.ActualHeight)
            liquid.SetMaterialPointer(null);
    }
    private readonly List<FrameworkElement> controlSurfaces = [];
    private bool controlsQueued;
    public void RefreshControls() => QueueControls();

    // Control Center only. All regions borrow this popup's single capture and blur.
    public void RegisterControl(FrameworkElement element)
    {
        if (controlSurfaces.Contains(element)) return;
        controlSurfaces.Add(element);
        element.Loaded += ControlLoaded;
        element.Unloaded += ControlLoaded;
        element.SizeChanged += ControlSizeChanged;
        QueueControls();
    }
    private void ControlLoaded(object sender, RoutedEventArgs args) => QueueControls();
    private void ControlSizeChanged(object sender, SizeChangedEventArgs args) => QueueControls();
    private void QueueControls()
    {
        if (disposed || controlsQueued) return;
        controlsQueued = true;
        window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            controlsQueued = false;
            if (disposed) return;
            var regions = new List<PopupGlassRegion>();
            foreach (var element in controlSurfaces)
            {
                if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
                var visible = true;
                for (DependencyObject? parent = element; parent is UIElement ui; parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
                    if (ui.Visibility != Visibility.Visible) { visible = false; break; }
                if (!visible) continue;
                var bounds = element.TransformToVisual(root).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
                var radius = element is Border border ? border.CornerRadius.TopLeft
                    : element is Control control ? control.CornerRadius.TopLeft : 0;
                regions.Add(new(new((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height), (float)(radius * bounds.Width / element.ActualWidth)));
            }
            // Parent cards first; smaller button lenses must not be painted over.
            regions.Sort((a, b) => (b.Bounds.Z * b.Bounds.W).CompareTo(a.Bounds.Z * a.Bounds.W));
            liquid.SetPopupRegions(regions.ToArray());
        });
    }

    public PopupLiquidGlassSurface(Window window, Panel root, DesktopGlassBackdrop backdrop)
    {
        this.window = window; this.root = root; this.backdrop = backdrop;
        pointerMoved = PointerMoved;
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
        QueueControls();
        var (bounds, radius, scale) = backdrop.PopupGeometry;
        if (!ReferenceEquals(appliedOptics, backdrop.PopupOptics))
        {
            appliedOptics = backdrop.PopupOptics;
            liquid.Material = UseMaterialLighting ? GlassDock.Core.Materials.ControlCenterGlassMaterial.Panel(appliedOptics)
                : useDockOptics ? appliedOptics : GlassDock.Core.Materials.UtilityMaterial.PopupOptics(appliedOptics);
            liquid.ControlMaterial = UseMaterialLighting ? GlassDock.Core.Materials.ControlCenterGlassMaterial.Control(appliedOptics)
                : DokyLiquidGlassSurface.ControlOptics(appliedOptics);
        }
        var active = root.IsLoaded && window.AppWindow.IsVisible &&
            backdrop.PopupMode == DockAppearanceMode.Clear && bounds.Z > 0 && bounds.W > 0;
        if (!active && !wasActive) return;
        wasActive = active;
        if (!active) liquid.SetMaterialPointer(null);
        // Root already carries the popup's fade/transform: do not apply opacity twice.
        liquid.Update(active, 1, scale > 0 ? scale : 1, bounds,
            new(bounds.X + bounds.Z / 2, Math.Max(1, bounds.Z / 2), 0, radius));
    }
    // The capture source normally excludes the popup HWND to avoid recursive
    // refraction. The screenshot path freezes a clean frame and temporarily
    // lifts that exclusion, exactly as for the main dock.
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
        root.RemoveHandler(UIElement.PointerMovedEvent, pointerMoved);
        root.PointerExited -= PointerExited;
        foreach (var element in controlSurfaces)
        {
            element.Loaded -= ControlLoaded;
            element.Unloaded -= ControlLoaded;
            element.SizeChanged -= ControlSizeChanged;
        }
        controlSurfaces.Clear();
        liquid.Dispose();
    }
}
