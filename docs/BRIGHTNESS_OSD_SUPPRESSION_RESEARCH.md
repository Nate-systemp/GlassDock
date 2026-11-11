# Native brightness OSD: pre-display suppression candidate

The subsequent opt-in implementation and exact PDB-based brightness identification are recorded in [BRIGHTNESS_SUPPRESSION.md](../experiments/NativeOsdSuppression/BRIGHTNESS_SUPPRESSION.md). The historical investigation below predates that implementation.

Research follow-up, 2026-11-10. No Doky implementation or live Shell state was changed during this investigation.

## Finding

A concrete source-level mechanism exists in the official Windhawk mod [Windows 8/10 Style Volume & Brightness OSD](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/win10-volume-brightness-osd.wh.cpp). The downloaded source declares version **1.2.0**, `@include explorer.exe`, x86-64. Its local SHA-256 is `AE8FC24436537BE03F94A5308F4187A1AD76CE2DC5B94DEC96E04949174DD411`; the main-branch URL can change. GitHub commit metadata queries timed out, so no commit hash is asserted.

The relevant functions in that inspected file are:

| Function / source line | Mechanism |
| --- | --- |
| `ShowWindow_Hook`, line 446 | Recognizes the native indicator before calling the original function and declines its show request. |
| `SetWindowPos_Hook`, line 456 | On a matching `SWP_SHOWWINDOW` request, removes that flag and adds `SWP_HIDEWINDOW` before forwarding to the real API. |
| `Wh_ModInit`, lines 1606–1609 | Registers both API hooks through `Wh_SetFunctionHook`. |
| `Wh_ModUninit`, line 1639 | Stops the mod's UI thread; Windhawk manages removal of its registered hooks during unload. |

This operates **inside Explorer**, before the show operation, rather than observing an already visible window. It therefore avoids the inherent out-of-context post-show hiding delay and does not depend on a brightness virtual-key event. It is a credible prototype direction for this machine's non-keyboard brightness path. Its actual first-frame behavior on this machine is **not yet verified**.

For comparison, the inspected MIT [Dynamic Island fork](https://github.com/Adiieeee/Fork-Dynamic-Island-Windhawk/blob/main/mod.wh.cpp) uses an out-of-context `EVENT_OBJECT_SHOW` callback and recent WMI changes to hide a presented window. Flylet minimizes/hides the native host/child. Neither is evidence of a pre-presentation veto for this task. The `IFlyoutDisplay` references found are used to show/realize the native OSD; they do not establish a documented suppression flag.

## Scope and safety differences

The Windhawk mod is not a drop-in Doky dependency: it draws its own replacement UI and captures volume keys, and its own readme warns that other hardware indicators, including airplane mode, share the suppressed host. Its classifier uses class/size, excludes multitasking and snap threads, and learns the indicator's thread/band signature. Copying its broad matching predicate would not establish brightness-only suppression or preservation of every other flyout.

A Doky experiment would need a **suppression-only native bridge**, leaving all existing Doky WMI/DDC, Core Audio, HUD and animation logic in the normal app. Required conditions before enabling it:

1. Opt-in, Explorer-only injection/API interception; no binary-file changes, Explorer termination, registry policy or generic notification suppression.
2. Strong current-Shell/HWND/band/bridge identity checks plus validated intent classification. Unknown windows/indicators fail open. The upstream airplane-mode side effect must not be silently accepted.
3. A verified Doky PID/process-lifetime gate: suppression is permitted only while the matching normal app is alive, opted in and its replacement brightness HUD/backend is available. After app exit/crash, subsequent show requests must pass through, even if the native module remains loaded. Normal mod disable/unload removes hooks. This gate is a proposed requirement, not an implemented or tested recovery guarantee.
4. No COM, WMI, synchronous pipe I/O or waits for Doky's UI inside the hooked APIs. Requests for an unchanged brightness level should notify Doky's existing HUD without inventing a hardware value.
5. Real first-press, rapid/held, 0/100%, Task Manager, normal-exit and forced-parent-exit tests; test unrelated flyouts and independently initiated Shell recreation without terminating Explorer as part of the experiment.

Injecting code/API hooks into Explorer introduces a Shell stability/security boundary absent from the current volume helper. A native bug could affect Explorer. It also conflicts with the user's earlier instruction **"Do not kill Explorer, patch Windows components, or make permanent registry changes."** The user also previously required **"If suppression requires a third-party utility or unsupported private mechanism, investigate and report it before integrating."** Accordingly no Windhawk installer/mod, injected DLL or API detour has been executed or added to Doky without resolving that scope explicitly.

## Licensing and verification

The mod has no explicit `@license` metadata. The [official collection README](https://github.com/ramensoftware/windhawk-mods#submitting-a-new-mod) states that submitted mods without a license are MIT. Any adapted source must retain the appropriate author/copyright/permission notice; this research copied no mod code into Doky. Windhawk engine/framework packaging must be evaluated separately from the individual mod's license.

References: [mod source](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/win10-volume-brightness-osd.wh.cpp), [Windhawk hook/unload API](https://github.com/ramensoftware/windhawk/wiki/Creating-a-new-mod), [Windhawk discussion of Shell stability from in-process mods](https://github.com/ramensoftware/windhawk/wiki/Mods-as-tools:-Running-mods-in-a-dedicated-process).

This turn performed source inspection and wrote this report only. No build was necessary for the documentation change. Doky's current hardware brightness controls and responsive dragging remain running; the native brightness popup is still present until a separately authorized prototype succeeds.

## Isolated proof of concept (2026-11-10)

The requested external-process and fixture-only investigation is recorded in [the isolated lab architecture and results](../experiments/NativeOsdSuppression/README.md). It passed 71 controlled fixture checks: external local API interception did not suppress producer-originated shows, while in-process policy interception worked with fail-open lifecycle guards. A read-only real Shell identity snapshot kept content unknown and performed no suppression. The separate opt-in Windhawk adapter is prepared as source only, disabled, and refuses to arm until a verified native-content/readiness integration exists. No Explorer injection or Doky production-code modification was performed; actual native OSD suppression remains unverified and unimplemented.

## Production integration decision (2026-11-10)

Follow-up preparation: the isolated adapter now has a bounded, default-off read-only pre-API diagnostic, documented in [READ_ONLY_DIAGNOSTIC.md](../experiments/NativeOsdSuppression/READ_ONLY_DIAGNOSTIC.md). It has not been loaded into Explorer. This adds evidence-collection capability, not precise native intent classification or suppression.

Source inspection confirms Sound suppression exists; it is not a Shell-window suppressor. `NativeVolumeOsdSuppression` consumes only `VK_VOLUME_MUTE`, `VK_VOLUME_DOWN` and `VK_VOLUME_UP` through `WH_KEYBOARD_LL`, invoking `MasterVolumeService.TryHandleVolumeKey` before Shell dispatch. `MonitorDockCoordinator.ConfigureVolumeSuppression` also configures the existing protected input-helper path for elevated foreground windows. Disabling/disposal releases handling; no foreign Shell window state is retained.

That key-capture mechanism cannot be extended to this machine's brightness path merely by adding a key constant. Existing `artifacts/brightness-key-all-events.log` records brightness-related Shell kind 55 events with `keyEvents=0`, followed by actual WMI changes. This is prior recorded hardware evidence, not a new physical-key test. WMI notification arrives after the hardware change and is not a cancellable pre-display request; it also cannot identify unchanged 0/100% keypresses reliably.

The inspected Windhawk `NativeOsdSeen` uses recent volume/brightness timestamps and a learned class/size/thread/band signature. It does not expose an exact brightness-only discriminator. Its `ShowWindow_Hook` and `SetWindowPos_Hook` must execute in the caller's process to veto that caller's shows. An external SHOW observer would reintroduce the first-frame race and does not meet the requested behavior.

Therefore no production toggle, hook or brightness-key replacement is added. The already prepared `experiments/NativeOsdSuppression/explorer-prototype.wh.cpp` remains the integration boundary: default off, `DokyOsdPrototypeArmV1` returns `ERROR_NOT_SUPPORTED`, and unknown native content passes through. Reusing its tested lifetime/lease/reentrancy policy is appropriate only after exact native intent and authenticated replacement-readiness transport are verified. Reusing Sound's volume-key capture for non-keyboard brightness events is not.

**Exact blockers:** separate approval is required before any Explorer injection, including observation-only; precise intent tied to the pre-show call is not yet available; framework unload/quiescence, real first-key crash recovery and first-frame behavior remain unverified. Approval alone does not resolve those technical blockers. The safe next experiment is separately approved observation-only instrumentation of the native call path, with suppression still incapable of arming. No installation, injector, persistent setting, production control or delayed-hide workaround is introduced here.

Verification for this integration review: `dotnet build GlassDock.sln -c Debug --artifacts-path C:\Dev\GlassDock\artifacts\brightness-osd-verification --nologo` succeeded with 0 warnings and 0 errors, using separate output rather than replacing the running app. The isolated native lab rebuilt and all 71 fixture checks passed again. `--inspect-shell` on build 26300 found one native host with unknown intent and explicitly reported `suppressionAttempted=false injectionAttempted=false`. `git diff --check` passed. These checks do not demonstrate native brightness-popup suppression, zero-frame behavior, new hardware-key behavior or actual Shell crash recovery. No production implementation changed in this review.
