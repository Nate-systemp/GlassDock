using System.ComponentModel;
using System.Runtime.InteropServices;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public sealed class WindowsOverlayManager : IDisposable
{
    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;
    private NativeMethods.WinEventProc? foregroundCallback;
    private nint foregroundHook;
    private NativeMethods.Point[]? lastInteractionPolygon;
    private nint inputRegion;
    private const nuint InputTimer = 0x4744;
    private bool expandedInput;
    private bool transparentInput;
    private NativeMethods.Point? previousPointer;
    public event EventHandler? PointerMovedOutsideInput;
    public event EventHandler? PointerMovedInsideInput;
    public WindowsOverlayManager(nint hwnd) { this.hwnd = hwnd; callback = WindowMessage; }
    public const double Width = 640;
    public const double Height = 144;
    public double Scale => NativeMethods.GetDpiForWindow(hwnd) is var dpi && dpi > 0 ? dpi / 96d : 1;

    public void Configure(bool inspection = false)
    {
        // Only GlassDock's own HWND: tool window, no activation, no caption/resizing frame.
        var ex = NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
        var extendedStyle = inspection ? ((ex | 0x08040000L) & ~0x80L) : ((ex | 0x08000080L) & ~0x00040000L);
        NativeMethods.SetWindowLongPtr(hwnd, -20, (nint)(extendedStyle | 0x00080000L)); // WS_EX_LAYERED
        if (!NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, 2)) throw new Win32Exception();
        var style = NativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, -16, (nint)(style & ~0x00CF0000L));
        var noCorner = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref noCorner, sizeof(int));
        ConfigureTransparency();
        if (!NativeMethods.SetWindowSubclass(hwnd, callback, 2, 0))
            throw new InvalidOperationException("Cannot attach overlay transparency handling.");
        var dc = NativeMethods.GetDC(hwnd);
        try { ClearBackground(dc); } finally { NativeMethods.ReleaseDC(hwnd, dc); }
        // Out-of-context notification only: no input interception or code in other processes.
        foregroundCallback = (_, _, _, _, _, _, _) => EnsureTopmost();
        foregroundHook = NativeMethods.SetWinEventHook(3, 3, 0, foregroundCallback, 0, 0, 0);
        if (foregroundHook == 0) throw new InvalidOperationException("Cannot monitor foreground changes for the desktop overlay.");
        EnsureTopmost();
    }

    private void ConfigureTransparency()
    {
        var margins = new NativeMethods.Margins();
        Marshal.ThrowExceptionForHR(NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins));
        var emptyRegion = NativeMethods.CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new NativeMethods.BlurBehind { Flags = 3, Enable = 1, Region = emptyRegion };
            Marshal.ThrowExceptionForHR(NativeMethods.DwmEnableBlurBehindWindow(hwnd, ref blur));
        }
        finally { NativeMethods.DeleteObject(emptyRegion); }
    }

    private bool ClearBackground(nint dc)
    {
        if (dc == 0 || !NativeMethods.GetClientRect(hwnd, out var rect)) return false;
        return NativeMethods.FillRect(dc, ref rect, NativeMethods.GetStockObject(4)) != 0;
    }

    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0084 && expandedInput) // WM_NCHITTEST, signed virtual-desktop coordinates
        {
            var point = new NativeMethods.Point { X = (short)(long)lParam, Y = (short)((long)lParam >> 16) };
            var inside = ContainsScreenPoint(point);
            SetInputTransparent(!inside);
            return inside ? 1 : -1; // HTCLIENT / HTTRANSPARENT; layered style passes to other processes.
        }
        if (message == 0x0113 && wParam == InputTimer) { UpdateInputTransparency(); return 0; }
        if (message == 0x0014 && ClearBackground((nint)wParam)) return 1;
        if (message == 0x031E) ConfigureTransparency();
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

   private void EnsureTopmost()
    {
        if (!NativeMethods.IsWindow(hwnd))
            return;

        NativeMethods.SetWindowPos(
            hwnd,
            -1, // HWND_TOPMOST
            0,
            0,
            0,
            0,
            0x0001 | // SWP_NOSIZE
            0x0002 | // SWP_NOMOVE
            0x0010   // SWP_NOACTIVATE
        );
    }
    public void Dispose()
    {
        NativeMethods.KillTimer(hwnd, InputTimer);
        if (inputRegion != 0) NativeMethods.DeleteObject(inputRegion);
        inputRegion = 0;
        if (foregroundHook != 0) NativeMethods.UnhookWinEvent(foregroundHook);
        foregroundHook = 0;
        NativeMethods.RemoveWindowSubclass(hwnd, callback, 2);
        GC.KeepAlive(foregroundCallback);
    }

    public PixelRect Position(double margin)
    {
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.Point(),
            1
        );

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            throw new Win32Exception();

        var screen = new PixelRect(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top
        );

        var rect = DesktopPlacement.BottomCenter(
            screen,
            Width,
            Height,
            margin,
            Scale
        );

        if (!NativeMethods.SetWindowPos(
            hwnd,
            -1, // HWND_TOPMOST
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            0x0010 | // SWP_NOACTIVATE
            0x0020 | // SWP_FRAMECHANGED
            0x0040   // SWP_SHOWWINDOW
        ))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error()
            );
        }

        EnsureTopmost();

        return rect;
    }

    public void SetInteractionRegion(bool expanded, double bottomMargin = 24)
    {
        expandedInput = false;
        NativeMethods.KillTimer(hwnd, InputTimer);
        SetInputTransparent(false);
        if (inputRegion != 0) NativeMethods.DeleteObject(inputRegion);
        inputRegion = 0;
        lastInteractionPolygon = null;
        var scale = Scale;
        // The idle hit target surrounds the pill and reaches through its lower margin.
        var x = expanded ? 0 : 220;
        var y = expanded ? 0 : Math.Max(0, Height - bottomMargin - 28);
        var width = expanded ? Width : 200;
        var height = Height - y;
        var region = NativeMethods.CreateRoundRectRgn((int)(x * scale), (int)(y * scale),
            (int)((x + width) * scale) + 1, (int)((y + height) * scale) + 1, (int)(16 * scale), (int)(16 * scale));
        if (region == 0) throw new Win32Exception();
        if (NativeMethods.SetWindowRgn(hwnd, region, true) == 0)
        {
            NativeMethods.DeleteObject(region);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        // Windows owns the region after a successful SetWindowRgn.
    }

    /// <summary>
    /// Updates the small peek target. During the rise/fall animation an input
    /// corridor keeps the pointer latched to the dock instead of chasing the
    /// moving five-pixel pill and repeatedly entering/leaving it.
    /// </summary>
    public void SetPeekInteraction(double bottom, bool watchPointer, double? transitionBottom = null)
    {
        var left = (Width - 120) / 2;
        var top = Height - bottom - 5;
        if (watchPointer)
        {
            if (transitionBottom is { } targetBottom)
            {
                var targetTop = Height - targetBottom - 5;
                var corridorTop = Math.Min(top, targetTop) - 8;
                var corridorBottom = Math.Max(top + 6, targetTop + 6) + 8;
                SetInteractionPolygon([(left - 8, corridorTop), (left + 128, corridorTop),
                    (left + 128, corridorBottom), (left - 8, corridorBottom)]);
                return;
            }
            SetInteractionPolygon([(left - 1, top - 1), (left + 121, top - 1),
                (left + 121, top + 6), (left - 1, top + 6)]);
            return;
        }
        expandedInput = false;
        NativeMethods.KillTimer(hwnd, InputTimer);
        SetInputTransparent(false);
        previousPointer = null;
        if (inputRegion != 0) NativeMethods.DeleteObject(inputRegion);
        inputRegion = 0;
        lastInteractionPolygon = null;
        var scale = Scale;
        var region = NativeMethods.CreateRoundRectRgn((int)Math.Floor((left - 1) * scale),
            (int)Math.Floor((top - 1) * scale), (int)Math.Ceiling((left + 121) * scale),
            (int)Math.Ceiling((top + 6) * scale), (int)(5 * scale), (int)(5 * scale));
        if (region == 0) throw new Win32Exception();
        if (NativeMethods.SetWindowRgn(hwnd, region, true) == 0)
        {
            NativeMethods.DeleteObject(region);
            throw new Win32Exception();
        }
    }

    /// <summary>Keep an input-only region; never clip expanded glass/shadow rendering.</summary>
    public void SetInteractionPolygon(IReadOnlyList<(double X, double Y)> outline)
    {
        if (outline.Count < 3) return;
        var scale = Scale;
        var points = outline.Select(p => new NativeMethods.Point
        {
            X = (int)Math.Round(p.X * scale), Y = (int)Math.Round(p.Y * scale)
        }).ToArray();
        if (lastInteractionPolygon is { } previous && points.SequenceEqual(previous)) return;
        var region = NativeMethods.CreatePolygonRgn(points, points.Length, 2 /* WINDING */);
        if (region == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!expandedInput)
        {
            if (GetCursorPos(out var current)) previousPointer = current;
            NativeMethods.SetWindowRgn(hwnd, 0, true);
            expandedInput = true;
            // Only runs while expanded/transitioning. Idle uses its existing small native region.
            // Needed to reacquire hover after a layered window passes input to another process.
            NativeMethods.SetTimer(hwnd, InputTimer, 50, 0);
        }
        if (inputRegion != 0) NativeMethods.DeleteObject(inputRegion);
        inputRegion = region;
        lastInteractionPolygon = points;
        UpdateInputTransparency();
    }

    private bool ContainsScreenPoint(NativeMethods.Point point) =>
        inputRegion != 0 && NativeMethods.GetWindowRect(hwnd, out var bounds) &&
        PtInRegion(inputRegion, point.X - bounds.Left, point.Y - bounds.Top);

    private void UpdateInputTransparency()
    {
        if (!expandedInput || !GetCursorPos(out var point)) return;
        var inside = ContainsScreenPoint(point);
        SetInputTransparent(!inside);
        var moved = previousPointer is { } previous && (previous.X != point.X || previous.Y != point.Y);
        previousPointer = point;
        // Geometry movement alone must not cause a raised pill to oscillate.
        if (moved)
        {
            if (inside) PointerMovedInsideInput?.Invoke(this, EventArgs.Empty);
            else PointerMovedOutsideInput?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetInputTransparent(bool transparent)
    {
        if (transparentInput == transparent) return;
        var style = NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, -20, (nint)(transparent ? style | 0x20 : style & ~0x20L));
        transparentInput = transparent;
    }

    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativeMethods.Point point);
}
