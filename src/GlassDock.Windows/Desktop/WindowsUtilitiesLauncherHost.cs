using System.Runtime.InteropServices;
using GlassDock.Windows.Interop;
using GlassDock.Core.Desktop;

namespace GlassDock.Windows.Desktop;

public sealed class WindowsUtilitiesLauncherHost : IDisposable
{
    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;
    private int shortcut;
    // The shell reserves a full-width TOP strip; the actual WinUI pill stays
    // small, transparent around its content, and anchored within the strip.
    private const uint AppBarCallbackMessage = 0x8045; // Different from Doky's bottom dock (0x8044).
    private const uint AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3;
    private const uint AbnPosChanged = 1, AbeTop = 1;
    private bool topBarRegistered, applyingTopBar, topBarDirty = true;
    private int topBarHeight;
    private nint topBarMonitor;
    private NativeMethods.Rect topBarMonitorBounds;
    private NativeMethods.Rect topBarBounds;
    private bool disposed;
    public event Action? Shortcut;
    public WindowsUtilitiesLauncherHost(nint hwnd)
    {
        this.hwnd = hwnd;
        callback = Message;
        if (!NativeMethods.SetWindowSubclass(hwnd, callback, 73, 0)) throw new InvalidOperationException("Cannot attach Utilities launcher.");
    }
    public bool ConfigureShortcut(bool enabled, int key)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var desired = enabled ? key : 0;
        if (shortcut == desired) return true;
        NativeMethods.UnregisterHotKey(hwnd, 0x4773);
        shortcut = 0;
        if (!enabled) return true;
        if (!NativeMethods.RegisterHotKey(hwnd, 0x4773, 0x4003, (uint)key)) return false;
        shortcut = key;
        return true;
    }
    private nint Message(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0312 && wParam == 0x4773) { Shortcut?.Invoke(); return 0; }
        if (message == AppBarCallbackMessage)
        {
            if ((uint)wParam == AbnPosChanged)
            {
                topBarDirty = true;
                if (!applyingTopBar && topBarRegistered) RefreshTopBar();
            }
            return 0;
        }
        // Display topology and DPI changes can move the reservation even if
        // the HWND has not yet been re-positioned. AppBar callbacks handle
        // work-area changes; avoid WM_SETTINGCHANGE feedback from ABM_SETPOS.
        if (message is 0x007E or 0x02E0)
            topBarDirty = true;
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }
    /// <summary>
    /// AppBar shell reservation for the monitor containing the visible Utilities
    /// pill. This does not resize the pill HWND into a full-width click blocker.
    /// The existing dock AppBar can coexist at the bottom of the same monitor.
    /// </summary>
    public bool ReserveTopBar(int heightPixels)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        heightPixels = Math.Max(1, heightPixels);
        var monitor = NativeMethods.MonitorFromWindow(hwnd, 2);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            ClearTopBar();
            return false;
        }

        // Relocation is handled as remove/re-register: each shell registration
        // belongs to exactly one monitor and must not leave old work-area insets.
        if (topBarRegistered && monitor != topBarMonitor)
            ClearTopBar();

        if (!topBarRegistered)
        {
            var registration = AppBarData();
            if (NativeMethods.SHAppBarMessage(AbmNew, ref registration) == 0)
                return false;
            topBarRegistered = true;
            topBarDirty = true;
        }

        if (topBarHeight != heightPixels || !RectEquals(topBarMonitorBounds, info.Monitor))
            topBarDirty = true;
        topBarMonitor = monitor;
        topBarMonitorBounds = info.Monitor;
        topBarHeight = heightPixels;
        return !topBarDirty || RefreshTopBar();
    }

    public bool TopBarReserved => topBarRegistered;
    public (int Left, int Top, int Right, int Bottom) TopBarBounds =>
        (topBarBounds.Left, topBarBounds.Top, topBarBounds.Right, topBarBounds.Bottom);

    public (int Left, int Top, int Right, int Bottom) MonitorBounds()
    {
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        var monitor = NativeMethods.MonitorFromWindow(hwnd, 2);
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return (0, 0, 0, 0);
        return (info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
    }

    private NativeMethods.AppBarData AppBarData() => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(),
        Window = hwnd,
        CallbackMessage = AppBarCallbackMessage,
        Edge = AbeTop
    };

    private bool RefreshTopBar()
    {
        if (!topBarRegistered || applyingTopBar) return topBarRegistered;
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (topBarMonitor == 0 || !NativeMethods.GetMonitorInfo(topBarMonitor, ref info))
            return false;
        var data = AppBarData();
        data.Rectangle = info.Monitor;
        data.Rectangle.Bottom = data.Rectangle.Top + topBarHeight;
        applyingTopBar = true;
        try
        {
            // The shell handles existing top-edge appbars and multi-monitor
            // placement. Respect its proposed top edge before setting height.
            NativeMethods.SHAppBarMessage(AbmQueryPos, ref data);
            data.Rectangle.Bottom = data.Rectangle.Top + topBarHeight;
            // Another registered appbar (including Doky's bottom dock) may
            // notify us of work-area changes. Do not publish the same position
            // again and create a cross-appbar callback feedback loop.
            if (RectEquals(data.Rectangle, topBarBounds))
            {
                topBarDirty = false;
                return true;
            }
            if (NativeMethods.SHAppBarMessage(AbmSetPos, ref data) == 0)
                return false;
            topBarBounds = data.Rectangle;
            topBarDirty = false;
            return true;
        }
        finally { applyingTopBar = false; }
    }

    public void ClearTopBar()
    {
        if (topBarRegistered)
        {
            // Mark it removed before calling shell32: callbacks can be
            // delivered synchronously during ABM_REMOVE.
            topBarRegistered = false;
            var data = AppBarData();
            NativeMethods.SHAppBarMessage(AbmRemove, ref data);
        }
        topBarMonitor = 0;
        topBarHeight = 0;
        topBarBounds = default;
        topBarDirty = true;
    }

    private static bool RectEquals(NativeMethods.Rect a, NativeMethods.Rect b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    /// <summary>Limit native mouse input to the visible right-hand Split pill.
    /// Called only when expansion starts/finishes, never on animation frames.
    /// HWND ownership of the region transfers to Windows on success.</summary>
    public bool SetSplitInputRegion(int windowWidth, int windowHeight, int visibleWidth)
    {
        if (disposed) return false;
        windowWidth = Math.Max(1, windowWidth);
        windowHeight = Math.Max(1, windowHeight);
        var (left, _) = FloatingUtilityPlacement.SplitInputRegion(windowWidth, visibleWidth);
        var region = NativeMethods.CreateRectRgn(left, 0, windowWidth, windowHeight);
        if (region == 0) return false;
        if (NativeMethods.SetWindowRgn(hwnd, region, true) != 0) return true;
        NativeMethods.DeleteObject(region);
        return false;
    }

    /// <summary>Return HWND shaping to WinUI when switching away from Split.</summary>
    public void ClearSplitInputRegion()
    {
        if (!disposed) NativeMethods.SetWindowRgn(hwnd, 0, true);
    }

    public double Scale => Math.Max(1, GetDpiForWindow(hwnd) / 96d);
    public (int X, int Y) Cursor()
    {
        GetCursorPos(out var point); return (point.X, point.Y);
    }
    public bool Fullscreen()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0 || foreground == hwnd) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out var owner);
        var name = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(foreground, name, name.Capacity);
        var monitor = NativeMethods.MonitorFromWindow(hwnd, 2);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info) && NativeMethods.GetWindowRect(foreground, out var bounds) &&
            FullscreenPolicy.CoversMonitor(owner != Environment.ProcessId && name.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"),
                NativeMethods.MonitorFromWindow(foreground, 2) == monitor, NativeMethods.IsIconic(foreground),
                bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
    }
    public void Dispose()
    {
        if (disposed) return;
        ClearTopBar();
        disposed = true;
        NativeMethods.UnregisterHotKey(hwnd, 0x4773);
        NativeMethods.RemoveWindowSubclass(hwnd, callback, 73);
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
}
