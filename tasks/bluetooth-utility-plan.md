# Bluetooth utility implementation

User authorization: the attached Doky Bluetooth implementation task (2026-11-06). No commit, pairing/discovery workaround, shell changes, or new dependency.

## Investigation and decisions
- Bluetooth is an unavailable UtilityDefinition entry; no Bluetooth service exists.
- Reuse UtilityButton, UtilityPlacement, UtilityIcon/Phosphor assets, UtilityGlassSurface, WifiNetworkContentEffect, PopupCompositionTrack, and the activating Wi-Fi popup host.
- Extract that host to UtilityPopupWindow with a real panel contract used by both Wi-Fi and Bluetooth. Preserve the Wi-Fi wrapper and interaction behavior.
- Windows owns radios, paired-only device watchers and operations. Core owns immutable device presentation and bounded list geometry/state. UI owns expansion, confirmation and rendering.
- A successful request is not a connected device. LE uses GATT and actual connection status; audio uses the existing Core Audio service plus documented KS reconnect/disconnect properties and confirms actual endpoint state. Unsupported device operations report failure without a Settings fallback. Never unpair or reinstall profile drivers as a connect/disconnect workaround.
- No earlier removal Option A exists in source; unpair requires explicit in-panel confirmation.
- One shared glass surface, five-row maximum viewport; expanded content remains scrollable within it. All row effects affect content only.

## Work
- [x] Windows Bluetooth service, event lifetime, cancellation and truthful operations; focused state tests.
- [x] Shared popup host and Bluetooth panel with retained rows, one glass surface, measured expansion and fading scrollbar.
- [x] Utility registration, shared runtime lifetime, input/morph/placement and dock integration.
- [x] Final review fix for partially clipped expanded actions and scoped source review.
- [x] Documentation and validation limits recorded in docs/BLUETOOTH_UTILITY.md.

## Verification
The user now requires manual verification: do not build or run tests again. Before that instruction, Debug and Release builds succeeded with 0 warnings/errors; Core tests passed 434/434; Windows tests passed 248/249. The single failure is a pre-existing source-string assertion in DockAppMenuWiringTests.cs:45 (confirmed against HEAD), left unchanged. Focused Bluetooth tests passed 21/21 and geometry tests 5/5. Later review fixes are source-reviewed only. No radio operations or hardware behavior were tested, and no commit was made.

Independent final review found one issue: the content effect clipped expanded action hit targets near viewport edges. The fix keeps actions outside the clipped header source and confines the header tap fallback to the header. No other material service, popup extraction or integration finding was reported.

Scoped independent re-review confirmed the fix in source and found no related regression. Runtime appearance and hardware acceptance remain with the user's manual verification; no further builds or tests were run.

Ruling: retain the documented inline removal confirmation because the earlier Option A is absent from source; the cost is one extra confirmation before unpairing.

Primary API references: https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client and https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/enumerate-devices

## Direct connection follow-up
- Removed device Settings links and obsolete NeedsSettings state; retained row layout, actions, hover, scrolling and animation.
- Added driver-specific audio reconnect/disconnect to the existing Core Audio endpoint implementation, matching container IDs and confirming real endpoint state.
- LE connection maintenance falls back to uncached GATT discovery; actual connection status remains required. Connection operations have a 20-second cancellation budget and retain existing duplicate/stale-completion guards.
- Updated failure/retry state tests and added a no-container identity boundary check. Tests are not run per user instruction; this follow-up explicitly authorizes build verification only.
- Classic HID reconnection without a driver-supported control remains unavailable; fail truthfully rather than toggling driver installation or treating SDP discovery as a connection.
- Final Debug solution build passed with 0 warnings and 0 errors. Tests were compiled but not run; no runtime or hardware operations were performed.
