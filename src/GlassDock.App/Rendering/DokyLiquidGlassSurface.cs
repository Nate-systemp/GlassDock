using System.Numerics;
using GlassDock.Core.Materials;
using GlassDock.Windows.Desktop;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace GlassDock.App.Rendering;

/// <summary>One retained GPU shader and swap chain per Clear dock. No CPU pixel readback.</summary>
internal sealed class DokyLiquidGlassSurface : IDisposable
{
    public CanvasSwapChainPanel Panel { get; } = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
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
    public LiquidGlassMaterial Material { get; set; } = new();

    public DokyLiquidGlassSurface(nint window, DispatcherQueue queue, Func<CanvasGeometry?> geometry, Action<bool> suppressNative)
    { hwnd = window; dispatcher = queue; outline = geometry; setNativeSuppressed = suppressNative; }

    public void Update(bool active, double opacity, float dpiScale, Vector4 dockBounds, Vector4 waveShape)
    {
        if (disposed) return;
        dock = dockBounds; wave = waveShape; scale = dpiScale;
        Panel.Opacity = opacity;
        if (!active)
        {
            enabled = false; failed = false;
            Stop();
            return;
        }
        enabled = true;
        if (failed) return;
        try
        {
            var next = DesktopCaptureSource.Locate(hwnd);
            if (next.Width <= 0 || next.Height <= 0) return;
            if (capture is not null && next.Monitor != placement.Monitor) Stop();
            placement = next;
            if (capture is null)
            {
                device = CanvasDevice.GetSharedDevice();
                shader = new PixelShaderEffect(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", "LiquidGlass.bin")));
                swapChain = new CanvasSwapChain(device, next.Width, next.Height, 96);
                Panel.SwapChain = swapChain;
                capture = new(hwnd, device.As<IDirect3DDevice>(), next);
                capture.FrameAvailable += FrameAvailable;
                capture.Closed += CaptureClosed;
                SetStatus("Liquid awaiting GPU capture · Windows capture indicator enabled");
            }
            if (swapChain!.Size.Width != next.Width || swapChain.Size.Height != next.Height)
                swapChain.ResizeBuffers(next.Width, next.Height);
            // The swap chain is physical pixels at 96 DPI; XAML lays out in DIPs.
            Panel.Width = next.Width / scale; Panel.Height = next.Height / scale;
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
        if (disposed || !enabled || capture is null || shader is null || swapChain is null) return;
        try
        {
            var pending = capture.TakeFrame();
            if (pending is not null)
            {
                if (pending.ContentSize.Width != placement.MonitorWidth || pending.ContentSize.Height != placement.MonitorHeight)
                {
                    pending.Dispose();
                    throw new InvalidOperationException("Capture resolution differs from monitor coordinates; refusing a misaligned texture.");
                }
                // Hold at most one of the two pooled GPU frames. It can be drawn
                // again for hover motion when the desktop itself has not changed.
                shader.Source1 = null;
                bitmap?.Dispose();
                currentFrame?.Dispose(); currentFrame = pending;
                bitmap = CanvasBitmap.CreateFromDirect3D11Surface(device!, pending.Surface);
            }
            if (bitmap is null) return;
            var geometry = outline();
            if (geometry is null) return;
            var m = Material.Normalize();
            shader.Source1 = bitmap;
            var origin = new Vector2(placement.X, placement.Y);
            shader.Properties["Dock"] = dock * scale + new Vector4(origin, 0, 0);
            shader.Properties["Wave"] = wave * scale + new Vector4(origin.X, 0, 0, 0);
            shader.Properties["Optics"] = new Vector4(m.RefractionStrength * scale, m.EdgeThickness * scale, m.RefractionFalloff, m.DiffusionAmount * scale);
            shader.Properties["Lighting"] = new Vector4(m.TintOpacity, m.SpecularIntensity, m.SpecularFalloff, 0);
            shader.Properties["FrameSize"] = new Vector2(placement.MonitorWidth, placement.MonitorHeight);
            using (var drawing = swapChain.CreateDrawingSession(Microsoft.UI.Colors.Transparent))
            using (drawing.CreateLayer(1, geometry))
                drawing.DrawImage(shader, Vector2.Zero,
                    new global::Windows.Foundation.Rect(placement.X, placement.Y, placement.Width, placement.Height));
            swapChain.Present(0);
            shader.Source1 = null;
            Panel.Visibility = Visibility.Visible;
            setNativeSuppressed(true);
            SetStatus("Liquid active · GPU desktop refraction · Windows capture indicator enabled");
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Fail(error); }
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
        Panel.Visibility = Visibility.Collapsed;
        setNativeSuppressed(false);
        if (capture is not null) { capture.FrameAvailable -= FrameAvailable; capture.Closed -= CaptureClosed; capture.Dispose(); capture = null; }
        shader?.Dispose(); shader = null;
        bitmap?.Dispose(); bitmap = null;
        currentFrame?.Dispose(); currentFrame = null;
        Panel.SwapChain = null;
        swapChain?.Dispose(); swapChain = null;
        device = null; // Shared Win2D device is borrowed, never disposed here.
    }
    public void Dispose() { if (disposed) return; disposed = true; enabled = false; Stop(); }
}
