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

/// <summary>One retained GPU shader and swap chain per Clear dock. No CPU pixel readback.</summary>
internal sealed class DokyLiquidGlassSurface : IDisposable
{
    private readonly LiquidGlassDrawingTarget? popupTarget;
    private readonly LiquidGlassRim rim = new();
    private readonly Action<CanvasDrawingSession, CanvasGeometry> drawRim;
    private CropEffect? popupCrop;
    private GaussianBlurEffect? popupBlur;
    private ExposureEffect? popupExposure;
    public FrameworkElement Output => popupTarget?.Element ?? (FrameworkElement)Panel;
    public CanvasSwapChainPanel Panel { get; } = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    // SwapChainPanel punches through underlying XAML even at transparent pixels.
    // A composition drawing surface blends with the live icons outside the lens.
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
    private ColorSourceEffect? lensTint;
    private Action<CanvasDrawingSession>? paintDock;
    private Vector4 lensBounds;
    private float lensOpacity;
    private bool lensMerge, clearActive;
    private DockAppearanceMode appearanceMode;
    private GlassMaterial dockMaterial = new();
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
    private DesktopCaptureSource? capture;
    private DesktopCaptureSource.Placement placement;
    private Direct3D11CaptureFrame? currentFrame;
    private CanvasBitmap? bitmap;
    private bool disposed, failed, enabled;
    private int queued;
    private Vector4 dock, wave;
    private float scale = 1;
    // Stronger Clear rim bending within the existing narrow edge band.
    // The drag lens has its own material and retains its current optics.
    private LiquidGlassMaterial material = new() { RefractionStrength = 12 };
    public LiquidGlassMaterial Material { get => material; set => material = value.Normalize(); }

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
        if (!clearActive) { enabled = false; Stop(); }
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
        lensShader?.Dispose(); lensShader = null;
        lensScene?.Dispose(); lensScene = null;
        lensBackdrop?.Dispose(); lensBackdrop = null;
        lensComposite?.Dispose(); lensComposite = null;
        lensBlur?.Dispose(); lensBlur = null;
        lensSaturation?.Dispose(); lensSaturation = null;
        lensExposure?.Dispose(); lensExposure = null;
        lensTint?.Dispose(); lensTint = null;
    }

    public void Update(bool active, double opacity, float dpiScale, Vector4 dockBounds, Vector4 waveShape)
    {
        if (disposed) return;
        dock = dockBounds; wave = waveShape; scale = dpiScale;
        clearActive = active;
        Output.Opacity = opacity;
        if (!active) { Output.Visibility = Visibility.Collapsed; setNativeSuppressed(false); }
        if (!active && paintDock is null)
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
            // A frame pool belongs to both a monitor and its physical dimensions.
            // DPI/window movement alone must not restart monitor capture.
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
                device = CanvasDevice.GetSharedDevice();
                shader = new PixelShaderEffect(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", "LiquidGlass.bin")));
                if (popupTarget is null)
                {
                    swapChain = new CanvasSwapChain(device, next.Width, next.Height, 96);
                    Panel.SwapChain = swapChain;
                }
                else
                {
                    popupCrop = new();
                    popupBlur = new() { Source = popupCrop, BorderMode = EffectBorderMode.Hard };
                    popupExposure = new() { Source = popupBlur, Exposure = UtilityMaterial.LiquidBackdropExposure };
                }
                capture = new(hwnd, device.As<IDirect3DDevice>(), next);
                capture.FrameAvailable += FrameAvailable;
                capture.Closed += CaptureClosed;
                SetStatus(CaptureBorderPermission.IsAllowed
                    ? "Liquid awaiting GPU capture · borderless permission granted"
                    : "Liquid awaiting GPU capture · Windows capture indicator required");
            }
            if (swapChain is not null && (swapChain.Size.Width != next.Width || swapChain.Size.Height != next.Height))
                swapChain.ResizeBuffers(next.Width, next.Height);
            // The swap chain is physical pixels at 96 DPI; XAML lays out in DIPs.
            Panel.Width = next.Width / scale; Panel.Height = next.Height / scale;
            LensPanel.Width = Panel.Width; LensPanel.Height = Panel.Height;
            QueueDraw();
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); }
    }
    private void FrameAvailable(object? sender, EventArgs args) => QueueDraw();
    private void CaptureClosed(object? sender, EventArgs args) => dispatcher.TryEnqueue(() =>
    {
        if (!disposed && ReferenceEquals(sender, capture)) Fail(new InvalidOperationException("Monitor capture closed."));
    });
    private void QueueDraw()
    {
        if (Interlocked.Exchange(ref queued, 1) != 0) return;
        if (!dispatcher.TryEnqueue(Draw)) Interlocked.Exchange(ref queued, 0);
    }
    private void Draw()
    {
        Interlocked.Exchange(ref queued, 0);
        if (disposed || !enabled || capture is null || shader is null || (swapChain is null && popupTarget is null)) return;
        try
        {
            var pending = capture.TakeFrame();
            if (pending is not null)
            {
                if (pending.ContentSize.Width != placement.MonitorWidth || pending.ContentSize.Height != placement.MonitorHeight)
                {
                    pending.Dispose();
                    if (DesktopCaptureSource.Locate(hwnd).SameCaptureSource(placement))
                        throw new InvalidOperationException("Capture frame dimensions do not match the monitor; retaining native fallback.");
                    // Display notifications and frames can race. Release the old
                    // pool and re-query physical bounds; never render stretched pixels.
                    Stop();
                    Update(clearActive, Output.Opacity, scale, dock, wave);
                    return;
                }
                // Hold at most one of the two pooled GPU frames. It can be drawn
                // again for hover motion when the desktop itself has not changed.
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
            shader.Source1 = bitmap;
            if (popupCrop is not null)
            {
                // Restrict blur evaluation to this popup plus its sampling/blur halo.
                var margin = 64 * scale;
                var x = Math.Max(0, placement.X - margin);
                var y = Math.Max(0, placement.Y - margin);
                popupCrop.SourceRectangle = new(x, y,
                    Math.Min(placement.MonitorWidth, placement.X + placement.Width + margin) - x,
                    Math.Min(placement.MonitorHeight, placement.Y + placement.Height + margin) - y);
                popupCrop.Source = bitmap;
                popupBlur!.BlurAmount = UtilityMaterial.LiquidBackdropBlur * scale;
                shader.Source1 = popupExposure;
            }
            var origin = new Vector2(placement.X, placement.Y);
            ConfigureShader(shader, Material, dock * scale + new Vector4(origin, 0, 0),
                wave * scale + new Vector4(origin.X, 0, 0, 0), new(placement.MonitorWidth, placement.MonitorHeight), Vector4.Zero);
            if (clearActive)
            {
                var crop = new global::Windows.Foundation.Rect(placement.X, placement.Y, placement.Width, placement.Height);
                if (popupTarget is not null) popupTarget.Draw(device!, shader, geometry, crop, scale, drawRim);
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
            shader.Source1 = null;
            SetStatus(clearActive ? "Liquid active · GPU desktop refraction"
                : "Liquid drag lens active · GPU dock refraction");
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); }
    }

    /// <summary>Stops the live desktop capture when the dock returns to its pill.</summary>
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
        effect.Properties["Dock"] = bounds; effect.Properties["Wave"] = shape;
        effect.Properties["Optics"] = new Vector4(m.RefractionStrength * scale, m.EdgeThickness * scale,
            m.RefractionFalloff, m.DiffusionAmount * scale);
        effect.Properties["Lighting"] = new Vector4(m.TintOpacity, m.SpecularIntensity, m.SpecularFalloff,
            m.SpecularAngleDegrees * MathF.PI / 180);
        lens.Y = m.ChromaticDispersion * scale;
        effect.Properties["FrameSize"] = frame; effect.Properties["Lens"] = lens;
    }

    private void DrawRim(CanvasDrawingSession drawing, CanvasGeometry geometry) =>
        rim.Draw(drawing, geometry, dock, scale, Material);

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
            lensExposure = (ExposureEffect)lensComposite.Sources[0];
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
        ReleaseLens();
        Output.Visibility = Visibility.Collapsed;
        popupTarget?.Dispose();
        setNativeSuppressed(false);
        if (capture is not null) { capture.FrameAvailable -= FrameAvailable; capture.Closed -= CaptureClosed; capture.Dispose(); capture = null; }
        shader?.Dispose(); shader = null;
        rim.Dispose();
        popupExposure?.Dispose(); popupExposure = null;
        popupBlur?.Dispose(); popupBlur = null;
        popupCrop?.Dispose(); popupCrop = null;
        bitmap?.Dispose(); bitmap = null;
        currentFrame?.Dispose(); currentFrame = null;
        Panel.SwapChain = null;
        swapChain?.Dispose(); swapChain = null;
        device = null; // Shared Win2D device is borrowed, never disposed here.
    }
    public void Dispose() { if (disposed) return; disposed = true; enabled = false; Stop(); }
}
