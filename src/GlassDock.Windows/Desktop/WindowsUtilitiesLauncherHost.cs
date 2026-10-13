using System.Runtime.InteropServices;
using GlassDock.Windows.Interop;
using GlassDock.Core.Desktop;

namespace GlassDock.Windows.Desktop;

public sealed class WindowsUtilitiesLauncherHost : IDisposable
{
    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;
    private int shortcut;
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
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
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
        disposed = true;
        NativeMethods.UnregisterHotKey(hwnd, 0x4773);
        NativeMethods.RemoveWindowSubclass(hwnd, callback, 73);
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
}
