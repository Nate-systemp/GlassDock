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

}
