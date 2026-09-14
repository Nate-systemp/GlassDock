using System.Runtime.InteropServices;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

/// <summary>Owns a hollow, click-through highlight. Never changes the target window.</summary>
public sealed class DesktopWindowHighlight : IDisposable
{
    private const nuint TrackingTimer = 1;
    private readonly NativeMethods.SubclassProc callback;
    private nint overlay, target;
    private uint targetProcess;
    private NativeMethods.Rect lastBounds;
    private uint lastDpi;
    private bool disposed;

    public DesktopWindowHighlight()
    {
        callback = (window, message, wParam, lParam, id, data) =>
        {
            if (message == 0x0084) return -1; // HTTRANSPARENT; layered style also passes input across processes.
            if (message == 0x0021) return 3; // MA_NOACTIVATE
            if (message == 0x0113 && wParam == TrackingTimer) { Update(); return 0; }
            return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
        };
    }

    public void Show(nint window)
    {
        if (disposed) return;
        if (window == 0 || !NativeMethods.IsWindow(window)) { Hide(); return; }
        if (overlay == 0 && !CreateOverlay()) return;
        if (target != window)
        {
            target = window;
            NativeMethods.GetWindowThreadProcessId(window, out targetProcess);
            lastBounds = default;
        }
        Update();
        if (target != 0) NativeMethods.SetTimer(overlay, TrackingTimer, 100, 0);
    }

    private bool CreateOverlay()
    {
        using var dpiContext = new PhysicalCoordinates();
        // WS_EX_NOACTIVATE | TOOLWINDOW | LAYERED | TRANSPARENT; WS_POPUP | SS_WHITERECT.
        // STATIC paints the white ring; the center is physically absent from the window region.
        overlay = NativeMethods.CreateWindowExW(0x080800A0, "STATIC", "GlassDock window highlight",
            0x80000006, 0, 0, 1, 1, 0, 0, 0, 0);
        if (overlay == 0) return false;
        if (!NativeMethods.SetWindowSubclass(overlay, callback, 1, 0) ||
            !NativeMethods.SetLayeredWindowAttributes(overlay, 0, 165, 2))
        {
            NativeMethods.DestroyWindow(overlay); overlay = 0;
            return false;
        }
        return true;
    }

    private void Update()
    {
        if (target == 0) return;
        using var dpiContext = new PhysicalCoordinates();
        NativeMethods.GetWindowThreadProcessId(target, out var process);
        if (process != targetProcess || !NativeMethods.IsWindow(target) ||
            !NativeMethods.IsWindowVisible(target) || NativeMethods.IsIconic(target) ||
            (NativeMethods.DwmGetWindowAttribute(target, 14, out var cloaked, sizeof(int)) >= 0 && cloaked != 0))
        { Hide(); return; }

        // Extended frame bounds omit invisible resize margins. Both rectangles use desktop coordinates.
        var result = NativeMethods.DwmGetFrameBounds(target, 9, out var bounds, Marshal.SizeOf<NativeMethods.Rect>());
        if ((result < 0 || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) &&
            !NativeMethods.GetWindowRect(target, out bounds)) { Hide(); return; }
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < 8 || height < 8 || width > 32768 || height > 32768) { Hide(); return; }
        var dpi = NativeMethods.GetDpiForWindow(target);
        if (dpi == 0) dpi = 96;
        if (bounds.Equals(lastBounds) && dpi == lastDpi) return;

        if (width != lastBounds.Right - lastBounds.Left || height != lastBounds.Bottom - lastBounds.Top || dpi != lastDpi)
        {
            var thickness = Math.Max(2, (int)Math.Round(2.5 * dpi / 96));
            var radius = Math.Max(4, (int)Math.Round(8d * dpi / 96));
            var outer = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
            var inner = NativeMethods.CreateRoundRectRgn(thickness, thickness, width - thickness + 1,
                height - thickness + 1, Math.Max(2, (radius - thickness) * 2), Math.Max(2, (radius - thickness) * 2));
            try
            {
                if (outer == 0 || inner == 0 || NativeMethods.CombineRgn(outer, outer, inner, 4) == 0 ||
                    NativeMethods.SetWindowRgn(overlay, outer, true) == 0) { Hide(); return; }
                outer = 0; // Ownership transferred to Windows.
            }
            finally
            {
                if (outer != 0) NativeMethods.DeleteObject(outer);
                if (inner != 0) NativeMethods.DeleteObject(inner);
            }
        }
        // This is the ONLY HWND being positioned or raised. Never pass target to SetWindowPos.
        if (!NativeMethods.SetWindowPos(overlay, -1, bounds.Left, bounds.Top, width, height, 0x0010 | 0x0040))
        { Hide(); return; }
        lastBounds = bounds; lastDpi = dpi;
    }

    public void Hide()
    {
        target = 0; targetProcess = 0; lastBounds = default;
        if (overlay == 0) return;
        NativeMethods.KillTimer(overlay, TrackingTimer);
        NativeMethods.ShowWindow(overlay, 0);
    }

    public void Dispose()
    {
        if (disposed) return;
        Hide(); disposed = true;
        if (overlay == 0) return;
        NativeMethods.RemoveWindowSubclass(overlay, callback, 1);
        NativeMethods.DestroyWindow(overlay); overlay = 0;
        GC.KeepAlive(callback);
    }

    // DWM frame bounds are physical pixels; keep the fallback and overlay in that same space.
    // This changes only the calling thread temporarily, not process or Windows configuration.
    private readonly struct PhysicalCoordinates : IDisposable
    {
        private readonly nint previous;
        public PhysicalCoordinates() => previous = NativeMethods.SetThreadDpiAwarenessContext(-4);
        public void Dispose()
        {
            if (previous != 0) NativeMethods.SetThreadDpiAwarenessContext(previous);
        }
    }
}
