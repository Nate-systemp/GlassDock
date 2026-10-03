using System.ComponentModel;
using System.Runtime.InteropServices;
using GlassDock.Core.Applications;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public readonly record struct DesktopMonitor(
    nint Handle,
    PixelRect Bounds,
    bool IsPrimary)
{
    public string Key => $"0x{Handle.ToInt64():X}";
}

/// <summary>
/// Small monitor-topology service used by the multi-monitor dock coordinator.
/// It never owns windows and can be queried whenever WM_DISPLAYCHANGE/topology
/// changes need to be reconciled.
/// </summary>
public static class WindowsMonitorService
{
    public static IReadOnlyList<DesktopMonitor> GetMonitors()
    {
        var result = new List<DesktopMonitor>();
        NativeMethods.MonitorEnumProc callback = (nint monitor, nint hdc, ref NativeMethods.Rect monitorRect, nint data) =>
        {
            var info = new NativeMethods.MonitorInfo
            {
                Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
            };

            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return true;

            result.Add(new DesktopMonitor(
                monitor,
                new PixelRect(
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top),
                (info.Flags & 1u) != 0));
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        GC.KeepAlive(callback);
        return result
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.Bounds.X)
            .ThenBy(monitor => monitor.Bounds.Y)
            .ToArray();
    }

    public static nint PrimaryMonitorHandle()
    {
        var primary = GetMonitors().FirstOrDefault(monitor => monitor.IsPrimary);
        if (primary.Handle != 0)
            return primary.Handle;

        return NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);
    }

    public static ApplicationSnapshot FilterSnapshotForMonitor(
        ApplicationSnapshot snapshot,
        nint monitor)
    {
        if (monitor == 0)
            return snapshot;

        return DockApplicationMonitorFilter.ForMonitor(
            snapshot,
            window => window.Handle != 0 &&
                NativeMethods.MonitorFromWindow((nint)window.Handle, 2) == monitor);
    }
}
