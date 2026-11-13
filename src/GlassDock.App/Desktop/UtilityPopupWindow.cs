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

internal class UtilityPopupWindow : Window
{
    internal const double Gutter = 8;
    private readonly nint dock;
    private readonly Grid root = new();
    private readonly DesktopGlassBackdrop transparentBackdrop = new() { UseSolidSurface = true };
    private readonly IUtilityPanel panel;
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
    internal Vector4? LeftOfColumn { get; set; }
    internal bool RightAlignToAnchor { get; init; }
    internal bool UseDockOptics { get; init; }
    internal bool UseQuickSettingsMotion { get; set; }
    internal Func<bool>? RelatedWindowHasForeground { get; set; }
    internal Func<bool, Task>? AnimateControls { get; init; }
    internal bool HasForeground => host.HasForegroundOrOwner(WinRT.Interop.WindowNative.GetWindowHandle(this));
    private int animationRevision;
    public IEnumerable<UtilityGlassSurface> GlassSurfaces => panel.GlassSurfaces;
    public int GlassTargetsRevision => panel.GlassTargetsRevision;
    public bool IsOpen { get; private set; }
    public bool IsDetailsOpen => IsOpen && !closing && !disposed;
    public event EventHandler? DetailsVisibilityChanged;
    public event EventHandler? Hidden;
    internal event EventHandler? Hiding;
    public event EventHandler? GlassChanged;

    public UtilityPopupWindow(nint dock, IUtilityPanel panel, UtilityTheme theme, string title)
    {
        this.dock = dock; this.panel = panel;
        Title = "Doky — " + title; WindowBranding.Apply(this); AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true; presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        var light = theme == UtilityTheme.Light;

        panelHost = new ContentControl { Content = panel.View, Margin = new(Gutter), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(panelHost);
        root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        Content = root;
        host = new(WinRT.Interop.WindowNative.GetWindowHandle(this)) { UseDockLayeredTransparency = true };
        host.Configure();
        transparentBackdrop.SetSolidAppearance(light ? DockAppearanceMode.Light : DockAppearanceMode.Dark, 0, 0);
        transparentBackdrop.SetSurfaceRegions([]);
        SystemBackdrop = transparentBackdrop;
        captureExcluded = (Application.Current as App)?.BasicRendering != true && host.SetCaptureExclusion(true);
        panel.View.SizeChanged += (_, _) => { if (IsOpen && !closing) Reposition(anchor); };
        panel.GlassChanged += (_, _) => { if (IsOpen && !closing) Reposition(anchor, surfacesChanged: true); };
        root.Loaded += (_, _) => { if (IsOpen) Reposition(anchor); };
        root.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Escape) { e.Handled = true; Hide(); } };
        root.PointerPressed += (_, e) => { if (AnimateControls is not null && ReferenceEquals(e.OriginalSource, root)) Hide(); };
        Activated += async (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated || snippingCaptureActive) return;
            if (host.IsScreenshotForeground()) ScreenshotRequested?.Invoke();
            if (snippingCaptureActive) return;
            await Task.Delay(180);
            if (disposed || !IsOpen || closing || snippingCaptureActive || host.HasForegroundOrOwner(dock) || RelatedWindowHasForeground?.Invoke() == true) return;
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
        panel.ConfigureGlass(backdrop, UseDockOptics ? optics : PopupMaterial.FrostedUtilityOptics(optics), layoutScale, highlights);
        var native = UseDockOptics && mode is (DockAppearanceMode.Frosted or DockAppearanceMode.Acrylic);
        var reconnect = transparentBackdrop.UseSolidSurface == native;
        transparentBackdrop.UseSolidSurface = !native;
        transparentBackdrop.VisualMaterial = native ? optics : null;
        if (native && mode.GlassStyle() is { } style)
            transparentBackdrop.ApplyMainDock(style, backdrop);
        else
            transparentBackdrop.SetSolidAppearance(mode, 0, 0);
        if (reconnect) transparentBackdrop.RebuildConnectedSurface();
    }

    public void BeginSnippingCapture()
    {
        if (disposed || !IsOpen || closing || snippingCaptureActive) return;
        snippingCaptureActive = true;
        if (captureExcluded) host.SetCaptureExclusion(false);
    }

    public void EndSnippingCapture()
    {
        if (disposed || !snippingCaptureActive) return;
        snippingCaptureActive = false;
        if ((captureExcluded && !host.SetCaptureExclusion(true)) || !host.HasForegroundOrOwner(dock)) Hide(immediate: true);
    }

    public void Show(Vector4 source)
    {
        if (disposed) return;
        if (AnimateControls is not null) root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
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
        panel.View.Measure(new(panel.View.Width, double.PositiveInfinity));
        var height = Math.Min(panel.View.DesiredSize.Height + Gutter * 2, area.Height / dpi - 16);
        var width = Math.Min(panel.View.Width + Gutter * 2, area.Width / dpi);
        panelHost.MaxHeight = Math.Max(1, height - Gutter * 2);
        var rectangle = WindowPreviewLayout.Position(area, dockBounds, dpi, source.X + source.Z / 2, source.Y - 6,
            width, Math.Max(1, height));
        if (LeftOfColumn is { } column)
        {
            var x = dockBounds.X + (column.X - width + Gutter - 12) * dpi;
            var y = dockBounds.Y + (column.Y + column.W - height + Gutter) * dpi;
            rectangle = new(Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - width * dpi)),
                Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - height * dpi)), width * dpi, height * dpi);
        }
        else if (RightAlignToAnchor)
        {
            var x = dockBounds.X + (source.X + source.Z - width + Gutter) * dpi;
            rectangle = rectangle with { X = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - width * dpi)) };
        }
        if (WindowPreviewLayout.NeedsNativeMove(positioned, rectangle))
        {
            positioned = rectangle;
            AppWindow.MoveAndResize(new((int)rectangle.X, (int)rectangle.Y, (int)rectangle.Width, (int)rectangle.Height));
        }
        transparentBackdrop.SetBounds(rectangle.Width / dpi, rectangle.Height / dpi,
            rectangle.Width / dpi, rectangle.Height / dpi, 0, dpi, 1);
        if (panel is QuickSettingsPanel quick) quick.UseNativeBackdrop = !transparentBackdrop.UseSolidSurface;
        var changed = panel.PositionGlass(root, new((float)((rectangle.X - dockBounds.X) / dpi),
            (float)((rectangle.Y - dockBounds.Y) / dpi)), captureExcluded);
        if (panel is QuickSettingsPanel nativePanel)
            transparentBackdrop.SetUtilitySurfaces(nativePanel.UseNativeBackdrop ? nativePanel.NativeGlassSurfaces(root) : []);
        if (changed || surfacesChanged) GlassChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Animate(bool hide)
    {
        animationRevision++; batch?.Dispose(); batch = null;
        if (AnimateControls is { } animate) { _ = AnimateControlsAsync(animate, hide, animationRevision); return; }
        var visual = ElementCompositionPreview.GetElementVisual(root);
        track ??= new(visual); track.Stop();
        if (!animations && !UseQuickSettingsMotion) { if (hide) FinishHide(); return; }
        var frames = Enumerable.Range(0, 9).Select(i =>
        {
            var t = i / 8f;
            var eased = UseQuickSettingsMotion && !hide ? 1 + 2.3f * MathF.Pow(t - 1, 3) + 1.3f * MathF.Pow(t - 1, 2)
                : t * t * (3 - 2 * t);
            var visible = hide ? 1 - eased : eased;
            var transform = Matrix4x4.CreateScale(.94f + .06f * visible, .94f + .06f * visible, 1,
                new((float)(AppWindow.Size.Width / dpi / 2), (float)(AppWindow.Size.Height / dpi), 0));
            return new PopupCompositionFrame(animations ? transform * Matrix4x4.CreateTranslation(0, (1 - visible) * 8, 0) : Matrix4x4.Identity,
                Math.Clamp(visible, 0, 1));
        }).ToArray();
        var current = animationRevision;
        batch = visual.Compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        track.Bind(); track.Start(frames, TimeSpan.FromMilliseconds(UseQuickSettingsMotion ? !animations ? 100 : hide ? 140 : 260 : hide ? 120 : 180));
        batch.Completed += (_, _) =>
        {
            if (disposed || current != animationRevision) return;
            track.Stop(); visual.TransformMatrix = Matrix4x4.Identity; visual.Opacity = 1;
            if (hide) FinishHide();
        };
        batch.End();
    }

    private async Task AnimateControlsAsync(Func<bool, Task> animate, bool hide, int current)
    {
        await animate(hide);
        if (!disposed && current == animationRevision && hide) FinishHide();
    }

    public void Hide(bool immediate = false)
    {
        if (disposed || !IsOpen || (closing && !immediate)) return;
        closing = true; root.IsHitTestVisible = false;
        Hiding?.Invoke(this, EventArgs.Empty);
        DetailsVisibilityChanged?.Invoke(this, EventArgs.Empty);
        if (immediate) FinishHide(); else Animate(true);
    }
    private void FinishHide()
    {
        IsOpen = false; foreach (var glass in GlassSurfaces) glass.Active = false;
        AppWindow.Hide(); Close();
    }
}
