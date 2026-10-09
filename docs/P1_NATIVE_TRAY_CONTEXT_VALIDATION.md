# Doky P1 #4 — Native tray right-click validation (best effort)

## Why Phase 1 was incorrect

Doky attached its own `MenuFlyout` containing `Open application` and `Windows tray settings`. These were Doky actions, **not** the right-click menu supplied by Discord, Steam, or any other application's tray icon.

## Changed behavior

- Right-click/keyboard context requests are handled separately from left-click activation.
- Right-click attempts to locate the **live** native Windows Explorer overflow icon by an exact normalized accessible name.
- The native overflow must be visible and on screen; we do not click a historical registry record, a guessed pixel, or an invented owner HWND.
- Doky only transfers focus once the icon is found, and only sends Windows' standard Shift+F10 context-menu gesture if the native item can accept focus, **MSAA confirms that focus**, and Explorer is foreground.
- Failed or unavailable native actions show a clear status message rather than a fake application context menu.
- Existing left-click/native default action and explicit registry-only open-app semantics are unchanged.
- No changes to Liquid Glass themes, dock animation, Win-key hooks, helper, or watchdog.

**Limits:** Windows does not offer a stable public API to enumerate and invoke arbitrary third-party tray context menus. Some Windows 11 XAML/MSAA providers refuse focus, and Doky may hide the Windows taskbar/Explorer overflow. In these cases **right-click remains unavailable** instead of being faked. Even if keyboard forwarding succeeds, only a **physical Windows session** can establish that the intended native menu actually appeared. This is a guarded attempt, **not guaranteed complete native tray parity**.

## Build on user's Windows machine

```powershell
cd C:\Dev\GlassDock
dotnet build C:\Dev\GlassDock\GlassDock.sln -c Debug
dotnet test C:\Dev\GlassDock\GlassDock.sln -c Debug
```

## Manual acceptance procedure

1. Close/restart the built Debug Doky; do not mix the running installed version and the Debug build.
2. Open Discord and Steam so their tray icons are running. Open Doky's tray panel.
3. Right-click **Discord**. Verify the actual Discord menu (not Doky's former `Open application / Windows tray settings` fallback).
4. Repeat for **Steam**, including opening and dismissing the menu without launching a duplicate Steam instance.
5. Left-click both icons and confirm existing app/default action still works.
6. Repeat with Explorer native tray visible and hidden, after restarting Explorer, and after lock/unlock.
7. Right-click a registry-only icon without a matching live accessible control. The tray status should clearly say native menu unavailable; no unrelated menu or app launch should occur.
8. Verify Win-key open/close and Clear mode still work.
9. Attach `%LOCALAPPDATA%` Doky user-data `tray-activation.log` (if it exists) and a screenshot/symptom if native menu fails; the output traces whether Windows exposed a visible, focusable control.

## Do not mark as DONE if

- Either test icon opens Windows tray settings or the Doky fallback instead of its real menu.
- The app unexpectedly launches on right-click.
- Context menu is sent to the wrong target/desktop/window.
- Tests compile but real Windows native context menus do not appear.

Only the user's runtime tests can decide whether forwarding works on this PC. Future full native tray parity would require a separately validated integration strategy; avoid undocumented Explorer shell memory scraping or guessing `Shell_NotifyIcon` callback HWND/message IDs.
