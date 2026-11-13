using System.Numerics;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace GlassDock.App.Rendering;

internal sealed class DokyLiquidGlassSurface : IDisposable
{
    private readonly LiquidGlassDrawingTarget? popupTarget;
    private readonly LiquidGlassRim rim = new();
    public bool CircularHighlightsEnabled { get; set; } = true;
    public float DockLayoutScale { get; set; } = 1;
    public float PopupRimStrokeScale { get; set; } = 1;
    private readonly Action<CanvasDrawingSession, CanvasGeometry> drawRim;
    private CropEffect? popupCrop;
    private GaussianBlurEffect? popupBlur;
    private ExposureEffect? popupExposure;
    public bool PreserveDockBackdrop { get; set; }
    public FrameworkElement Output => popupTarget?.Element ?? (FrameworkElement)Panel;
    public CanvasSwapChainPanel Panel { get; } = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    public Grid LensPanel { get; } = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private CompositionGraphicsDevice? lensGraphicsDevice;
    private CompositionDrawingSurface? lensSurface;
    private CompositionSurfaceBrush? lensBrush;
    private SpriteVisual? lensVisual;
    private CanvasRenderTarget? lensScene;
    private PixelShaderEffect? lensShader;
    private ArithmeticCompositeEffect? lensBackdrop;
    private CompositeEffect? lensComposite;
    private GaussianBlurEffect? lensBlur;
    private SaturationEffect? lensSaturation;
    private ExposureEffect? lensExposure;
    private ContrastEffect? lensContrast;
    private ColorSourceEffect? lensTint;
    private Action<CanvasDrawingSession>? paintDock;
    private Vector4 lensBounds;
    private float lensOpacity;
    private bool lensMerge, clearActive;
    private IReadOnlyList<UtilityGlassSurface> utilities = Array.Empty<UtilityGlassSurface>();
    private bool UtilitiesActive => utilities.Any(u => u.Active);
    public void SetUtilities(IReadOnlyList<UtilityGlassSurface> value) { utilities = value; QueueDraw(); }
    private DockAppearanceMode appearanceMode;
    private GlassMaterial dockMaterial = new();
    public GlassMaterial BackdropMaterial => dockMaterial;
    private static readonly LiquidGlassMaterial LensMaterial = new LiquidGlassMaterial
    { RefractionStrength = 11, EdgeThickness = 17, DiffusionAmount = .35f, ChromaticDispersion = .85f, SpecularIntensity = .20f }.Normalize();
    public string Status { get; private set; } = "Liquid inactive";
    public event EventHandler? StatusChanged;
    private readonly nint hwnd;
    private readonly DispatcherQueue dispatcher;
    private readonly Func<CanvasGeometry?> outline;
    private readonly Action<bool> setNativeSuppressed;
    private CanvasDevice? device;
    private CanvasSwapChain? swapChain;
    private PixelShaderEffect? shader;
    private LiquidGlassShaderState? shaderState, lensShaderState;
    private DesktopCaptureSource? capture;
    private InputDesktopMonitor? inputDesktop;
    private DesktopCaptureSource.Placement placement;
    private Direct3D11CaptureFrame? currentFrame;
    private CanvasBitmap? bitmap;
    private bool disposed, failed, enabled, screenshotFrozen, desktopRecoveryQueued;
    private readonly GlassDock.Core.Desktop.CaptureDrawGate drawGate = new();
    private long queuedAt;
    private Vector4 dock, wave;
    private float scale = 1;
    private LiquidGlassMaterial material = new() { RefractionStrength = 12 };
    public LiquidGlassMaterial Material { get => material; set { if (material != value) material = value.Normalize(); } }

    public DokyLiquidGlassSurface(nint window, DispatcherQueue queue, Func<CanvasGeometry?> geometry, Action<bool> suppressNative,
        bool popup = false)
    {
        hwnd = window; dispatcher = queue; outline = geometry; setNativeSuppressed = suppressNative;
        drawRim = DrawRim;
        if (popup) { popupTarget = new(); Output.Visibility = Visibility.Collapsed; }
    }

    public void SetAppearance(DockAppearanceMode mode, GlassMaterial material)
    { appearanceMode = mode; dockMaterial = material; }

    public void BeginLens(Action<CanvasDrawingSession> drawDock)
    { paintDock = drawDock; }

    public void UpdateLens(Vector4 bounds, float opacity, bool merging)
    { lensBounds = bounds; lensOpacity = opacity; lensMerge = merging; QueueDraw(); }

    public void EndLens()
    {
        paintDock = null;
        ReleaseLens();
        if (!clearActive && !UtilitiesActive) { enabled = false; Stop(); }
    }

    private void ReleaseLens()
    {
        LensPanel.Visibility = Visibility.Collapsed;
        ElementCompositionPreview.SetElementChildVisual(LensPanel, null);
        if (lensVisual is not null) lensVisual.Brush = null;
        if (lensBrush is not null) lensBrush.Surface = null;
        lensVisual?.Dispose(); lensVisual = null;
        lensBrush?.Dispose(); lensBrush = null;
        lensSurface?.Dispose(); lensSurface = null;
        lensGraphicsDevice?.Dispose(); lensGraphicsDevice = null;
        lensShader?.Dispose(); lensShader = null; lensShaderState = null;
        lensScene?.Dispose(); lensScene = null;
        lensBackdrop?.Dispose(); lensBackdrop = null;
        lensComposite?.Dispose(); lensComposite = null;
        lensBlur?.Dispose(); lensBlur = null;
        lensSaturation?.Dispose(); lensSaturation = null;
        lensExposure?.Dispose(); lensExposure = null;
        lensContrast?.Dispose(); lensContrast = null;
        lensTint?.Dispose(); lensTint = null;
    }

    public void Update(bool active, double opacity, float dpiScale, Vector4 dockBounds, Vector4 waveShape)
    {
        if (disposed) return;
        if (screenshotFrozen)
        {
            if (!active && !UtilitiesActive) EndScreenshotMode();
            else return;
        }
        dock = dockBounds; wave = waveShape; scale = dpiScale;
        if (!active && paintDock is null && !UtilitiesActive && !clearActive && !enabled && !failed && capture is null)
            return;
        clearActive = active;
        if (Math.Abs(Output.Opacity - opacity) > 0.0001)
            Output.Opacity = opacity;
        if (!active)
        {
            if (Output.Visibility != Visibility.Collapsed)
                Output.Visibility = Visibility.Collapsed;
            setNativeSuppressed(false);
        }
        if (!active && paintDock is null && !UtilitiesActive)
        {
            enabled = false; failed = false;
            Stop();
            return;
        }
        enabled = true;
        try
        {
            var next = DesktopCaptureSource.Locate(hwnd);
            if (next.Width <= 0 || next.Height <= 0) return;
            var sourceChanged = !next.SameCaptureSource(placement);
            if (failed && !sourceChanged) return;
            if (sourceChanged)
            {
                if (capture is not null) Stop();
                failed = false;
            }
            placement = next;
            if (capture is null)
            {
                if (inputDesktop is null)
                {
                    inputDesktop = new();
                    inputDesktop.Restored += InputDesktopRestored;
                }
                device = CanvasDevice.GetSharedDevice();
                shader = new PixelShaderEffect(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", "LiquidGlass.bin")));
                {
                    popupCrop = new();
                    popupBlur = new() { Source = popupCrop, BorderMode = EffectBorderMode.Hard };
                    popupExposure = new() { Source = popupBlur, Exposure = PopupMaterial.LiquidBackdropExposure };
                }
                capture = new(hwnd, device.As<IDirect3DDevice>(), next);
                if (GlassDock.Core.Desktop.DockPerformanceDiagnostics.Enabled)
                    StartupDiagnostics.Write($"Capture source: hwnd={hwnd:X} monitor={next.Monitor:X} pixels={next.MonitorWidth}x{next.MonitorHeight} popup={popupTarget is not null}");
                capture.FrameAvailable += FrameAvailable;
                capture.Closed += CaptureClosed;
                SetStatus(CaptureBorderPermission.IsAllowed
                    ? "Liquid awaiting GPU capture · borderless permission granted"
                    : "Liquid awaiting GPU capture · Windows capture indicator required");
            }
            if (popupTarget is null && clearActive && swapChain is null)
            {
                swapChain = new CanvasSwapChain(device!, next.Width, next.Height, 96);
                Panel.SwapChain = swapChain;
            }
            if (swapChain is not null && (swapChain.Size.Width != next.Width || swapChain.Size.Height != next.Height))
                swapChain.ResizeBuffers(next.Width, next.Height);
            var panelWidth = next.Width / scale;
            var panelHeight = next.Height / scale;
            if (Math.Abs(Panel.Width - panelWidth) > 0.01 || Math.Abs(Panel.Height - panelHeight) > 0.01)
            {
                Panel.Width = panelWidth; Panel.Height = panelHeight;
                LensPanel.Width = panelWidth; LensPanel.Height = panelHeight;
            }
            QueueDraw();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); }
    }
    public bool BeginScreenshotMode()
    {
        if (screenshotFrozen) return true;
        if (disposed || !enabled || capture is null || bitmap is null ||
            !(clearActive && Output.Visibility == Visibility.Visible || utilities.Any(u => u.Active && u.HasFrame))) return false;
        CanvasRenderTarget? snapshot = null;
        try
        {
            snapshot = new CanvasRenderTarget(device!, bitmap.SizeInPixels.Width, bitmap.SizeInPixels.Height, 96);
            using (var drawing = snapshot.CreateDrawingSession()) drawing.DrawImage(bitmap);
            shader!.Source1 = null;
            if (popupCrop is not null) popupCrop.Source = null;
            if (lensBackdrop is not null) lensBackdrop.Source1 = null;
            if (lensBlur is not null) lensBlur.Source = null;
            bitmap.Dispose(); bitmap = snapshot; snapshot = null;
            currentFrame?.Dispose(); currentFrame = null;
            screenshotFrozen = true;
            if (!capture.PauseForScreenshot())
            {
                screenshotFrozen = false;
                QueueDraw();
                return false;
            }
            DrawFrame(visualChanged: true, frozenFrame: true);
            if (!screenshotFrozen) return false;
            SetStatus("Liquid screenshot mode · frozen clean frame");
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Fail(error);
            return false;
        }
        finally { snapshot?.Dispose(); }
    }

    public void EndScreenshotMode()
    {
        if (!screenshotFrozen) { failed = false; return; }
        screenshotFrozen = false;
        try
        {
            capture?.ResumeAfterScreenshot();
            DrawFrame(visualChanged: true, frozenFrame: true);
            QueueDraw();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); }
    }

    private void FrameAvailable(object? sender, EventArgs args) { if (!screenshotFrozen) QueueDraw(visualChanged: false); }
    private void InputDesktopRestored(object? sender, EventArgs args)
    {
        if (disposed || desktopRecoveryQueued) return;
        desktopRecoveryQueued = true;
        if (!dispatcher.TryEnqueue(() =>
        {
            desktopRecoveryQueued = false;
            if (disposed || !enabled || screenshotFrozen || inputDesktop?.IsCurrent != true) return;
            try
            {
                Stop();
                failed = false;
                Update(clearActive, Output.Opacity, scale, dock, wave);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); }
        })) desktopRecoveryQueued = false;
    }
    private void CaptureClosed(object? sender, EventArgs args) => dispatcher.TryEnqueue(() =>
    {
        if (!disposed && ReferenceEquals(sender, capture)) Fail(new InvalidOperationException("Monitor capture closed."));
    });
    private void QueueDraw(bool visualChanged = true)
    {
        if (!drawGate.Request(visualChanged)) return;
        queuedAt = GlassDock.Core.Desktop.DockPerformanceDiagnostics.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (!dispatcher.TryEnqueue(Draw)) drawGate.EnqueueFailed();
    }
    private void Draw() => DrawFrame(drawGate.Begin());
    private void DrawFrame(bool visualChanged, bool frozenFrame = false)
    {
        var scheduledAt = queuedAt;
        if (disposed || !enabled || (screenshotFrozen && !frozenFrame) || capture is null || shader is null ||
            (clearActive && swapChain is null && popupTarget is null)) return;
        var started = GlassDock.Core.Desktop.DockPerformanceDiagnostics.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        long preparedAt = 0;
        if (started != 0) GlassDock.Core.Desktop.DockPerformanceDiagnostics.RecordDrawQueueDelay(started - scheduledAt);
        try
        {
            var pending = frozenFrame ? null : capture.TakeFrame();
            if (pending is null && !visualChanged)
            {
                GlassDock.Core.Desktop.DockPerformanceDiagnostics.RecordEmptyCaptureDraw();
                return;
            }
            if (pending is not null)
            {
                if (pending.ContentSize.Width != placement.MonitorWidth || pending.ContentSize.Height != placement.MonitorHeight)
                {
                    pending.Dispose();
                    if (DesktopCaptureSource.Locate(hwnd).SameCaptureSource(placement))
                        throw new InvalidOperationException("Capture frame dimensions do not match the monitor; retaining native fallback.");
                    Stop();
                    Update(clearActive, Output.Opacity, scale, dock, wave);
                    return;
                }
                shader.Source1 = null;
                if (popupCrop is not null) popupCrop.Source = null;
                if (lensBackdrop is not null) lensBackdrop.Source1 = null;
                if (lensBlur is not null) lensBlur.Source = null;
                bitmap?.Dispose();
                currentFrame?.Dispose(); currentFrame = pending;
                bitmap = CanvasBitmap.CreateFromDirect3D11Surface(device!, pending.Surface);
            }
            if (bitmap is null) return;
            var geometry = outline();
            if (geometry is null) return;
            GlassDock.Core.Desktop.DockPerformanceDiagnostics.RecordCaptureDraw();
            shader.Source1 = bitmap;
            if (popupCrop is not null)
            {
                var margin = (64 + Material.BlurAmount * 3) * scale;
                var x = Math.Max(0, placement.X - margin);
                var y = Math.Max(0, placement.Y - margin);
                popupCrop.SourceRectangle = new(x, y,
                    Math.Min(placement.MonitorWidth, placement.X + placement.Width + margin) - x,
                    Math.Min(placement.MonitorHeight, placement.Y + placement.Height + margin) - y);
                popupCrop.Source = bitmap;
                popupBlur!.BlurAmount = (popupTarget is null || PreserveDockBackdrop
                    ? Material.BlurAmount : Math.Max(Material.BlurAmount, PopupMaterial.LiquidBackdropBlur)) * scale;
                popupBlur.Optimization = GlassEffectGraph.Quality((int)Material.BlurQuality);
                popupExposure!.Exposure = popupTarget is null || PreserveDockBackdrop ? 0 : PopupMaterial.LiquidBackdropExposure;
                if (popupBlur.BlurAmount > 0 || popupExposure.Exposure != 0) shader.Source1 = popupExposure;
            }
            var origin = new Vector2(placement.X, placement.Y);
            ConfigureShader(shader, Material, dock * scale + new Vector4(origin, 0, 0),
                wave * scale + new Vector4(origin.X, 0, 0, 0), new(placement.MonitorWidth, placement.MonitorHeight), Vector4.Zero);
            if (clearActive)
            {
                if (started != 0) preparedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                var crop = new global::Windows.Foundation.Rect(placement.X, placement.Y, placement.Width, placement.Height);
                if (popupTarget is not null)
                {
                    popupTarget.Draw(device!, shader, geometry, crop, scale, drawRim);
                }
                else
                {
                    using (var drawing = swapChain!.CreateDrawingSession(Microsoft.UI.Colors.Transparent))
                    using (drawing.CreateLayer(1, geometry))
                    {
                        drawing.DrawImage(shader, Vector2.Zero, crop);
                        DrawRim(drawing, geometry);
                    }
                    swapChain.Present(0);
                }
                Output.Visibility = Visibility.Visible;
                setNativeSuppressed(true);
            }
            if (paintDock is not null) DrawLens(geometry);
            foreach (var utility in utilities)
                utility.Draw(device!, bitmap, origin, new(placement.MonitorWidth, placement.MonitorHeight), scale);
            shader.Source1 = null;
            SetStatus(clearActive ? "Liquid active · GPU desktop refraction"
                : "Liquid drag lens active · GPU dock refraction");
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); }
        finally
        {
            if (started != 0) GlassDock.Core.Desktop.DockPerformanceDiagnostics.RecordDrawDuration(System.Diagnostics.Stopwatch.GetTimestamp() - started);
            if (preparedAt != 0) GlassDock.Core.Desktop.DockPerformanceDiagnostics.RecordRendererTiming(
                popupTarget is not null, preparedAt - started, System.Diagnostics.Stopwatch.GetTimestamp() - preparedAt);
        }
    }

    public void DeactivateCapture()
    {
        if (disposed) return;
        clearActive = false;
        enabled = false;
        Stop();
    }

    private void ConfigureShader(PixelShaderEffect effect, LiquidGlassMaterial m, Vector4 bounds,
        Vector4 shape, Vector2 frame, Vector4 lens)
    {
        ref var previous = ref (ReferenceEquals(effect, shader) ? ref shaderState : ref lensShaderState);
        LiquidGlassShader.Configure(effect, m, scale, bounds, shape, frame, lens, ref previous);
    }

    private void DrawRim(CanvasDrawingSession drawing, CanvasGeometry geometry)
    {
        rim.Draw(drawing, geometry, dock, scale,
            popupTarget is not null || CircularHighlightsEnabled ? Material : Material with { SpecularIntensity = 0 },
            dockSurface: popupTarget is null, strokeScale: popupTarget is null ? DockLayoutScale : PopupRimStrokeScale);
    }

    private void DrawLens(CanvasGeometry geometry)
    {
        if (lensVisual is null)
        {
            var compositor = ElementCompositionPreview.GetElementVisual(LensPanel).Compositor;
            lensGraphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, device!);
            lensSurface = lensGraphicsDevice.CreateDrawingSurface(new(placement.Width, placement.Height),
                Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
            lensBrush = compositor.CreateSurfaceBrush(lensSurface);
            lensBrush.Stretch = CompositionStretch.Fill;
            lensVisual = compositor.CreateSpriteVisual();
            lensVisual.Brush = lensBrush;
            ElementCompositionPreview.SetElementChildVisual(LensPanel, lensVisual);
            lensShader = new PixelShaderEffect(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", "LiquidGlass.bin")));
            lensBackdrop = (ArithmeticCompositeEffect)GlassEffectGraph.Create(bitmap!);
            lensComposite = (CompositeEffect)lensBackdrop.Source2;
            lensContrast = (ContrastEffect)lensComposite.Sources[0];
            lensExposure = (ExposureEffect)lensContrast.Source;
            lensSaturation = (SaturationEffect)lensExposure.Source;
            lensBlur = (GaussianBlurEffect)lensSaturation.Source;
            lensTint = (ColorSourceEffect)lensComposite.Sources[1];
        }
        if (lensScene is null || lensScene.Size.Width != placement.Width || lensScene.Size.Height != placement.Height)
        {
            lensScene?.Dispose();
            lensScene = new CanvasRenderTarget(device!, placement.Width, placement.Height, 96);
            CanvasComposition.Resize(lensSurface!, new(placement.Width, placement.Height));
        }
        lensVisual.Size = new(placement.Width / scale, placement.Height / scale);
        var crop = new global::Windows.Foundation.Rect(placement.X, placement.Y, placement.Width, placement.Height);
        using (var drawing = lensScene.CreateDrawingSession())
        {
            drawing.Clear(Microsoft.UI.Colors.Transparent);
            drawing.DrawImage(bitmap!, Vector2.Zero, crop);
            using (drawing.CreateLayer(1, geometry))
            {
                if (clearActive)
                {
                    drawing.DrawImage(shader!, Vector2.Zero, crop);
                    DrawRim(drawing, geometry);
                }
                else if (appearanceMode.GlassStyle() is null)
                    drawing.FillRectangle(new(0, 0, placement.Width, placement.Height), Desktop.DockControlPalette.SolidSurface(appearanceMode));
                else
                {
                    lensBackdrop!.Source1 = bitmap; lensBlur!.Source = bitmap;
                    lensBlur.BlurAmount = (float)dockMaterial.BlurAmount * scale;
                    lensBlur.Optimization = GlassEffectGraph.Quality(dockMaterial.BlurQuality);
                    lensContrast!.Contrast = (float)(dockMaterial.Contrast - 1);
                    lensSaturation!.Saturation = (float)dockMaterial.Saturation;
                    lensExposure!.Exposure = (float)Math.Log2(dockMaterial.Brightness);
                    lensTint!.Color = GlassEffectGraph.Tint(dockMaterial);
                    lensBackdrop.Source1Amount = (float)(1 - dockMaterial.Opacity);
                    lensBackdrop.Source2Amount = (float)dockMaterial.Opacity;
                    drawing.DrawImage(lensBackdrop, Vector2.Zero, crop);
                }
            }
            paintDock!(drawing);
        }
        lensShader!.Source1 = lensScene;
        var bounds = lensBounds * scale;
        ConfigureShader(lensShader, LensMaterial, bounds,
            new(bounds.X + bounds.Z / 2, bounds.Z / 2, 0, Math.Min(bounds.Z, bounds.W) / 2),
            new(placement.Width, placement.Height), new(lensMerge ? .11f : .07f, 0, 1, lensOpacity));
        using (var drawing = CanvasComposition.CreateDrawingSession(lensSurface!))
        {
            drawing.Clear(Microsoft.UI.Colors.Transparent);
            drawing.DrawImage(lensShader, Vector2.Zero, new global::Windows.Foundation.Rect(0, 0, placement.Width, placement.Height));
        }
        lensShader.Source1 = null;
        LensPanel.Visibility = Visibility.Visible;
    }
    private void Fail(Exception error)
    {
        failed = true;
        Stop();
        SetStatus($"Liquid unavailable ({error.Message}) · native Clear fallback, no refraction");
        StartupDiagnostics.Write("Liquid Glass failed", error);
    }
    private void SetStatus(string status)
    {
        if (Status == status) return;
        Status = status; StartupDiagnostics.Write(status); StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Stop()
    {
        foreach (var utility in utilities) utility.Hide();
        screenshotFrozen = false;
        ReleaseLens();
        Output.Visibility = Visibility.Collapsed;
        popupTarget?.Dispose();
        setNativeSuppressed(false);
        if (capture is not null) { capture.FrameAvailable -= FrameAvailable; capture.Closed -= CaptureClosed; }
        shader?.Dispose(); shader = null; shaderState = null;
        rim.Dispose();
        popupExposure?.Dispose(); popupExposure = null;
        popupBlur?.Dispose(); popupBlur = null;
        popupCrop?.Dispose(); popupCrop = null;
        bitmap?.Dispose(); bitmap = null;
        currentFrame?.Dispose(); currentFrame = null;
        capture?.Dispose(); capture = null;
        Panel.SwapChain = null;
        swapChain?.Dispose(); swapChain = null;
        device = null;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; enabled = false;
        if (inputDesktop is not null)
        {
            inputDesktop.Restored -= InputDesktopRestored;
            inputDesktop.Dispose(); inputDesktop = null;
        }
        Stop();
    }
}
