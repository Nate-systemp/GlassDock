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

    // Low-overhead shell event guard. The watchdog's existing thread pumps these
    // WinEvent callbacks; no extra thread or higher-frequency polling loop is used.
    private NativeMethods.WinEventProc? shellEventCallback;
    private nint foregroundHook;
    private nint showHook;
    private bool eventGuardActive;

    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectShow = 0x8002;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;

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

    private static bool IsTaskbarWindow(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            return false;

        var name = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
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

            // An interrupted/older lease can leave Explorer's taskbar disabled while
            // no watchdog process owns it. Normalize that recoverable partial state
            // instead of refusing to start a new lease. TaskbarAutoHide.Suspend below
            // restores any stale journal before recording the new session state.
            if (!NativeMethods.IsWindowEnabled(hwnd))
                NativeMethods.EnableWindow(hwnd, true);

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

            StartEventGuard();
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

        // Validate the current taskbars as one Explorer-owned set. Explorer can
        // recreate its taskbar HWNDs (or restart) during shell transitions; that
        // should not destroy the active GlassDock lease.
        uint? currentExplorer = null;
        foreach (var hwnd in current)
        {
            if (!IsExplorerTaskbar(hwnd, out var pid))
                return false;

            if (currentExplorer is null)
                currentExplorer = pid;
            else if (currentExplorer.Value != pid)
                return false;
        }

        if (currentExplorer is null)
            return false;

        if (currentExplorer.Value != ownerProcess)
        {
            // Explorer restarted. Re-acquire its new taskbar windows while keeping
            // the same watchdog lease. New Explorer taskbars are normally visible,
            // so restore should show them when GlassDock eventually releases them.
            ownedWindows.Clear();
            ownerProcess = currentExplorer.Value;
            foreach (var hwnd in current)
                ownedWindows[hwnd] = new OwnedTaskbar(true);
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

        // Hide first. A transient auto-hide journal/shell failure must not leave
        // the visible taskbar on screen while the watchdog waits to retry.
        foreach (var hwnd in current)
            HideWindow(hwnd);

        var autoHideHealthy = TaskbarAutoHide.Maintain(journalWindow);

        var hidden = current.All(hwnd =>
            !NativeMethods.IsWindowVisible(hwnd) &&
            !NativeMethods.IsWindowEnabled(hwnd));

        if (!hidden)
        {
            // One immediate retry closes the short Explorer race without increasing
            // the watchdog's normal maintenance frequency.
            foreach (var hwnd in current)
                HideWindow(hwnd);

            hidden = current.All(hwnd =>
                !NativeMethods.IsWindowVisible(hwnd) &&
                !NativeMethods.IsWindowEnabled(hwnd));
        }

        return hidden && autoHideHealthy;
    }

    public bool Restore()
    {
        StopEventGuard();

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
        StopEventGuard();
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

    private void StartEventGuard()
    {
        if (eventGuardActive || ownerProcess == 0)
            return;

        shellEventCallback = OnShellEvent;

        // EVENT_OBJECT_SHOW catches Explorer making the taskbar visible.
        showHook = NativeMethods.SetWinEventHook(
            EventObjectShow,
            EventObjectShow,
            0,
            shellEventCallback,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);

        // Foreground changes (Task Manager is a reliable reproducer) trigger one
        // cheap proactive reassertion before Explorer can leave the taskbar up.
        foregroundHook = NativeMethods.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            0,
            shellEventCallback,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);

        eventGuardActive = showHook != 0 || foregroundHook != 0;
    }

    private void StopEventGuard()
    {
        eventGuardActive = false;

        if (showHook != 0)
            NativeMethods.UnhookWinEvent(showHook);
        if (foregroundHook != 0)
            NativeMethods.UnhookWinEvent(foregroundHook);

        showHook = 0;
        foregroundHook = 0;
        shellEventCallback = null;
    }

    private void OnShellEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int objectId,
        int childId,
        uint threadId,
        uint time)
    {
        if (!eventGuardActive || ownerProcess == 0 || ownedWindows.Count == 0)
            return;

        if (eventType == EventObjectShow)
        {
            if (!IsTaskbarWindow(hwnd) || GetOwnerProcess(hwnd) != ownerProcess)
                return;

            // Preserve the original visible state before hiding a newly-created
            // secondary taskbar so normal restore semantics remain correct.
            if (!ownedWindows.ContainsKey(hwnd))
                ownedWindows[hwnd] = new OwnedTaskbar(NativeMethods.IsWindowVisible(hwnd));

            HideWindow(hwnd);
            return;
        }

        if (eventType != EventSystemForeground)
            return;

        // Foreground events are infrequent compared with rendering frames and are
        // the exact transition that currently reproduces the Task Manager bug.
        foreach (var taskbar in FindTaskbars())
        {
            if (GetOwnerProcess(taskbar) != ownerProcess)
                continue;

            if (!ownedWindows.ContainsKey(taskbar))
                ownedWindows[taskbar] = new OwnedTaskbar(NativeMethods.IsWindowVisible(taskbar));

            HideWindow(taskbar);
        }
    }

    /// <summary>
    /// Waits efficiently for shell/WinEvent work while pumping the watchdog
    /// thread's message queue. This replaces Thread.Sleep; it does not add a
    /// faster polling loop or another thread.
    /// </summary>
    public void WaitAndPumpEvents(int milliseconds)
    {
        const uint QsAllInput = 0x04FF;
        const uint MwmoInputAvailable = 0x0004;
        const uint PmRemove = 0x0001;

        NativeMethods.MsgWaitForMultipleObjectsEx(
            0,
            0,
            (uint)Math.Max(0, milliseconds),
            QsAllInput,
            MwmoInputAvailable);

        while (NativeMethods.PeekMessage(out var message, 0, 0, 0, PmRemove))
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }

    private static void HideWindow(nint hwnd)
    {
        // Explorer may re-show or re-enable the taskbar during a foreground/shell
        // transition. These calls are idempotent and avoid a check-then-act race.
        NativeMethods.EnableWindow(hwnd, false);
        NativeMethods.ShowWindow(hwnd, 0);
    }
}
