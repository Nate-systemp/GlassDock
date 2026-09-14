using System.Runtime.InteropServices;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public sealed class DesktopWindowFocus : IDisposable
{
    private readonly nint preview;
    private readonly NativeMethods.SubclassProc callback;
    private nint dim, mirror, outline;
    private bool mirrorShown;
    private ApplicationWindow? target;
    private WindowThumbnail? thumbnail;
    private long? dismissedWindow;
    private bool disposed;
    public event EventHandler? Dismissed;

    public DesktopWindowFocus(nint previewWindow)
    {
        preview = previewWindow;

        callback = (window, message, w, l, id, data) =>
        {
            if (message == 0x84) return -1;
            if (message == 0x21) return 3;
            if (window == mirror && message == 0x14) return 1;
            if (message == 0x0F) { if (window == mirror) { NativeMethods.ValidateRect(window, 0); return 0; } var dc = NativeMethods.GetDC(window); try { if (dc != 0 && NativeMethods.GetClientRect(window, out var rect)) NativeMethods.FillRect(dc, ref rect, NativeMethods.GetStockObject(window == outline ? 0 : 4)); } finally { if (dc != 0) NativeMethods.ReleaseDC(window, dc); } NativeMethods.ValidateRect(window, 0); return 0; }

            if (message == 0x113)
            {
                if ((NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0)
                {
                    var handle = target?.Handle;
                    Hide();
                    dismissedWindow = handle;
                    Dismissed?.Invoke(this, EventArgs.Empty);
                }
                else Update();

                return 0;
            }

            return NativeMethods.DefSubclassProc(window, message, w, l);
        };
    }

    public bool Show(ApplicationWindow window)
    {
        if (disposed) return false;
        if (dismissedWindow == window.Handle) return false;

        dismissedWindow = null;

        using var coordinates = new PhysicalCoordinates();

        if (!Eligible(window)) { Hide(); return false; }
        if (!EnsureWindows()) { Hide(); return false; }

        if (target is null || target.Handle != window.Handle || target.ProcessId != window.ProcessId || target.ProcessStartTicks != window.ProcessStartTicks)
        {
            var next = new WindowThumbnail(mirror, window);
            var previous = thumbnail;

            target = window;
            thumbnail = next;

            Update();

            if (target is not null)
                NativeMethods.DwmFlush();

            previous?.Dispose();
        }
        else Update();

        if (target is not null && NativeMethods.SetTimer(dim, 1, 100, 0) == 0)
            Hide();

        return target is not null;
    }

    private static bool Eligible(ApplicationWindow window) => WindowsApplicationService.IsEligible(window) && !NativeMethods.IsIconic((nint)window.Handle);

    private bool EnsureWindows()
    {
        if (dim == 0) dim = Create("GlassDock desktop dim", 153);
        if (mirror == 0) mirror = Create("GlassDock desktop mirror", 0);
        if (outline == 0) outline = Create("GlassDock desktop outline", 220);

        return dim != 0 && mirror != 0 && outline != 0;
    }

    private nint Create(string title, byte opacity)
    {
        var window = NativeMethods.CreateWindowExW(0x080800A8, "STATIC", title, 0x80000000, 0, 0, 1, 1, 0, 0, 0, 0);

        if (window == 0)
            return 0;

        if (!NativeMethods.SetWindowSubclass(window, callback, 1, 0) || !NativeMethods.SetLayeredWindowAttributes(window, 0, opacity, 2))
        {
            NativeMethods.DestroyWindow(window);
            return 0;
        }

        return window;
    }

    private void Update()
    {
        if (target is null)
            return;

        using var coordinates = new PhysicalCoordinates();

        if (!NativeMethods.IsWindow(preview) || !NativeMethods.IsWindowVisible(preview) || !Eligible(target))
        {
            Hide();
            return;
        }

        var window = (nint)target.Handle;

        if (!NativeMethods.GetWindowRect(window, out var bounds))
        {
            Hide();
            return;
        }

        var frame = bounds;

        if (NativeMethods.DwmGetFrameBounds(window, 9, out var extended, Marshal.SizeOf<NativeMethods.Rect>()) >= 0 &&
            extended.Right > extended.Left &&
            extended.Bottom > extended.Top)
            frame = extended;

        var width = frame.Right - frame.Left;
        var height = frame.Bottom - frame.Top;

        if (width <= 0 || height <= 0 || thumbnail is null ||
            !thumbnail.UpdateDesktop(new(frame.Left, frame.Top, width, height),
                new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)))
        {
            Hide();
            return;
        }

        var x = NativeMethods.GetSystemMetrics(76);
        var y = NativeMethods.GetSystemMetrics(77);
        var desktopWidth = NativeMethods.GetSystemMetrics(78);
        var desktopHeight = NativeMethods.GetSystemMetrics(79);

        if (desktopWidth <= 0 || desktopHeight <= 0 || !UpdateOutline(width, height) ||
            !NativeMethods.SetWindowPos(dim, preview, x, y, desktopWidth, desktopHeight, 0x10 | 0x40) ||
            !NativeMethods.SetWindowPos(mirror, preview, frame.Left, frame.Top, width, height, 0x10 | 0x40) ||
            !NativeMethods.SetWindowPos(outline, preview, frame.Left - 2, frame.Top - 2, width + 4, height + 4, 0x10 | 0x40))
        {
            Hide();
            return;
        }

        if (!mirrorShown)
        {
            NativeMethods.DwmFlush();

            if (!NativeMethods.SetLayeredWindowAttributes(mirror, 0, 255, 2))
            {
                Hide();
                return;
            }

            mirrorShown = true;
        }
    }

    private bool UpdateOutline(int width, int height)
    {
        const int thickness = 2;

        var outer = NativeMethods.CreateRectRgn(0, 0, width + 4, height + 4);
        var inner = NativeMethods.CreateRectRgn(thickness, thickness, width + 4 - thickness, height + 4 - thickness);

        if (outer == 0 || inner == 0)
        {
            if (outer != 0) NativeMethods.DeleteObject(outer);
            if (inner != 0) NativeMethods.DeleteObject(inner);
            return false;
        }

        try
        {
            if (NativeMethods.CombineRgn(outer, outer, inner, 4) == 0)
                return false;

            if (NativeMethods.SetWindowRgn(outline, outer, true) == 0)
                return false;

            outer = 0;
            return true;
        }
        finally
        {
            if (outer != 0)
                NativeMethods.DeleteObject(outer);

            NativeMethods.DeleteObject(inner);
        }
    }

    public void Hide()
    {
        dismissedWindow = null;
        target = null;

        thumbnail?.Dispose();
        thumbnail = null;

        if (dim != 0)
        {
            NativeMethods.KillTimer(dim, 1);
            NativeMethods.ShowWindow(dim, 0);
        }

        if (outline != 0)
            NativeMethods.ShowWindow(outline, 0);

        if (mirror != 0)
        {
            NativeMethods.SetLayeredWindowAttributes(mirror, 0, 0, 2);
            NativeMethods.ShowWindow(mirror, 0);
        }

        mirrorShown = false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Hide();
        disposed = true;

        foreach (var window in new[] { outline, mirror, dim })
        {
            if (window == 0)
                continue;

            NativeMethods.RemoveWindowSubclass(window, callback, 1);
            NativeMethods.DestroyWindow(window);
        }

        outline = mirror = dim = 0;

        GC.KeepAlive(callback);
    }

    private readonly struct PhysicalCoordinates : IDisposable
    {
        private readonly nint previous;

        public PhysicalCoordinates() => previous = NativeMethods.SetThreadDpiAwarenessContext(-4);

        public void Dispose()
        {
            if (previous != 0)
                NativeMethods.SetThreadDpiAwarenessContext(previous);
        }
    }
}