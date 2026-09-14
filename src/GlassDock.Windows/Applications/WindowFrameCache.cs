using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using GlassDock.Core.Applications;
using GlassDock.Windows.Interop;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace GlassDock.Windows.Applications;

/// <summary>Bounded, memory-only last visible frames. Never restores a capture source.</summary>
public sealed class WindowFrameCache : IDisposable
{
    internal sealed record Frame(int Width, int Height, byte[] Pixels, DateTimeOffset CapturedAt);
    private readonly PreviewDiagnostics diagnostics = new();
    public string DiagnosticsPath => diagnostics.Path;
    private readonly Dictionary<(long Handle, int Process, long Started), Entry> entries = [];
    private IDirect3DDevice? device;
    private bool disposed;
    // At most sixteen capture pools and 128 MiB of retained CPU pixels (including 4K sources).
    private const int MaxWindows = 16;
    private const int MaxPixels = 8 * 1024 * 1024;
    private const long PixelBudget = 32 * 1024 * 1024;

    public WindowFrameCache() => diagnostics.Write(null, "capture-policy", false, null, new
    {
        borderless = false,
        reason = "Unpackaged build: no graphicsCaptureWithoutBorder package capability; Windows capture indicator retained.",
        maxWindows = MaxWindows, retainedPixelBudget = PixelBudget
    });

    public void Report(ApplicationWindow window, string stage, object? detail = null)
    {
        entries.TryGetValue(Key(window), out var entry);
        diagnostics.Write(window, stage, entry is not null, entry?.Latest, detail);
    }

    public void Track(IEnumerable<ApplicationWindow> windows)
    {
        if (disposed) return;
        var current = windows.DistinctBy(Key).ToDictionary(Key);
        foreach (var key in entries.Keys.Where(key => !current.ContainsKey(key)).ToArray())
        { entries[key].Dispose(); entries.Remove(key); }
        foreach (var window in current.Values.OrderByDescending(w => w.IsActive))
        {
            if (entries.ContainsKey(Key(window)))
            { Report(window, "capture-state"); continue; }
            if (NativeMethods.IsIconic((nint)window.Handle) || !WindowsApplicationService.IsEligible(window))
            { Report(window, "capture-not-started", new { reason = "minimized-or-ineligible" }); continue; }
            // A newly active source must not be permanently starved by earlier idle entries.
            var requiredPixels = 1920L * 1080;
            if (NativeMethods.GetWindowRect((nint)window.Handle, out var rectangle))
                requiredPixels = Math.Max(requiredPixels, (long)(rectangle.Right - rectangle.Left) * (rectangle.Bottom - rectangle.Top));
            while (window.IsActive && requiredPixels <= MaxPixels &&
                (entries.Count >= MaxWindows || PixelBudget - entries.Values.Sum(e => e.ReservedPixels) < requiredPixels))
            {
                var victim = entries.FirstOrDefault(pair => !NativeMethods.IsIconic((nint)pair.Key.Handle));
                if (victim.Value is not null) { victim.Value.Dispose(); entries.Remove(victim.Key); }
                else break;
            }
            if (entries.Count >= MaxWindows)
            { Report(window, "capture-not-started", new { reason = "session-budget" }); continue; }
            try
            {
                if (!GraphicsCaptureSession.IsSupported()) return;
                device ??= CreateDevice();
                var entry = new Entry(device, window, PixelBudget - entries.Values.Sum(e => e.ReservedPixels), diagnostics);
                entries.Add(Key(window), entry);
            }
            catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException)
            { Report(window, "capture-start-failed", new { error = error.Message, hresult = error.HResult }); }
        }
    }

    internal Frame? Get(ApplicationWindow window) =>
        entries.TryGetValue(Key(window), out var entry) ? entry.Latest : null;

    private static (long, int, long) Key(ApplicationWindow w) => (w.Handle, w.ProcessId, w.ProcessStartTicks);

    private sealed class Entry : IDisposable
    {
        private readonly ApplicationWindow window;
        private readonly Direct3D11CaptureFramePool pool;
        private readonly GraphicsCaptureSession session;
        private readonly IDirect3DDevice device;
        private readonly object gate = new();
        private readonly PreviewDiagnostics diagnostics;
        private bool disposed, copying;
        private long lastCopy;
        private Frame? latest;
        public Frame? Latest => Volatile.Read(ref latest);
        public long ReservedPixels { get; }

        public Entry(IDirect3DDevice device, ApplicationWindow window, long remainingPixels, PreviewDiagnostics diagnostics)
        {
            this.window = window; this.device = device; this.diagnostics = diagnostics;
            var item = CreateItem((nint)window.Handle);
            ReservedPixels = Math.Min(remainingPixels, Math.Min(MaxPixels,
                Math.Max(1920 * 1080, (long)item.Size.Width * item.Size.Height)));
            if (item.Size.Width <= 0 || item.Size.Height <= 0 || (long)item.Size.Width * item.Size.Height > ReservedPixels)
                throw new InvalidOperationException("Window exceeds capture pixel budget.");
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            GraphicsCaptureSession? created = null;
            try
            {
                session = created = pool.CreateCaptureSession(item);
                session.IsCursorCaptureEnabled = false;
                pool.FrameArrived += OnFrame;
                session.StartCapture();
                diagnostics.Write(window, "capture-started", true, null, new { reservedPixels = ReservedPixels });
            }
            catch { created?.Dispose(); pool.Dispose(); throw; }
        }

        private async void OnFrame(Direct3D11CaptureFramePool sender, object args)
        {
            Direct3D11CaptureFrame? frame = null;
            lock (gate)
            {
                if (disposed || copying) return;
                try { frame = sender.TryGetNextFrame(); }
                catch (COMException) { return; }
                if (frame is null) return;
                if (NativeMethods.IsIconic((nint)window.Handle) || Environment.TickCount64 - lastCopy < 1000)
                { frame.Dispose(); return; }
                copying = true;
            }
            try
            {
                var size = frame.ContentSize;
                if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > ReservedPixels)
                {
                    diagnostics.Write(window, "frame-rejected", true, Latest, new { reason = "size-budget", size.Width, size.Height });
                    return;
                }
                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
                // A resized pool can contain padded/old pixels. Recreate it and await a complete frame.
                if (bitmap.PixelWidth != size.Width || bitmap.PixelHeight != size.Height)
                {
                    lock (gate) if (!disposed) pool.Recreate(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                    return;
                }
                var pixels = new byte[checked(size.Width * size.Height * 4)];
                bitmap.CopyToBuffer(pixels.AsBuffer());
                var hasColor = false;
                for (var i = 0; i < pixels.Length; i += 4)
                    if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0) { hasColor = true; break; }
                if (!hasColor)
                {
                    // Conservative empty-surface guard: a genuinely all-black window also retains its previous frame.
                    diagnostics.Write(window, "frame-rejected", true, Latest, new { reason = "all-black-surface" });
                    return;
                }
                lock (gate)
                {
                    // Never overwrite the last visible frame with minimized/closed-source output.
                    if (!disposed && !NativeMethods.IsIconic((nint)window.Handle) && WindowsApplicationService.IsEligible(window))
                    {
                        Volatile.Write(ref latest, new(size.Width, size.Height, pixels, DateTimeOffset.UtcNow));
                        lastCopy = Environment.TickCount64;
                        diagnostics.Write(window, "frame-retained", true, latest);
                    }
                }
            }
            catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException)
            { diagnostics.Write(window, "frame-failed", true, Latest, new { error = error.Message, hresult = error.HResult }); }
            finally { frame.Dispose(); lock (gate) copying = false; }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                Volatile.Write(ref latest, null);
            }
            // Do not hold the callback gate while closing the native producer.
            pool.FrameArrived -= OnFrame;
            session.Dispose(); pool.Dispose();
            diagnostics.Write(window, "capture-stopped", false, null);
        }
    }

    private static IDirect3DDevice CreateDevice()
    {
        nint native = 0, context = 0, dxgi = 0, inspectable = 0;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out native, out _, out context));
            var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally { foreach (var pointer in new[] { inspectable, dxgi, context, native }) if (pointer != 0) Marshal.Release(pointer); }
    }

    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software,
        uint flags, nint levels, uint levelCount, uint sdk, out nint device, out uint level, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);

    [ComImport, Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }
    [DllImport("combase.dll")] private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string text, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, in Guid iid, out ICaptureInterop factory);

    private static GraphicsCaptureItem CreateItem(nint window)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var text));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(text, typeof(ICaptureInterop).GUID, out var factory));
            try
            {
                var pointer = factory.CreateForWindow(window, new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760"));
                try { return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer); }
                finally { Marshal.Release(pointer); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        finally { WindowsDeleteString(text); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var entry in entries.Values) entry.Dispose();
        entries.Clear(); device?.Dispose(); device = null;
    }
}
