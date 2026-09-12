# Desktop dock foundation checkpoint

Checkpoint requested before further features. No adaptive sizing or application discovery was added.

## Validation

- Release solution: build passed, zero warnings/errors; 32/32 tests passed.
- Debug App: build passed, zero warnings/errors.
- Locked restore with `BuildingInsideVisualStudio=true`: passed.
- Home Indicator: observed as a single thin pill.
- Expansion and collapse: observed intermediate and final frames in both directions using Ctrl+Alt+Space, which uses the same toggle handler as bare Windows.
- Overlay: visually observed above the underlying normal desktop windows. Exclusive fullscreen, secure desktop, and all application types were not tested.
- Taskbar: live suppression reported Visible=false, Enabled=false, AutoHide=false. Normal exit restored Visible=true, Enabled=true, AutoHide=true.
- Bare Windows and Win+R/E/L/D/Tab: gesture unit tests pass; physical shortcut behavior remains unverified pending user confirmation. Automated Windows-key input is prohibited by the computer-use skill. Do not interpret the commit title as proof of those physical tests.

## Critical build regression fixed

Visual Studio evaluated RuntimeIdentifiers as win-x86;win-x64;win-arm64 while CLI evaluated only win-x64. This repeatedly invalidated locked restore with NU1004. Explicit RuntimeIdentifiers=win-x64 now matches the existing RuntimeIdentifier and x64 platform target in both environments; the lock file was regenerated and both restore paths verified.

## Snapshot scope

This checkout has an empty item array in DesktopOverlayWindow.CreateItems. It has no pinned-app discovery, running-app collection, or app launching/activation services. The later adaptive-sizing request describes those as existing prerequisites; the appropriate implementation/checkout must be supplied before that request can be completed without introducing excluded discovery features. The current UI and user edits are preserved.

See TASKBAR_AND_WINDOWS_KEY_FIX.md for recovery tests and known operational limitations.
