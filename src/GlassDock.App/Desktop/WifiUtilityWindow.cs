using System.Numerics;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Applications;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

/// <summary>An activating utility popup; its glass is drawn by the dock's existing capture.</summary>
internal sealed class WifiUtilityWindow : Window
{
    private const double Gutter = 8;
    private readonly nint dock;
    private readonly Grid root = new();
    private readonly DesktopGlassBackdrop transparentBackdrop = new() { UseSolidSurface = true };
    private readonly WifiNetworkPanel panel;
    private readonly ContentControl panelHost;
    private readonly InteractiveGlassWindowHost host;
    private readonly bool animations = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private readonly bool captureExcluded;
    private PopupCompositionTrack? track;
    private Microsoft.UI.Composition.CompositionScopedBatch? batch;
    private PreviewRect? positioned;
    private Vector4 anchor;
    private double dpi = 1;
    private bool closing, disposed, snippingCaptureActive;
    public Action? ScreenshotRequested { get; init; }
    private int animationRevision;
    public IEnumerable<UtilityGlassSurface> GlassSurfaces => panel.GlassSurfaces;
    public int GlassTargetsRevision => panel.GlassTargetsRevision;
    public bool IsOpen { get; private set; }
    public bool IsDetailsOpen => IsOpen && !closing && !disposed;
    public event EventHandler? DetailsVisibilityChanged;
    public event EventHandler? Hidden;
    public event EventHandler? GlassChanged;

    public WifiUtilityWindow(nint dock, WifiUtilityService wifi, UtilityTheme theme)
    {
        this.dock = dock;
        Title = "Doky — Wi-Fi"; WindowBranding.Apply(this); AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true; presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        var light = theme == UtilityTheme.Light;
        panel = new(wifi, theme);
        panelHost = new ContentControl { Content = panel, Margin = new(Gutter), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(panelHost);
        root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        Content = root;
        host = new(WinRT.Interop.WindowNative.GetWindowHandle(this)) { UseDockLayeredTransparency = true };
        host.Configure();
        transparentBackdrop.SetSolidAppearance(light ? DockAppearanceMode.Light : DockAppearanceMode.Dark, 0, 0);
        SystemBackdrop = transparentBackdrop;
        captureExcluded = (Application.Current as App)?.BasicRendering != true && host.SetCaptureExclusion(true);
        panel.SizeChanged += (_, _) => { if (IsOpen && !closing) Reposition(anchor); };
        panel.GlassChanged += (_, _) => { if (IsOpen && !closing) Reposition(anchor, surfacesChanged: true); };
        root.Loaded += (_, _) => { if (IsOpen) Reposition(anchor); };
        root.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Escape) { e.Handled = true; Hide(); } };
        Activated += async (_, e) =>
        {
            // The dock owns explicit outside-click dismissal; its activation is not an outside application.
            if (e.WindowActivationState != WindowActivationState.Deactivated || snippingCaptureActive) return;
            if (host.IsScreenshotForeground()) ScreenshotRequested?.Invoke();
            if (snippingCaptureActive) return;
            // Allow the existing asynchronous Win+Shift+S notification to arrive first.
            await Task.Delay(180);
            if (disposed || !IsOpen || closing || snippingCaptureActive || host.HasForegroundOrOwner(dock)) return;
            if (host.IsScreenshotForeground()) ScreenshotRequested?.Invoke();
            if (!snippingCaptureActive) Hide();
        };
        Closed += (_, _) =>
        {
            if (disposed) return;
            disposed = true; IsOpen = false;
            DetailsVisibilityChanged?.Invoke(this, EventArgs.Empty);
            panel.Dispose(); animationRevision++; batch?.Dispose(); track?.Dispose();
            SystemBackdrop = null; host.Dispose(); Hidden?.Invoke(this, EventArgs.Empty);
        };
    }

    public void ApplyAppearance(DockAppearanceMode mode, GlassMaterial backdrop, LiquidGlassMaterial optics, float layoutScale, bool highlights)
    {
        panel.ConfigureGlass(backdrop, PopupMaterial.FrostedUtilityOptics(optics), layoutScale, highlights);
    }

    public void BeginSnippingCapture()
    {
        if (disposed || !IsOpen || closing || snippingCaptureActive) return;
        // The owning dock has already stopped the shared monitor capture.
        snippingCaptureActive = true;
        if (captureExcluded) host.SetCaptureExclusion(false);
    }

    public void EndSnippingCapture()
    {
        if (disposed || !snippingCaptureActive) return;
        snippingCaptureActive = false;
        // Re-exclude before shared sampling resumes; close if exclusion cannot be restored.
        if ((captureExcluded && !host.SetCaptureExclusion(true)) || !host.HasForegroundOrOwner(dock)) Hide(immediate: true);
    }

    public void Show(Vector4 source)
    {
        if (disposed) return;
        if (IsOpen && !closing) { Reposition(source); Activate(); host.ActivateForUserInput(); _ = panel.RefreshAsync(); return; }
        IsOpen = true; closing = false; root.IsHitTestVisible = true;
        Reposition(source); Activate(); host.ActivateForUserInput();
        DetailsVisibilityChanged?.Invoke(this, EventArgs.Empty);
        Animate(false); _ = panel.RefreshAsync();
    }

    public void Reposition(Vector4 source, bool surfacesChanged = false)
    {
        anchor = source;
        if (!IsOpen || disposed) return;
        var (area, scale, dockBounds) = WindowPreviewPlacement.GetArea(dock); dpi = scale;
        panel.SetAvailableHeight(Math.Max(1, area.Height / dpi - 16 - Gutter * 2));
        panel.Measure(new(panel.Width, double.PositiveInfinity));
        var height = Math.Min(panel.DesiredSize.Height + Gutter * 2, area.Height / dpi - 16);
        var width = Math.Min(panel.Width + Gutter * 2, area.Width / dpi);
        panelHost.MaxHeight = Math.Max(1, height - Gutter * 2);
        var rectangle = WindowPreviewLayout.Position(area, dockBounds, dpi, source.X + source.Z / 2, source.Y - 6,
            width, Math.Max(1, height));
        if (WindowPreviewLayout.NeedsNativeMove(positioned, rectangle))
        {
            positioned = rectangle;
            AppWindow.MoveAndResize(new((int)rectangle.X, (int)rectangle.Y, (int)rectangle.Width, (int)rectangle.Height));
        }
        transparentBackdrop.SetBounds(rectangle.Width / dpi, rectangle.Height / dpi,
            rectangle.Width / dpi, rectangle.Height / dpi, 0, dpi, 1);
        var changed = panel.PositionGlass(root, new((float)((rectangle.X - dockBounds.X) / dpi),
            (float)((rectangle.Y - dockBounds.Y) / dpi)), captureExcluded);
        if (changed || surfacesChanged) GlassChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Animate(bool hide)
    {
        animationRevision++; batch?.Dispose(); batch = null;
        var visual = ElementCompositionPreview.GetElementVisual(root);
        track ??= new(visual); track.Stop();
        if (!animations) { if (hide) FinishHide(); return; }
        var frames = Enumerable.Range(0, 9).Select(i =>
        {
            var t = i / 8f; var eased = t * t * (3 - 2 * t); var visible = hide ? 1 - eased : eased;
            var transform = Matrix4x4.CreateScale(.94f + .06f * visible, .94f + .06f * visible, 1,
                new((float)(AppWindow.Size.Width / dpi / 2), (float)(AppWindow.Size.Height / dpi), 0));
            return new PopupCompositionFrame(transform * Matrix4x4.CreateTranslation(0, (1 - visible) * 8, 0), visible);
        }).ToArray();
        var current = animationRevision;
        batch = visual.Compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        track.Bind(); track.Start(frames, TimeSpan.FromMilliseconds(hide ? 120 : 180));
        batch.Completed += (_, _) =>
        {
            if (disposed || current != animationRevision) return;
            track.Stop(); visual.TransformMatrix = Matrix4x4.Identity; visual.Opacity = 1;
            if (hide) FinishHide();
        };
        batch.End();
    }

    public void Hide(bool immediate = false)
    {
        if (disposed || !IsOpen || (closing && !immediate)) return;
        closing = true; root.IsHitTestVisible = false;
        DetailsVisibilityChanged?.Invoke(this, EventArgs.Empty);
        if (immediate) FinishHide(); else Animate(true);
    }
    private void FinishHide()
    {
        IsOpen = false; foreach (var glass in GlassSurfaces) glass.Active = false;
        AppWindow.Hide(); Close();
    }
}
