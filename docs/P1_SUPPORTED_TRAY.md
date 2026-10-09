# P1 #4 Phase 2: supported tray functionality

This supersedes the implementation status in the feasibility gate, not its
native-parity limitations. No native-menu workarounds remain in the tray path.

## Implementation

- Windows integration now lives in `WindowsTrayAccessibility`, outside the UI.
  Existing Explorer accessibility providers are scanned read-only on an STA
  worker. Doky never shows/moves the overflow, invokes its chevron, injects input,
  or changes taskbar suppression to obtain an accessibility tree.
- A **Live action** is an enabled, named provider default action. Invocation
  resolves the item again and requires a unique exact name. Its HWND owner PID
  and process start time must still match Explorer; an old Explorer reference
  cannot silently become an application launch.
- An **Open Application** tile is explicitly a registry-derived shortcut, not
  a guaranteed live icon. Discovery requires the exact running executable path,
  not just the process basename. Historical/nonrunning entries are filtered;
  executable paths are deduplicated. Unique exact tooltip metadata matches
  suppress corresponding shortcut duplicates beside accessible entries.
- Shortcuts use application executable imagery. Live accessibility items use a
  named placeholder: this provider does not expose an accurate live bitmap.
  Unknown identity matches are not guessed. Tooltip changes can change the
  identity/order of an accessible item; there is no public stable icon ID here.
- App opening prefers an existing matching process with a window. A running
  background-only app is not relaunched to guess its activation contract. The
  failure is reported instead. This may limit tray-only apps such as GHelper
  when Explorer exposes no usable default action.
- An activation latch and Windows double-click interval suppress accidental
  repeat dispatch. There is no separate native double-click implementation.
- Right-click shows **Doky tray controls**, with Refresh and Move earlier/later.
  Windows tray settings are available only from explicitly labeled settings
  controls, never as a substitute for an app's context menu.
- Local drag reordering uses before/after edge cues and XAML reposition
  transitions. Existing tiles are moved rather than destroyed during a reorder.
  `tray-order.json` is atomically saved in the canonical Doky user-data directory;
  existing settings and pins are untouched. Missing entries retain saved keys.

## Refresh and recovery

Refresh runs on opening, explicit request and every three seconds **only while
the popup is visible**. No supported third-party tray change subscription is
available here. Scans never overlap or accumulate; results are deferred during
drag, management menus and activation. Unchanged tiles retain focus and scroll.
Each scan reacquires roots/processes and releases accessibility COM references.
Explorer restart, resume/unlock, app changes and monitor changes therefore use
fresh discovery on the next visible scan/open, rather than cached native handles.
Hidden/closed popups stop their timer; closed windows discard pending results.

No new global hook, process watcher, elevated operation or taskbar policy was
added. A hung third-party accessibility provider can still stall its STA call;
the UI remains asynchronous and does not spawn replacement scans indefinitely.

## Validation

2026-10-09: Debug and Release solution builds succeeded with zero warnings and
errors. Full tests passed in each configuration: 311 Core + 176 Windows = 487,
zero failures/skips. `scripts/Validate.ps1` completed successfully (locked
restore, Release build and both test projects). The Debug app was launched;
interactive Windows acceptance below is still pending.

Automated coverage exercises unique/default-action routing, unavailable and
ambiguous providers, stale Explorer references, exact executable matching,
rapid-click suppression, reorder/deduplication and order persistence/corruption.
Source boundary tests reject the removed Explorer/input workarounds. They do
not prove a native application's action, pointer animation or hardware recovery.

Manual acceptance checklist (pending):

1. Open Hidden Tray with taskbar suppression enabled; confirm no taskbar flash.
2. Check Live action versus Open Application labels and available imagery.
3. Activate each available application; rapid double-click must not launch twice.
   Test background-only and elevated applications; record unavailable actions.
4. Right-click: only Doky management commands. Escape/outside-click dismissal.
5. Drag before/after, cancel a drag, reorder with management commands, reopen and
   restart Doky; order persists without duplicate/missing tiles.
6. Start/exit a tray app while open; entries reconcile within a scan. Repeat with
   Explorer restarted by Windows/user, physical sleep/wake and lock/unlock.
7. Move between monitors; verify layout, reorder hit targets and all five themes.
8. Check dock animation, keyboard recovery, fullscreen suppression and watchdog
   recovery using the existing procedures. Do not kill Explorer as a test helper.

Working estimate: 70% of the revised supported P1 #4 scope, pending actual Windows
interaction/lifecycle acceptance. Native menus, generic live bitmaps, complete
tray enumeration and application-specific double-click contracts remain blocked
by provider capabilities and are not counted as implemented features.
