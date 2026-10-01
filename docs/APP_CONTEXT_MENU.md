# Doky app context menu

`WindowPreviewCoordinator` still owns command routing through the existing application view model/service. App buttons raise `ContextRequested` (mouse or keyboard); the empty-dock menu is unchanged. `DockAppMenuState` records capability flags and window identities, including PID/start time, so a closed or reused HWND invalidates an open menu.

One lazily created `DockAppContextMenuWindow` is reused across icons. Its header uses the actual application icon. Action rows use `UtilityPopupTheme` and the same `UtilityPopupStyle` / `DesktopGlassBackdrop` material path as the dock's utilities. Native client configuration precedes backdrop attachment. Appearance changes update the retained brushes and material. There is no separate menu theme or pointer geometry.

After the first live review, the surface was reduced to 240 DIP wide with 34 DIP action rows, a 26 DIP header icon and 13 DIP action labels. The transparent shadow gutter is separate from the panel dimensions.

The window uses the existing DPI-aware `WindowPreviewLayout.Position` work-area clamp, including a pinned dock's reserved work area. Tall window lists scroll. Multiple-window actions use a nested list with Back. Arrow keys, Home/End, Tab, Enter and Escape are supported; outside activation dismisses the menu. App activation commands dismiss immediately before executing so the menu does not take foreground back. Opening/Escape dismissal use a 140 ms, 6 DIP compositor transition shared by content and native glass, respecting Windows animation preferences. No idle animation timer runs.

The coordinator holds the dock and suppresses hover previews while the menu is present, including its close transition. Show All Windows opens the existing preview after the menu hold releases. Stale snapshots, button removal, closing the window and coordinator disposal release the menu cleanly.

## Verification

Automated coverage: running/stopped and pinned/unpinned availability, launch/elevation/location capabilities, single/multiple windows, stale/reused HWND identities, floating/pinned work-area clamping at 100–200% DPI and negative monitor origins, shared appearance wiring and preview hold integration.

Runtime visual verification is still required: running pinned/unpinned and stopped pinned apps; one/multiple windows; floating/Keep Doky expanded; Dark/Light/Frosted/Acrylic/Clear; Activate, New window, Show All Windows, Close Window, Pin/Unpin, Run as Administrator and Open File Location. Also inspect keyboard navigation, outside dismissal, long-list scrolling and preview coexistence. A passing build does not establish visual correctness.

The first Debug build stayed running with its watchdog and native backdrop initialized. A user-provided live screenshot confirmed the app header, action icons and grouped rows for File Explorer; the user requested the compact sizing above. That screenshot does not verify commands, all material modes or the revised sizing.
