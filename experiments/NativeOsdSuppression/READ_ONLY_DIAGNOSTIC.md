# Read-only Explorer diagnostic: prepared, not authorized to load

This extends the existing isolated Windhawk adapter. It is not integrated into Doky, has no injector/installer, and has not been loaded into Explorer. Preparing source does not authorize execution. Do not import/enable it without separate explicit approval. No suppression decision is enabled by this diagnostic.

## Scope and invariants

- Default `enablePrototype: false`. The active desktop Shell PID must equal this process. `RtlGetVersion` must report Windows major 10/build 26300; the running executable's version resource must be exactly **10.0.26100.9818**, the Explorer file observed on this OS build. This is a narrow version-resource gate, not an authenticity/hash check or compatibility guarantee. Other versions reject initialization.
- Records are taken before forwarding `ShowWindow` and `SetWindowPos`. Each call is forwarded exactly once with every argument, original return value and incoming/outgoing last-error behavior preserved. No show veto, rewritten flag, window mutation, keyboard interception, WMI write or suppression lease is used. `DokyOsdPrototypeArmV1` still returns `ERROR_NOT_SUPPORTED`.
- Collect for at most **60 seconds** and retain at most **128 records**. No automatic renewal. Settings changes stop capture and drain logs; a fresh explicitly enabled module load is needed for another capture. Timeout stops observation; the installed wrappers remain transparent until normal framework unload. No timer or worker thread is needed.
- Observe owned `XamlExplorerHostIslandWindow` candidates and API targets with such an ancestor, up to eight ancestor steps. This is a collection filter, **not** proof of OSD type. Unknown intent is never promoted to brightness.
- Capture API/arguments, monotonic time, thread ID, up to eight hierarchy nodes (parent/owner, PID/TID, class, band when available, styles, rectangle, prior visibility) and up to twelve raw stack addresses. Children are read in a bounded breadth-first walk; truncation is possible. No titles, accessibility text, process memory inspection, COM/UIA, symbol downloads, synchronous IPC or disk logging occur in the hook. Stack capture and Win32 reads still add overhead; no hard real-time latency guarantee is made.
- A try-acquired buffer lock and thread-local reentrancy guard drop observations instead of blocking or recursing. Full buffers drop excess records. Settings/unload drain via `Wh_Log` outside API hooks. Raw frames are historical; module names/bases are resolved at drain time and may differ after unload/reload. Logs contain HWNDs and addresses; treat as local diagnostic data.

The native capture reads pre-call state; it does not prove a window has never appeared before, that every reveal uses these APIs, or that classification will be available before the first rendered frame. Already-visible host reuse may produce no fresh show call. A descendant's content may not yet exist at this boundary. Native faults/framework errors can still destabilize Explorer despite transparent forwarding; fail-open policy is not process isolation.

## Build and owned-fixture verification

Run only this unelevated preparation command (no injection):

```powershell
pwsh -NoProfile -File experiments/NativeOsdSuppression/Build.ps1 -RunFixtures
```

`ObservationTests.exe` verifies disabled/default/expiry/stop behavior, bounded storage, contention/reentrancy, host/child context, unrelated-window pass-through and exact API forwarding. These tests use locally owned fixture windows and spy APIs, not Windows OSDs. The original external/policy fixtures remain separate.

`AdapterCompileCheck.cpp` supplies declaration-only Windhawk signatures and is compiled with `/W4 /WX /permissive- /std:c++20`, without linking. It checks C++ syntax, not real framework ABI, export handling, installation, hook activation/quiescence or unload. The exported self-contained source likewise is not a built or validated Windhawk module. Real Windhawk SDK compilation/provenance review must precede any approved load. In particular verify the framework applies hooks after `Wh_ModInit` initializes trampoline pointers and removes/quiesces hooks before module unload. Initialization failure must roll back any partially registered hooks.

Preparation verification on 2026-11-10: native lab and observation fixture builds plus adapter syntax check passed with warnings treated as errors. All observation fixtures and the existing 71 external/policy fixture assertions passed. This does not validate injected runtime behavior or a real SDK/module build. No full Doky rebuild is needed for this isolated-only change; the production code and solution references are unchanged.

## Proposed procedure after separate approval only

1. Review the exact exported source, installed framework provenance/license and framework lifecycle guarantees. Build with its actual SDK first. Confirm the OS/Explorer versions still match and normal framework disable/unload is independently reachable. Do not restart/terminate Explorer or change Windows settings to make the gate pass.
2. Load once with explicit opt-in and capture a timestamped **60-second** action timeline/video. Start from the native popup naturally hidden. Press brightness once, repeat/hold, and test unchanged 0/100% where practical. Log each deliberate action separately, not mixed rapid categories.
3. Collect volume and other Shell controls in separate labeled runs; the existing Sound suppression may consume volume keys before Shell, so no native-volume sample in that condition is expected. Any temporary Sound opt-out requires deliberate user selection and restoration. For airplane mode, use a separately approved user action because it changes radio state; never toggle it automatically. Include Quick Settings and a harmless notification as negative controls. This diagnostic itself changes none of those states.
4. At timeout, unload/disable normally to drain logs. Compare raw call stacks after normalizing stable module-relative addresses, target/ancestor identity, hierarchy and pre-call properties across labeled runs. Do not infer brightness from timing, absent volume events, rectangle or band alone. Retest multiple runs, including no-level-change presses. Preserve log/video timing so a pre-API sample is not mistaken for first-frame proof.
5. Review whether a stable brightness-specific request path/argument exists **before** both show APIs. If frames differ, inspect matching local symbols/source separately; avoid synchronous symbol resolution in hooks. If stacks/hierarchy are shared or not yet populated, this boundary cannot classify brightness safely: a validated upstream intent hook would be required, with separate review/approval.

## Current conclusion

Precise pre-display brightness identification is **not established**. The observation points can capture pre-API evidence, but the shared Shell host, recent WMI events and Windhawk's size/thread/band heuristic are insufficient. No injected diagnostic has run; there are no new real-Shell content results or no-flicker claims.

Brightness-only suppression would require a verified exact intent tied to each show request (including host reuse, rapid input and 0/100%), proven separation from volume/airplane/Quick Settings/notifications, a supported-build policy, authenticated live Doky/HUD readiness and bounded fail-open leases, validated hook lifetime/unload, and real disable/crash/first-frame tests after further approval. The existing isolated policy core can supply the lifecycle guards once classification is proven. Doky's current HUD/audio/brightness code remains untouched.
