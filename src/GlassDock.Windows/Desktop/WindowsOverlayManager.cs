using System.ComponentModel;
using System.Runtime.InteropServices;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
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
    private const nuint TopmostTimer = 0x4745;
    private bool expandedInput;
    private bool transparentInput;
    private NativeMethods.Point? previousPointer;
    private nint currentMonitor;
    private NativeMethods.Rect currentMonitorBounds;
    private double currentMonitorScale;
    private bool hasCurrentMonitorMetrics;
    public event EventHandler? PointerMovedOutsideInput;
    public event EventHandler? PointerMovedInsideInput;
    public WindowsOverlayManager(nint hwnd) { this.hwnd = hwnd; callback = WindowMessage; }
    // Maximum invisible host width. The actual host is clamped to the current
    // monitor so the visible dock can grow with pinned applications without
    // ever extending beyond the display.
    public const double Width = 1600;
    public const double Height = 144;
    public double CurrentHostWidthDips { get; private set; } = 960;
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
        NativeMethods.SetTimer(hwnd, TopmostTimer, 250, 0);
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
        if (message == 0x0113 && wParam == TopmostTimer)
        {
            // Foreground notifications can precede an app's final z-order update.
            // Reassert without activation, but leave our focused utility popup above us.
            var foreground = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundProcess);
            NativeMethods.GetWindowThreadProcessId(hwnd, out var dockProcess);
            if (foreground != 0 && foregroundProcess != dockProcess) EnsureTopmost();
            return 0;
        }
        if (message == 0x0084 && inputRegion != 0) // WM_NCHITTEST, signed virtual-desktop coordinates
        {
            var point = new NativeMethods.Point { X = (short)(long)lParam, Y = (short)((long)lParam >> 16) };
            var inside = ContainsScreenPoint(point);
            // Hit-test callers can probe points other than the physical cursor.
            // Do not let such a probe disable the entire HWND for OLE drops.
            // The existing cursor sampler owns cross-process click-through.
            return inside ? 1 : -1; // HTCLIENT / HTTRANSPARENT; layered style passes to other processes.
        }
        if (message == 0x0113 && wParam == InputTimer) { UpdateInputTransparency(); return 0; }
        if (message == 0x0014 && ClearBackground((nint)wParam)) return 1;

        // WM_DISPLAYCHANGE / WM_SETTINGCHANGE / WM_DPICHANGED.
        // Keep our own placement/input geometry in sync when resolution,
        // monitor layout, or per-monitor scaling changes without changing HMONITOR.
        if (message is 0x007E or 0x001A or 0x02E0)
        {
            hasCurrentMonitorMetrics = false;
            lastInteractionPolygon = null;
            previousPointer = null;
        }

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
        NativeMethods.KillTimer(hwnd, TopmostTimer);
        if (inputRegion != 0) NativeMethods.DeleteObject(inputRegion);
        inputRegion = 0;
        if (foregroundHook != 0) NativeMethods.UnhookWinEvent(foregroundHook);
        foregroundHook = 0;
        NativeMethods.RemoveWindowSubclass(hwnd, callback, 2);
        GC.KeepAlive(foregroundCallback);
    }

    public PixelRect Position(double margin, DockDisplayMode displayMode = DockDisplayMode.Primary)
    {
        var monitor = ResolveMonitor(displayMode);
        if (monitor == 0)
            monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);

        var monitorChanged = currentMonitor != 0 && currentMonitor != monitor;

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
            info.Monitor.Bottom - info.Monitor.Top);

        var scale = GetMonitorScale(monitor);

        // Size the transparent overlay host to the current monitor rather than
        // keeping the historical 960-DIP cap. The host itself is invisible;
        // only the glass surface grows to its content. Leave a small physical
        // edge margin so large docks never touch or cross the display edges.
        var availableWidthDips = Math.Max(320d, screen.Width / scale - 32d);
        var hostWidthDips = Math.Min(Width, availableWidthDips);
        CurrentHostWidthDips = hostWidthDips;

        var rect = DesktopPlacement.BottomCenter(
            screen,
            hostWidthDips,
            Height,
            margin,
            scale);

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
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        currentMonitor = monitor;
        currentMonitorBounds = info.Monitor;
        currentMonitorScale = scale;
        hasCurrentMonitorMetrics = true;

        if (monitorChanged)
        {
            // The interaction polygon is stored in physical pixels. Force it to be
            // rebuilt after a cross-monitor move so different DPI/scaling values do
            // not leave the dock with a stale hit-test region.
            lastInteractionPolygon = null;
            previousPointer = null;
        }

        EnsureTopmost();
        return rect;
    }

    public bool RepositionIfMonitorChanged(double margin, DockDisplayMode displayMode)
    {
        var target = ResolveMonitor(displayMode);
        if (target == 0)
            return false;

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(target, ref info))
            return false;

        var targetScale = GetMonitorScale(target);
        var bounds = info.Monitor;

        var metricsChanged =
            !hasCurrentMonitorMetrics ||
            target != currentMonitor ||
            Math.Abs(targetScale - currentMonitorScale) > 0.001 ||
            bounds.Left != currentMonitorBounds.Left ||
            bounds.Top != currentMonitorBounds.Top ||
            bounds.Right != currentMonitorBounds.Right ||
            bounds.Bottom != currentMonitorBounds.Bottom;

        if (!metricsChanged)
            return false;

        Position(margin, displayMode);
        return true;
    }

    private nint ResolveMonitor(DockDisplayMode displayMode)
    {
        const uint DefaultToNearest = 2;

        if (displayMode == DockDisplayMode.Pointer &&
            NativeMethods.GetCursorPos(out var pointer))
        {
            return NativeMethods.MonitorFromPoint(pointer, DefaultToNearest);
        }

        if (displayMode == DockDisplayMode.Foreground)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != 0 && foreground != hwnd)
            {
                var monitor = NativeMethods.MonitorFromWindow(foreground, DefaultToNearest);
                if (monitor != 0)
                    return monitor;
            }

            if (NativeMethods.GetCursorPos(out var fallbackPointer))
                return NativeMethods.MonitorFromPoint(fallbackPointer, DefaultToNearest);
        }

        return NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);
    }

    private double GetMonitorScale(nint monitor)
    {
        try
        {
            if (monitor != 0 &&
                NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) >= 0 &&
                dpiX > 0)
            {
                return dpiX / 96d;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96d : 1;
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
        var hostWidth = NativeMethods.GetClientRect(hwnd, out var client) ? (client.Right - client.Left) / scale : Width;
        var x = expanded ? 0 : (hostWidth - 200) / 2;
        var y = expanded ? 0 : Math.Max(0, Height - bottomMargin - 28);
        var width = expanded ? hostWidth : 200;
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
    /// Samples the current cursor against the native interaction shape. Menu
    /// modality can suppress the normal XAML PointerExited event, so callers
    /// use this when a context menu closes.
    /// </summary>
    public bool IsPointerInsideInput()
    {
        if (!NativeMethods.GetCursorPos(out var point) || !NativeMethods.GetWindowRect(hwnd, out var bounds))
            return false;

        if (inputRegion != 0)
            return ContainsScreenPoint(point);

        var region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == 0)
            return false;
        try
        {
            var hasRegion = NativeMethods.GetWindowRgn(hwnd, region) > 0;
            return hasRegion && PtInRegion(region, point.X - bounds.Left, point.Y - bounds.Top);
        }
        finally
        {
            NativeMethods.DeleteObject(region);
        }
    }

    /// <summary>Input follows the visible pill; eight DIP extend only below it.</summary>
    public void SetPeekInteraction(double bottom)
    {
        var hostWidth = NativeMethods.GetClientRect(hwnd, out var client) ? (client.Right - client.Left) / Scale : Width;
        var left = (hostWidth - 120) / 2;
        var top = Height - bottom - 5;
        SetInteractionPolygon([(left, top), (left + 120, top),
            (left + 120, top + 13), (left, top + 13)]);
    }

    public bool TryGetPointerPosition(out double x, out double y)
    {
        x = y = 0;
        if (!NativeMethods.GetCursorPos(out var point) || !NativeMethods.GetWindowRect(hwnd, out var bounds))
            return false;
        x = (point.X - bounds.Left) / Scale;
        y = (point.Y - bounds.Top) / Scale;
        return true;
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
            if (NativeMethods.GetCursorPos(out var current)) previousPointer = current;
            NativeMethods.SetWindowRgn(hwnd, 0, true);
            expandedInput = true;
            // Reuse the existing 20 Hz input sampler in peek too; a transparent host must reacquire input.
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
        point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom &&
        PtInRegion(inputRegion, point.X - bounds.Left, point.Y - bounds.Top);

    private void UpdateInputTransparency()
    {
        if (inputRegion == 0 || !NativeMethods.GetCursorPos(out var point)) return;
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
}
