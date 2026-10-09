# P1 #4 native tray feasibility gate

Status: blocked at feasibility; native parity is not implemented or verified.

## Evidence in the current implementation

- `SystemTrayWindow.cs`, `TryShowNativeContextMenuAsync`: requires Explorer's
  overflow window to be visible on screen, finds an accessibility element by
  name, transfers foreground/focus, and sends Shift+F10. Success means input
  was dispatched, not that the application's menu appeared.
- `WindowsTaskbarController.cs`, `HideWindow`: disables and hides Explorer's
  taskbar. The watchdog reasserts that policy on shell events and maintenance.
- `ReadRegistryMetadataRecords`: reads ExecutablePath, InitialTooltip and
  IsPromoted. These are not the registered owner HWND, callback message, icon
  identifier and negotiated notification version needed to deliver tray events.
- `OverflowScanSession`: moves Explorer overflow off screen and shows it for
  accessibility. This is a version-dependent realization technique, not a
  supported background notification-area bridge; it does not satisfy the
  visible/focusable requirements of the separate right-click path.
- `InvokeAsync`: MSAA default action is best effort. Registry items may instead
  explicitly open the executable. That is not guaranteed native single-click
  behavior and does not establish double-click semantics.
- Existing TrayActivationWiringTests inspect source strings. They do not prove
  real Discord/Steam menu delivery, visual suppression or lifecycle recovery.

## Interface assessment

Public APIs: Shell_NotifyIcon registers an application's own notification icon;
NOTIFYICONDATA specifies its callback contract. Shell_NotifyIconGetRect retrieves
geometry for a known icon, not another application's callback registration.
MSAA default action and UI Automation patterns expose only actions that the
provider implements. Generic Invoke is not a guaranteed context-menu action.

Version-dependent approaches: Explorer window classes/accessibility trees,
off-screen overflow realization, internal COM tray interfaces and tray toolbar
structures. None has been established here as a reliable Windows 11 contract
while the taskbar stays hidden and disabled. Do not ship these as native parity.

Rejected approaches: guessed callback messages, input sent to arbitrary windows,
Explorer injection/memory modification, taking over the shell, or bypassing the
watchdog. Temporarily exposing Explorer also violates the requested behavior.

Conclusion: no reliable generic native-tray bridge has been established within
the requested interfaces and constraints. This does not assert that every
undocumented technique is impossible. It means there is no justified production
architecture to implement from the evidence available. No runtime tray patch
was made at this gate; no fake menu or new unavailable-status fallback was added.

The safest achievable existing behavior is explicitly labeled app activation;
it cannot provide tray-only commands. Actual native parity would require a
supported broker contract from Windows or cooperation from each application.
Using Windows' own notification area is an alternative only if the visibility
requirement changes. Emergency taskbar restoration must always remain available.

## Validation and remaining acceptance

This gate was source/documentation inspection only. No new executable changes,
builds, automated regression runs or physical tray tests were performed for this
gate. Previous passing builds do not certify P1 #4. Feature acceptance: 0% of
the new native-parity requirements verified; investigation is complete enough
to reject the existing approach as a production implementation.

Any future candidate must demonstrate actual Discord, Steam and other available
tray menus, native single/double-click semantics, correct icon identity and live
updates, no taskbar flash, no duplicate launches, and recovery after real
sleep/wake, lock/unlock and Explorer recreation. Test shutdown/resource cleanup
and failed accessibility calls too. Do not restart Explorer as an automated
experiment. All these runtime checks remain pending.

## Primary references

- https://learn.microsoft.com/en-us/windows/win32/shell/notification-area
- https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw
- https://learn.microsoft.com/en-us/windows/win32/shell/taskbar
- https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.invokepattern.invoke
