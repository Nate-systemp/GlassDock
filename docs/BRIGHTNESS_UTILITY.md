# Brightness utility

Implemented 2026-11-10. Add **Brightness** in Settings > Add Utilities. Its circular Phosphor Fill sun button opens the hardware-brightness capsule; right-click/hold opens a compact monitor selector. The default is **Follow dock display**. Choosing another supported display is a session-only override; disconnecting it falls back to the dock display. Unsupported displays remain explicitly marked and cannot be adjusted.

## Shared HUD

`UtilityHudWindow` is the extracted existing Sound HUD, not another slider implementation. Both utilities use its 200×36 DIP expanded capsule, 200×6 DIP bottom-anchored compact bar, smooth fill, pointer dragging, automation peer, 1.5-second idle delay, spring entrance and scale-to-zero exit. The Sound branch retains the original icon selection, dimensions, appearance materials, timings and animation paths. Brightness supplies the existing filled sun asset and actual hardware percent through small read/write delegates. `VolumeHudState` and `VolumeHudPlacement` remain the shared state/geometry implementations without duplicated animation or positioning logic.

Both windows use the original Liquid Glass, nonactivating placement, work-area/DPI fitting and capture/screenshot handling. Their utility-specific anchors prefer their pinned button, otherwise the dock. The coordinator hides the competing HUD before opening another and leaves an interacting HUD in place when another utility changes externally. A brightness drag retains its display identity so an unrelated monitor event cannot redirect the write midway. No dock/utility geometry is resized for either HUD.

## Windows services

`BrightnessService` owns one background MTA worker. `BrightnessHardware` owns all WMI objects, physical-monitor handles and native I/O on that worker. The UI receives immutable readback snapshots and queues bounded 0–100 requests; it never performs monitor I/O.

- Built-in displays: Microsoft's `System.Management` 10.0.0 queries active `WmiMonitorBrightness` / `WmiMonitorBrightnessMethods` instances in `root\wmi`. WMI instance names are matched to the actual monitor device-interface paths returned by `EnumDisplayDevices`, not blindly assigned to the primary screen. Hardware-supported levels are respected. The local provider reports a Boolean method result; Boolean and numeric status-code providers are handled distinctly. Missing output never invents a brightness value: actual hardware readback remains required.
- External displays: `GetPhysicalMonitorsFromHMONITOR`, `GetMonitorBrightness` and `SetMonitorBrightness` use the display's physical DDC/CI endpoint. The monitor's actual minimum/current/maximum range is validated and normalized to 0–100%; a zero hardware minimum is not assumed. Physical handles are released on rediscovery/shutdown. No overlay dimming, gamma ramp, contrast substitution or emulated success is used.
- Drag writes: latest value is coalesced per target. Successive native write operations are separated by at least 120 ms after the prior operation completes, so there are no more than approximately 8 writes/sec, often fewer for slow firmware. A DDC controller may briefly NACK readback after a write; up to three 80 ms read-only retries run on the hardware worker. Setters are not blindly repeated.
- External changes: `WmiMonitorBrightnessEvent` requests immediate state refresh when supported. A 2-second hardware/topology poll detects external DDC/CI adjustments and provides a fallback for absent WMI events. Full capability rediscovery also occurs every 30 seconds. New/disconnected interfaces purge stale pending writes. Query/read failures are published as unavailable, and topology failures are retried without killing the service.
- Self-originated writes update the fill without starting a second HUD burst; a short settling window suppresses their echoed notifications. Physical/OEM brightness keys retain their Windows behavior, and actual resulting changes open the HUD even when unpinned, where the display exposes those changes to WMI or DDC readback.

The only new package is Microsoft's WMI library and its transitive `System.CodeDom` dependency. This is recorded in the Windows project lock file and architecture gate. Core remains free of Windows/package dependencies. Taskbar, watchdog, native volume suppression and the protected elevated input-helper protocol are unchanged.

## Hardware verification

A temporary ignored console probe ran the production service and physically wrote/read/restored both detected displays on this machine:

| Display | Backend | Verified readback sequence |
| --- | --- | --- |
| Built-in `NCP004D` panel | WMI | 90 → 95 → 90% |
| Lenovo `LEN T2254pC` / `LEN60CC` | DDC/CI | 100 → 95 → 100% |

This proves that both hardware interfaces accept the operations and report the changed/restored values. It is not a photometric measurement and does not establish support for every display/adapter/dock combination. The probe also exposed the Boolean WMI return convention and the external monitor's post-write read NACK; both were corrected and the full sequences rerun without transient unavailable states.

Full Release solution build: **0 warnings, 0 errors**. `scripts/Validate.ps1`: **447 Core tests passed; 320 Windows tests passed; 1 existing unrelated failure** in `DockStackWiringTests.PopupUsesSharedServicesAndDismissalPaths`, whose source-string assertion still expects the older `LaunchOrActivate(app)` spelling. The test and stack launch code were not changed here. The 10 new brightness cases cover range validation, nonzero minima, WMI return conventions/quantization, identity mapping, coalescing/disconnect cleanup and passive real discovery. Existing shared HUD state/placement tests passed in the full Core suite.

Integrated HUD/physical-key test results are recorded below when observed.

## Limits

DDC/CI must be supported and enabled in the monitor; HDR, display drivers, adapters, USB/DisplayLink paths, virtual displays, remote sessions and clone configurations can restrict or change availability. WMI requires a matching active provider. Some physical keys alter firmware state without an observable brightness change; no fake HUD update is generated for those keys or at unchanged endpoints. DDC external-change detection can lag approximately 2 seconds plus native I/O time.

WMI operations have 2-second options. The native DDC API has no cancellable per-call timeout; a faulty driver can block the background worker. Shutdown never joins that worker on the UI thread; resources release when the call returns or the process exits. Unknown/unsupported displays are not controlled via guessed endpoints.

Windows' native **brightness** OSD is not suppressed by this feature. The existing volume-only suppression stays scoped to volume. Other Windows builds, HDR switching, hot-unplug during dragging, mixed DPI and all appearance modes still require manual acceptance beyond compilation and shared component tests.

Sources: [WmiMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbrightness), [WmiMonitorBrightnessMethods](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbrightnessmethods), [WmiMonitorBrightnessEvent](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbrightnessevent), [GetPhysicalMonitorsFromHMONITOR](https://learn.microsoft.com/en-us/windows/win32/api/physicalmonitorenumerationapi/nf-physicalmonitorenumerationapi-getphysicalmonitorsfromhmonitor), [GetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-getmonitorbrightness), [SetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorbrightness), [EnumDisplayDevicesW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw).

## Integrated checks and drag-latency refinement

The user tested the rebuilt app with unpinned physical brightness keys and confirmed that the sun HUD appears. The user also confirmed pinned dragging and selecting both monitors work, with the Sound/Brightness HUDs staying separate. The first feedback was that the fill felt delayed while dragging.

The cause was coupling the visual gesture to throttled hardware readback plus the existing 110 ms fill transition. Brightness now previews the pointer's requested level immediately during a drag, while the hardware service still coalesces and throttles real writes. Older in-flight readbacks do not pull the fill backward. Per-display request revisions keep the last gesture pending until that exact/latest operation has completed; release then reconciles the fill to the actual readback, including hardware quantization or failure. A pending preview is explicitly described as requested/applying in accessibility status and is never published as confirmed hardware state. This interaction preview changes no brightness through overlays and does not claim a successful setter. Sound keeps its original drag path and animation logic.

After rebuilding/restarting, the user tested both monitors and confirmed **"Dragging is responsive now."** The final Debug build had **0 warnings, 0 errors**. **36 focused brightness, Core Audio, volume-suppression and input-helper tests passed**, including the new final-request-revision regression case (11 brightness cases total). The earlier complete validation still has the one unrelated Stack source-string assertion failure described above.

## Native brightness OSD blocker on this machine

The user correctly observed that Windows' native brightness OSD still accompanies physical keys. This feature has not hidden it.

A temporary read-only diagnostic received 15 Shell brightness notifications (`SHELLHOOK` kind 55) without a corresponding known function/volume virtual-key event. A second trace counted every keyboard down/up without recording typed key values. Across five brightness notifications it recorded **`keyEvents=0`**, while actual WMI brightness changed after each notification. The same live hook subsequently counted **34 ordinary keyboard events**, and the foreground chat processes were verified unelevated with `UIAccess=0`; this was not the earlier Task Manager privilege mismatch.

Thus this hardware's brightness path cannot be replaced with the verified volume-VK interception mechanism. The observed Shell notification is not a documented pre-show veto. The native host is shared with other indicators; retaining a foreign-window region/style/minimization mutation would require a separate independently tested recovery design and would not establish first-frame protection when Windows recreates the host. Post-show hiding already failed the no-flash requirement in the volume investigation. No delayed hiding, shell patch, registry setting or speculative keyboard remapping was added here.

A reliable native-brightness-OSD suppression implementation remains blocked on a verified pre-presentation Shell/driver interface or another safe supported mechanism for this non-keyboard path. Current brightness hardware control and custom HUD are functional; native OSD suppression is explicitly incomplete.

A subsequent [pre-display suppression investigation](BRIGHTNESS_OSD_SUPPRESSION_RESEARCH.md) found a concrete Windhawk `ShowWindow`/`SetWindowPos` interception approach inside Explorer. It is not a pure out-of-process C# solution and introduces an injection/API-hook scope decision under the user's earlier constraints. No such bridge has been installed or enabled; the native brightness OSD remains unsuppressed.
