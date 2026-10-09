using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace GlassDock.Windows.Desktop;

/// <summary>GPU-only monitor capture. Owns the frame pool and reversible self-exclusion.</summary>
public sealed class DesktopCaptureSource : IDisposable
{
    public readonly record struct Placement(nint Monitor, int X, int Y, int Width, int Height,
        int MonitorWidth, int MonitorHeight)
    {
        public bool SameCaptureSource(Placement other) => Monitor == other.Monitor &&
            MonitorWidth == other.MonitorWidth && MonitorHeight == other.MonitorHeight;
    }
    private readonly nint window;
    private readonly uint previousAffinity;
    private GraphicsCaptureItem? item;
    private Direct3D11CaptureFramePool? pool;
    private GraphicsCaptureSession? session;
    private bool excluded;
    public event EventHandler? FrameAvailable;
    public event EventHandler? Closed;

    public static Placement Locate(nint hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(hwnd, out var rect)) throw new Win32Exception();
        return new(monitor, rect.Left - info.Monitor.Left, rect.Top - info.Monitor.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top,
            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
    }

    public DesktopCaptureSource(nint hwnd, IDirect3DDevice device, Placement placement)
    {
        window = hwnd;
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows GPU capture is unavailable.");
        if (!GetWindowDisplayAffinity(hwnd, out previousAffinity)) throw new Win32Exception();
        try
        {
            if (!SetWindowDisplayAffinity(hwnd, 0x11)) throw new Win32Exception("Doky could not exclude itself from capture.");
            excluded = true;
            item = CreateMonitor(placement.Monitor);
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            pool.FrameArrived += FrameArrived;
            item.Closed += ItemClosed;
            session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            CaptureBorderPermission.Configure(session);
            if (global::Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(
                "Windows.Graphics.Capture.GraphicsCaptureSession", "MinUpdateInterval"))
                session.MinUpdateInterval = TimeSpan.FromSeconds(1d / 60);
            session.StartCapture();
        }
        catch { Dispose(); throw; }
    }
    private void FrameArrived(Direct3D11CaptureFramePool sender, object args) => FrameAvailable?.Invoke(this, EventArgs.Empty);
    private void ItemClosed(GraphicsCaptureItem sender, object args) => Closed?.Invoke(this, EventArgs.Empty);
    public Direct3D11CaptureFrame? TakeFrame() => pool?.TryGetNextFrame();
    public void Dispose()
    {
        if (pool is not null) pool.FrameArrived -= FrameArrived;
        if (item is not null) item.Closed -= ItemClosed;
        session?.Dispose(); session = null;
        pool?.Dispose(); pool = null; item = null;
        if (excluded) { SetWindowDisplayAffinity(window, previousAffinity); excluded = false; }
    }

    private static GraphicsCaptureItem CreateMonitor(nint monitor)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var text));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(text, typeof(ICaptureInterop).GUID, out var factory));
            try
            {
                var pointer = factory.CreateForMonitor(monitor, new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760"));
                try { return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer); }
                finally { Marshal.Release(pointer); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        finally { WindowsDeleteString(text); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [ComImport, Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureInterop { nint CreateForWindow(nint hwnd, in Guid iid); nint CreateForMonitor(nint monitor, in Guid iid); }
    [DllImport("combase.dll")] private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string text, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, in Guid iid, out ICaptureInterop factory);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
}
