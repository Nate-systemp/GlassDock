using System.Diagnostics;
using System.Runtime.InteropServices;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public readonly record struct HomeMonitorArea(int X, int Y, int Width, int Height, double Scale);

public static class HomeDesktopEnvironment
{
    public static HomeMonitorArea Resolve(DockDisplayMode mode, nint owner, double bottomMargin)
    {
        nint monitor = 0;
        if (mode == DockDisplayMode.Foreground)
            monitor = NativeMethods.MonitorFromWindow(NativeMethods.GetForegroundWindow(), 2);
        else if (mode is DockDisplayMode.Pointer or DockDisplayMode.AllDisplays && NativeMethods.GetCursorPos(out var cursor))
            monitor = NativeMethods.MonitorFromPoint(cursor, 2);
        else monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            monitor = NativeMethods.MonitorFromWindow(owner, 2);
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Monitor work area unavailable.");
        }
        var scale = NativeMethods.GetDpiForMonitor(monitor, 0, out var dpi, out _) >= 0 ? dpi / 96d : 1;
        // Honor reserved work areas and leave room for the existing dock plus a visible gap.
        var bottom = Math.Min(info.Work.Bottom, info.Monitor.Bottom - (int)Math.Round((bottomMargin + 92) * scale));
        var inset = (int)Math.Round(12 * scale);
        return new(info.Work.Left + inset, info.Work.Top + inset,
            Math.Max(1, info.Work.Right - info.Work.Left - inset * 2),
            Math.Max(1, bottom - info.Work.Top - inset * 2), scale);
    }

    public static bool OpenSettings(string uri)
    {
        try { using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); return true; }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    public static bool Lock() => LockWorkStation();
    public static Task<bool> SleepAsync() => Task.Run(() => SetSuspendState(false, false, false));
    public static bool Shutdown(bool restart)
    {
        try
        {
            // No force flag: Windows can still protect unsaved work.
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), restart ? "/r /t 0" : "/s /t 0")
                { UseShellExecute = false, CreateNoWindow = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LockWorkStation();
    [DllImport("powrprof.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWake);
}
