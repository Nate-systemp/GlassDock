# Doky Bluetooth utility

Bluetooth is available through the existing Utilities settings and placement system. It remains a separate circular control beside the dock. Left-click requests a real radio change; right-click or hold opens paired devices while Bluetooth is on. While off, the existing Wi-Fi-style gear morph exposes Windows Bluetooth settings and repeated right-click reverses it.

## Implementation

- `Controls/UtilityButton.cs` shares Wi-Fi sizing, material, pointer behavior and icon morphing with Bluetooth without enabling dock magnification.
- `Desktop/DesktopOverlayWindow.Bluetooth.cs` and the existing utility integration own popup lifetime, dismissal, capture participation and dispatcher updates. `DokySharedRuntime` owns one lazy Bluetooth service shared across monitors.
- `Controls/BluetoothDevicePanel.cs` retains device rows inside one `UtilityGlassSurface`. The viewport is capped at five 48-DIP normal rows; expanded details remain scrollable. Details use measured height and reversible transitions. The overlay scrollbar fades after inactivity. Existing Phosphor assets, `WifiNetworkContentEffect`, `PopupCompositionTrack` and utility placement are reused.
- `Desktop/UtilityPopupWindow.cs` extracts the existing Wi-Fi popup host. Both panels implement `IUtilityPanel`; `WifiUtilityWindow` remains a wrapper around the same behavior.
- `Windows/Desktop/BluetoothUtilityService.cs` owns native radios, paired-device watchers, operations and disposal. `BluetoothDeviceStore.cs` owns snapshots, stable updates, duplicate-operation prevention and completion generations. Core contains the device presentation record and list geometry.
- Utility registration/persistence tests and focused Bluetooth state/geometry tests cover the new integration without issuing hardware operations.

## Windows operations

`Windows.Devices.Radios.Radio` provides actual Bluetooth radio state and supported toggle requests. Paired-only `BluetoothDevice` and `BluetoothLEDevice` selectors feed `DeviceWatcher` instances. Watchers start on first refresh, remain shared when panels reopen, and stop when the radio is off or the service is disposed. No unpaired discovery or periodic battery polling is used.

For Bluetooth audio, the existing `SystemControlAudio` COM endpoint service now matches the selected AEP's `System.Devices.Aep.ContainerId` to audio endpoint container IDs. It follows `IDeviceTopology` connectors to the driver's KS filter, verifies `KSPROPSETID_BtAudio` support, and sends `KSPROPERTY_ONESHOT_RECONNECT` or `KSPROPERTY_ONESHOT_DISCONNECT` through `IKsControl`. It confirms actual endpoint state for up to 15 seconds; accepting the driver request never counts as success. Matching uses identity, not device names. It does not change the default audio output, enable disabled endpoints, restart drivers or change profile installation.

For supported LE devices, `GattSession.MaintainConnection` requests a connection, with uncached GATT service discovery available when connection maintenance is unsupported. `BluetoothLEDevice.ConnectionStatus` confirms the connection, with a 15-second wait and an overall 20-second connection cancellation budget. Merely constructing the device or submitting a request never counts as success. This covers BLE input devices and other GATT peripherals when their Windows access/driver permits it. Disconnect releases Doky's retained GATT session and rereads actual Windows state. Other applications or Windows profiles may retain the connection.

Windows exposes no universal explicit connect/disconnect API for every classic/profile device. Classic HID mice, keyboards and controllers without a supported direct driver operation remain device/OS-controlled. Unsupported, denied, unreachable and timed-out connection attempts report `Connection failed`, with retry through the same row. Device interactions contain no Windows Settings link or fallback. `BluetoothSetServiceState` is not used as a reconnect workaround because it installs/removes profile drivers; RFCOMM/SDP discovery and already-paired requests are not treated as functional audio/HID connections. There is no unpair/re-pair workaround or reverse-direction audio-sink API misuse. Pending operation results are invalidated on radio-off, removal and disposal; Windows itself may not cancel an already-submitted native connection immediately.

Battery icons use only valid Windows-reported `System.Devices.BatteryLife` values from 0 through 100. Missing/unknown data hides the icon. No numeric percentage is displayed. Device classes determine Phosphor icons; unknown classes use the generic Bluetooth icon.

The earlier removal “Option A” was not discoverable. Removal therefore requires an inline confirmation before `DeviceInformation.Pairing.UnpairAsync`; unsuccessful removal reports `Removal failed`. This conservative assumption adds one confirmation interaction.

## Verification record

Before the user's instruction to stop building and running tests:

- Debug solution build: succeeded, 0 warnings and 0 errors.
- Release solution build from `scripts/Validate.ps1`: succeeded, 0 warnings and 0 errors.
- Core suite: 434 passed.
- Windows suite: 248 passed, 1 failed. The failure is the pre-existing source-string assertion at `DockAppMenuWiringTests.cs:45`, which expects `return liquidGlass.BeginScreenshotMode();`; the baseline already uses `var frozen = ...; return frozen;`. This unrelated fixture was left unchanged.
- Focused Bluetooth service tests: 21 passed. Geometry tests: 5 passed.
- A Debug startup was initiated with `--safe-mode --basic-rendering --controls`; the process exposed the Floating Dock window. This does not verify Bluetooth UI, GPU glass or hardware behavior.

The user then requested no further builds or test runs and will verify manually. Later review fixes are source-reviewed only. No radio toggle, connection, disconnection or unpairing was exercised by the agent. No commit was made.

Independent review identified and resolved a clipped-action hit-testing issue: edge blur/fade now captures only the device header; expanded controls remain outside the clipped source. A scoped source re-review confirmed the correction. That change was initially source-reviewed only.

The subsequent list refinement removes the Bluetooth/On heading and Bluetooth settings footer, including their reserved space. Device icons now match Wi-Fi's 14-DIP size, names use `PopupStyle.CompactFontSize`, and connection status occupies the rightmost column on the same line. Full-width native row buttons retain their hit targets outside the clipped content effect, with Wi-Fi's 150-ms hover fill transition. The narrow native scrollbar uses the existing inactivity fade and the viewport remains capped at five normal rows. Error/empty/off messages remain available.

For this refinement the user explicitly requested build verification: `dotnet build src/GlassDock.App/GlassDock.App.csproj -c Debug --no-restore` succeeded with 0 warnings and 0 errors. No tests or runtime/hardware checks were run for the refinement.

The direct-connection follow-up also passed `dotnet build GlassDock.sln -c Debug --no-restore` with 0 warnings and 0 errors. Existing failure/retry tests were updated for removal of Settings state, and a missing-container safety test was added. These tests were compiled but not run. Actual headphone driver reconnect/disconnect, BLE input devices, unsupported classic HID failures and cancellation during native requests still require manual hardware verification.

## Remaining manual checks

1. Enable Bluetooth in Utilities; compare circular placement, hover isolation and Off material with Wi-Fi across appearance modes.
2. Check supported/denied/unavailable radio toggles, repeated Off right-click, long-press, Escape and outside-click dismissal.
3. With paired devices, verify real statuses, names, device icons and available battery icons; ensure unpaired nearby devices never appear.
4. With more than five devices, exercise wheel, touchpad and thumb dragging, scrollbar fading, edge effects and every expanded action. Check one-row expansion, rapid reversals and reduced motion.
5. Try Bluetooth headphones through their audio driver and BLE mouse/keyboard/controller connections. Verify actual Windows state, unreachable/unsupported failures, timeouts, duplicate clicks, retry, radio-off during an operation, and external connection/removal/battery changes. Confirm that no device interaction opens Settings.
6. Confirm removal cancellation first; use a disposable paired device for explicitly confirmed unpairing.
7. Reopen panels, switch monitors, remove/re-add the utility, and check Wi-Fi, capture/snipping, recovery hotkeys and dock behavior.

API references: [GATT client](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client), [device enumeration](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/enumerate-devices), [AEP connection state](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-aep-isconnected), [Bluetooth audio reconnect](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/ksproperty-oneshot-reconnect), [Bluetooth audio property set](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/kspropsetid-btaudio), [IKsControl](https://learn.microsoft.com/en-us/windows/win32/coreaudio/using-the-ikscontrol-interface-to-access-audio-properties), [BluetoothSetServiceState semantics](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothapis/nf-bluetoothapis-bluetoothsetservicestate).

## Rapid radio-toggle crash follow-up

The user reports a crash from rapidly left-clicking Bluetooth ON/OFF. Available development-error/startup logs and Application Error entries predate this report; they do not establish its exact exception or fault location. Hardware toggling was not reproduced by the agent.

Source tracing identified a new resource-churn path when Frosted/Acrylic OFF buttons use the native dock backdrop: turning the last radio utility ON starts shared WGC, and OFF stops it. Capture stop previously disposed utility composition drawing targets; native OFF mask coverage also disposed/recreated its visuals. Utility-only WGC additionally attached and detached a main-dock CanvasSwapChainPanel that it never rendered. Rapid toggling could repeatedly tear down compositor objects still referenced by the view.

Capture stop now hides utility targets; the owning button/panel disposes them when its lifetime ends. A real CanvasDevice change still resets incompatible resources. Native masks are hidden and reused until backdrop disconnection. The main swap chain is created only when the Clear dock itself needs it, not for utility-only rendering. Monitor frame pools and their pooled bitmap references still stop/release in the existing safe order.

The Bluetooth service keeps its shared gate and Busy state until actual radio states reach the requested state, not merely until SetStateAsync acknowledges the request. A 10-second confirmation timeout and lifetime cancellation release the operation cleanly. Temporary state observers detach in finally, including partial subscription failure, and native errors during recovery cannot escape the intended error-reporting path.

This is a source-identified crash-risk correction, not a hardware-verified diagnosis. `git diff --check` passed; no build, test or runtime operation was performed. Manual acceptance: rapid left-clicks during and after each confirmed state change; test one utility and both utilities in Frosted/Acrylic and Clear, with a panel open, then adapter removal/permission failure and app shutdown during a toggle. Confirm one outstanding radio request, correct final Windows state and no lingering Busy state.

## Shared Wi-Fi/Bluetooth toggle transition

Both directions now reuse the existing 180-ms cubic symbol pulse, starting synchronously on click while Windows runs asynchronously. The displayed state stays held until the lead animation and native request finish, then an opacity pulse settles into the actual radio state. Reduced motion skips the animations. Pointer/keyboard repeat activation is suppressed, and the shared service gate remains held through presentation completion so requests cannot queue across monitors.

Both services use actual radio-state confirmation and a 10-second native-operation timeout, including access and enumeration. Failure recovery reads Windows state; it does not assume the requested state succeeded. Animation disposal resolves pending completion, and cleanup releases the gate even when presentation fails. Frosted/Acrylic retain the native OFF underlay until the first ON capture frame is ready.

Validation: `dotnet build GlassDock.sln -c Debug --no-restore` succeeded with 0 warnings and 0 errors. No tests, app launch or hardware toggles were run. Manual checks remain: rapid clicks in both directions for both utilities, reduced motion, denied/unavailable adapters, timeout recovery, multi-monitor clicks, closing/removing a utility during a transition, and unchanged glass across appearance modes.
