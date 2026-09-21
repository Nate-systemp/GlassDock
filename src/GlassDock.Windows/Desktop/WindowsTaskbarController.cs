using System.Diagnostics;
using System.Text;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

/// <summary>
/// Watchdog-owned taskbar visibility/input and recoverable auto-hide suspension.
/// Supports the primary Explorer taskbar plus any secondary-monitor taskbars.
/// </summary>
public sealed class WindowsTaskbarController : ITaskbarController
{
    private sealed record OwnedTaskbar(bool WasVisible);

    private readonly Dictionary<nint, OwnedTaskbar> ownedWindows = new();
    private uint ownerProcess;

    private static List<nint> FindTaskbars()
    {
        var windows = new List<nint>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var name = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, name, name.Capacity);

            if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                windows.Add(hwnd);

            return true;
        }, 0);

        return windows.Distinct().ToList();
    }

    private static uint GetOwnerProcess(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    private static bool IsExplorerTaskbar(nint hwnd, out uint pid)
    {
        pid = GetOwnerProcess(hwnd);
        if (pid == 0)
            return false;

        try
        {
            using var owner = Process.GetProcessById((int)pid);
            return string.Equals(
                owner.ProcessName,
                "explorer",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public TaskbarStatus Inspect()
    {
        var windows = FindTaskbars();

        return new(
            windows.Count > 0,
            windows.Any(NativeMethods.IsWindowVisible),
            windows.Count,
            windows.Count > 0 && windows.All(NativeMethods.IsWindowEnabled),
            (TaskbarAutoHide.Read() & 1) != 0);
    }

    public void HideForTest()
    {
        if (ownedWindows.Count != 0)
            throw new InvalidOperationException("A taskbar lease is already active.");

        var windows = FindTaskbars();
        if (windows.Count == 0)
            throw new InvalidOperationException("Windows taskbar was not found.");

        uint? explorerProcess = null;

        foreach (var hwnd in windows)
        {
            if (!NativeMethods.IsWindow(hwnd))
                throw new InvalidOperationException("A taskbar window disappeared during initialization.");

            if (!NativeMethods.IsWindowEnabled(hwnd))
                throw new InvalidOperationException(
                    "A taskbar is already disabled; refusing to take ownership.");

            if (!IsExplorerTaskbar(hwnd, out var pid))
                throw new InvalidOperationException(
                    "A taskbar window is not owned by Explorer.");

            if (explorerProcess is null)
                explorerProcess = pid;
            else if (explorerProcess.Value != pid)
                throw new InvalidOperationException(
                    "Taskbar windows are owned by different Explorer processes.");

            ownedWindows[hwnd] = new OwnedTaskbar(
                NativeMethods.IsWindowVisible(hwnd));
        }

        ownerProcess = explorerProcess!.Value;

        // ABM_SETSTATE is a shell-wide taskbar setting. One valid Explorer
        // taskbar HWND is enough for the recoverable auto-hide journal.
        var journalWindow = windows[0];

        try
        {
            TaskbarAutoHide.Suspend(journalWindow);

            foreach (var hwnd in windows)
                HideWindow(hwnd);

            if (windows.Any(hwnd =>
                    NativeMethods.IsWindowVisible(hwnd) ||
                    NativeMethods.IsWindowEnabled(hwnd)))
            {
                throw new InvalidOperationException(
                    "Windows did not hide every taskbar.");
            }
        }
        catch
        {
            Restore();
            throw;
        }
    }

    public bool MaintainHidden()
    {
        if (ownedWindows.Count == 0 || ownerProcess == 0)
            return false;

        var current = FindTaskbars();
        if (current.Count == 0)
            return false;

        // If Explorer restarted, our original ownership is no longer valid.
        foreach (var hwnd in current)
        {
            if (!IsExplorerTaskbar(hwnd, out var pid) || pid != ownerProcess)
                return false;
        }

        // A secondary taskbar can be created after a monitor/display change.
        // Adopt it into this lease immediately so bottom-edge hover cannot
        // reveal it on the newly active display.
        foreach (var hwnd in current)
        {
            if (!ownedWindows.ContainsKey(hwnd))
            {
                ownedWindows[hwnd] = new OwnedTaskbar(
                    NativeMethods.IsWindowVisible(hwnd));
            }
        }

        // Forget taskbar HWNDs that disappeared because a display was removed.
        foreach (var hwnd in ownedWindows.Keys
                     .Where(hwnd => !NativeMethods.IsWindow(hwnd))
                     .ToArray())
        {
            ownedWindows.Remove(hwnd);
        }

        var journalWindow = current[0];

        if (!TaskbarAutoHide.Maintain(journalWindow))
            return false;

        foreach (var hwnd in current)
            HideWindow(hwnd);

        return current.All(hwnd =>
            !NativeMethods.IsWindowVisible(hwnd) &&
            !NativeMethods.IsWindowEnabled(hwnd));
    }

    public bool Restore()
    {
        if (ownedWindows.Count == 0)
            return true;

        var snapshot = ownedWindows.ToArray();
        ownedWindows.Clear();

        var expectedOwner = ownerProcess;
        ownerProcess = 0;

        var validWindows = new List<nint>();

        foreach (var (window, state) in snapshot)
        {
            if (!NativeMethods.IsWindow(window))
                continue;

            NativeMethods.GetWindowThreadProcessId(window, out var pid);
            if (pid != expectedOwner)
                return EmergencyRestore();

            NativeMethods.EnableWindow(window, true);

            if (state.WasVisible)
                NativeMethods.ShowWindow(window, 8); // SW_SHOWNA: do not activate Explorer.

            validWindows.Add(window);
        }

        if (validWindows.Count == 0)
            return EmergencyRestore();

        return TaskbarAutoHide.Restore(validWindows[0]) &&
               validWindows.All(NativeMethods.IsWindowEnabled);
    }

    public bool EmergencyRestore()
    {
        ownedWindows.Clear();
        ownerProcess = 0;

        var windows = FindTaskbars();

        foreach (var window in windows)
        {
            NativeMethods.EnableWindow(window, true);
            NativeMethods.ShowWindow(window, 8);
        }

        if (windows.Count == 0)
            return false;

        return TaskbarAutoHide.Restore(windows[0]) &&
               windows.All(NativeMethods.IsWindowEnabled);
    }

    private static void HideWindow(nint hwnd)
    {
        if (NativeMethods.IsWindowEnabled(hwnd))
            NativeMethods.EnableWindow(hwnd, false);

        if (NativeMethods.IsWindowVisible(hwnd))
            NativeMethods.ShowWindow(hwnd, 0);
    }
}
