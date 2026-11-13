# Taskbar edge and Windows-key activation

Follow-up: bare Windows now toggles the existing dock. Idle/collapsing expands; expanded/expanding collapses immediately through the same animation path used by pointer exit. Pending hover-collapse delays are canceled before keyboard transitions. The keyboard interception and shortcut pass-through remain unchanged.

The user authorized these two behaviors after the earlier Phase 2–3 restrictions.

The watchdog saves the taskbar's original ABM_GETSTATE value in a flushed, session-specific recovery journal under LocalAppData/GlassDock before clearing ABS_AUTOHIDE with ABM_SETSTATE. It then hides and temporarily disables the taskbar window. This removes the auto-hide edge trigger instead of only hiding a window that Explorer can reveal again. No Explorer process is stopped or restarted. The recovery participants serialize setting changes with a session mutex. Normal exit, initialization failure, watchdog/parent failure, missing heartbeats, and the independent recovery command restore the saved setting. The journal is removed only after verification; a subsequent lease first recovers a previous journal.

The overlay hit area reaches the bottom screen row, with the visible pill at its existing offset. After suspending auto-hide changes the work area, the overlay repositions using full monitor bounds.

A process-lifetime WH_KEYBOARD_LL callback recognizes bare Windows-key release. It passes original shortcut events through. On a bare release it inserts an unused menu-mask key (VK_E8) followed by the Windows-key release as one SendInput batch, prevents the duplicate physical release, and posts an expansion request to the UI thread. Input insertion failure passes the original release through. No key data is recorded. The callback does not run glass animation or file I/O. The existing expansion method handles both hover and keyboard activation. Ctrl+Alt+F12 remains independently registered for recovery. Secure desktops are not intercepted.

Limitations: one detected Windows taskbar is required; this is not multi-monitor taskbar support. Elevated foreground applications can reject input injection through UIPI; native behavior is retained on insertion failure. Windows may remove a slow low-level hook, especially while paused in a debugger. Simultaneous loss of App and watchdog requires the independent `GlassDock.Watchdog.exe --restore` command or a later launch to restore the saved setting. Physical Windows-key/shortcut behavior and secure desktop behavior need manual verification; unit tests verify gesture decisions, not Windows shell delivery.

References: [ABM_GETSTATE](https://learn.microsoft.com/en-us/windows/win32/shell/abm-getstate), [ABM_SETSTATE](https://learn.microsoft.com/en-us/windows/win32/shell/abm-setstate), [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc).

## Validation

Release solution and Debug App build: zero warnings/errors. All 32 tests passed (31 Core, one Windows architecture test). Live testing began with `AutoHide=true`. Suppression reported `Visible=false, Enabled=false, AutoHide=false`. Normal exit, deliberate App termination, deliberate watchdog termination, and missing-helper initialization restored/reported `Visible=true, Enabled=true, AutoHide=true`. The original setting journal contained `1` while suppressed. Physical-key tests remain manual as stated above.

The final bottom-center pixel at screen coordinate (960, 1079) accepted dock input after correcting the rounded region's exclusive lower boundary. The dock remained expanded and taskbar status remained hidden, disabled, and auto-hide suspended. Test instances were closed and auto-hide restored afterwards.

## Sleep and long-heartbeat-gap recovery, 2026-11-13

The active watchdog previously ended its process after ten seconds without UI heartbeats. Long sleep could reach that deadline, restore the taskbar, and stop the App heartbeat timer through the session-ended callback. The user then had to resume suppression manually.

An established `--watch-active` session now restores the taskbar at the same ten-second deadline and sends `WAITING: heartbeat expired.` while retaining the verified parent connection. The UI session records that renewal is required. Its next dispatcher heartbeat sends `HIDE`, reacquiring taskbar ownership and original-setting journaling through the existing controller. Ordinary queued `PING` messages leave the restored taskbar visible while waiting. Each renewal request is consumed once to avoid duplicate `HIDE` commands.

The watchdog waits with suppression released while the UI is unresponsive. Parent exit, EOF, `RESTORE`, and the independent emergency event still end the connection. The bounded `--watch` development test retains its sixty-second cap and ends on missing heartbeats. Suppression remains an explicit watchdog-owned development lease with independent recovery.

Validation on this S0 Low Power Idle system used a temporary parent fixture with the required App process identity and the real old/new watchdog executables. Heartbeats were withheld for 12.5 seconds in each timeout case.

- The old watchdog exited and restored the taskbar, reproducing the loss of automatic suppression.
- The new active watchdog remained connected with the taskbar visible and enabled while heartbeats were withheld.
- The first recovered UI heartbeat hid and disabled the taskbar again without a Resume action.
- A second heartbeat timeout followed by the independent recovery command ended the waiting watchdog. A later heartbeat left the taskbar visible.
- The bounded development session exited after heartbeat timeout and a later heartbeat left the taskbar visible.
- `scripts/Validate.ps1` passed with zero build warnings/errors, 456 Core tests, and 327 Windows tests. Core tests also model an eight-hour pause and retain the bounded sixty-second deadline.

Physical overnight sleep and hibernate cycles remain unverified. This validation covers the heartbeat-gap and recovery protocol using live taskbar operations.
