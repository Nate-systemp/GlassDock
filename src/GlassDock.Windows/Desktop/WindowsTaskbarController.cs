using System.Diagnostics;
using System.Text;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

/// <summary>Only reversible visibility changes. Never changes Explorer or appbar configuration.</summary>
public sealed class WindowsTaskbarController : ITaskbarController
{
    private nint ownedWindow;
    private uint ownerProcess;
    private bool wasVisible;

    private static List<nint> FindTaskbars()
    {
        var windows = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var name = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") windows.Add(hwnd);
            return true;
        }, 0);
        return windows;
    }

    public TaskbarStatus Inspect()
    {
        var windows = FindTaskbars();
        return new(windows.Count > 0, windows.Any(NativeMethods.IsWindowVisible), windows.Count);
    }

    public void HideForTest()
    {
        if (ownedWindow != 0) throw new InvalidOperationException("A taskbar lease is already active.");
        var windows = FindTaskbars();
        if (windows.Count != 1) throw new InvalidOperationException("Taskbar tests require exactly one Windows taskbar.");
        var hwnd = windows[0];
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        using var owner = Process.GetProcessById((int)pid);
        if (!string.Equals(owner.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The taskbar is not owned by Explorer.");
        wasVisible = NativeMethods.IsWindowVisible(hwnd);
        ownedWindow = hwnd;
        ownerProcess = pid;
        NativeMethods.ShowWindow(hwnd, 0);
        if (NativeMethods.IsWindowVisible(hwnd))
        {
            Restore();
            throw new InvalidOperationException("Windows did not hide the taskbar.");
        }
    }

    public bool MaintainHidden()
    {
        if (ownedWindow == 0 || !NativeMethods.IsWindow(ownedWindow)) return false;
        NativeMethods.GetWindowThreadProcessId(ownedWindow, out var pid);
        if (pid != ownerProcess || !FindTaskbars().SequenceEqual(new[] { ownedWindow })) return false;
        if (NativeMethods.IsWindowVisible(ownedWindow)) NativeMethods.ShowWindow(ownedWindow, 0);
        return !NativeMethods.IsWindowVisible(ownedWindow);
    }

    public bool Restore()
    {
        if (ownedWindow == 0) return true;
        var window = ownedWindow;
        ownedWindow = 0;
        NativeMethods.GetWindowThreadProcessId(window, out var pid);
        if (!NativeMethods.IsWindow(window) || pid != ownerProcess) return EmergencyRestore();
        if (wasVisible) NativeMethods.ShowWindow(window, 8); // SW_SHOWNA: do not activate Explorer.
        return !wasVisible || NativeMethods.IsWindowVisible(window);
    }

    public bool EmergencyRestore()
    {
        var windows = FindTaskbars();
        foreach (var window in windows) NativeMethods.ShowWindow(window, 8);
        return windows.Count > 0 && windows.All(NativeMethods.IsWindowVisible);
    }
}
