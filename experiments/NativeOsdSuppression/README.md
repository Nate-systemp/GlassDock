# Isolated native OSD suppression proof of concept

Status: investigation/fixture prototype only. **No Explorer injection is authorized or performed.** Nothing here is referenced by GlassDock.sln or Doky's production code. Do not install the optional Windhawk source into Explorer.

The adapter now contains a prepared, default-off **read-only diagnostic**; see [READ_ONLY_DIAGNOSTIC.md](READ_ONLY_DIAGNOSTIC.md) for its exact capture bounds, version gates, build limits and proposed separately approved procedure. Its hooks forward unchanged and cannot arm suppression. Earlier fixture suppression results below refer to the separate policy lab, not the adapter's observation hooks. The adapter has been syntax-checked using a signature shim, not built/loaded with the real Windhawk SDK.

## Architecture before execution

The inspected Windhawk v1.2.0 source intercepts `ShowWindow` and `SetWindowPos` in the calling Explorer process. A matching show is declined or converted from `SWP_SHOWWINDOW` to `SWP_HIDEWINDOW` before forwarding. ShowWindow's BOOL is previous visibility, not proof that a show succeeded. Its classifier is too broad for Doky: the upstream readme admits airplane-mode indicators can be hidden.

The experiment first tests the process boundary using **owned fixture windows only**:

- `NativeOsdLab.exe --external-test`: a controller interposes its own imports of the two APIs. Its child producer creates a small fixture indicator and invokes the original APIs in its own process. Interposition is first validated on controller-originated calls to that same target HWND. The child then invokes both APIs itself. Read-only, producer-PID-scoped WinEvent SHOW observation records whether it was already visible. No external hide/minimize/region/style mutation is used.
- `NativeOsdLab.exe --policy-test`: the same hook/policy code runs in an owned fixture host. Volume and brightness fixture intents may be suppressed; unknown/airplane/Quick Settings/notification/snap/other windows must pass. Explicit opt-in, replacement readiness, identity, intent, a bounded lease and both bridge-owner/Doky-owner process lifetimes are mandatory. The two lifetime actors are disposable child processes of this test, not Doky. Their forced exits model crashes while hooks remain installed in the host.
- `explorer-prototype.wh.cpp`: **separate, disabled-by-default, unexecuted** Windhawk adapter source. It is not a launcher/installer. It must not be loaded into Explorer until additional approval and a verified native-content intent provider exist. Default unknown intent always passes through. The fixture property used in lab tests is never treated as a real Shell discriminator.

The local import-table interposition is real Win32 interception in the controller/fixture executable, without modifying Windows DLL code or files. It deliberately does not claim to be Windhawk's inline-detour engine, nor to prove all possible external mechanisms impossible. It tests whether equivalent caller-side ShowWindow/SetWindowPos decisions propagate to another address space. An out-of-context event callback has no return-value veto. No shared native Shell window is persistently clipped, hidden or minimized in this experiment.

## Fail-open contract

The decision requires a live bridge-owner process and live Doky-owner process, each verified by PID, creation time and same session, plus an unexpired lease. The hook does not wait: zero-time process checks and a try-acquired shared lock only. Disable/expired lease/dead owner/contended configuration/reentrant callback/missing identity or intent returns to the original API. The lock prevents handles being closed while a hook uses them. Configuration/disposal run outside hooks. No COM, WMI, disk logging, synchronous IPC or waits for UI run in hooks.

No foreign-window mutation is retained, so fail-open means the **next** Windows show follows its original path. It does not resurrect an already expired popup. A crash between the live-process check and returning from a hook can affect one racing request; the next request must pass. Future injected integration must keep hooks/trampolines valid until in-flight callbacks finish; disabling the policy must precede unload.

Explorer restart invalidates the expected shell PID/creation time, learned HWNDs and outstanding intent records. A future adapter must start disabled in the new process, reacquire identity/capability and receive a fresh lease; it must not reuse old HWND/PID signatures or auto-arm merely because the class name reappears. This experiment does not restart or terminate Explorer.

## Native intent blocker

Host identity is necessary but insufficient. The real native volume/brightness and other hardware indicators can share a host. A recent brightness change, the absence of a volume event, a small window, a thread name or band 18 alone is not proof that a show request is brightness. Zero/max brightness presses may not change WMI state. A Shell integration must therefore provide an exact, validated volume/brightness intent tied to the show operation, or fail open. No such provider is claimed to exist in this prototype; broad upstream matching is intentionally not copied.

## Safety and test order

1. Read this architecture and source. Build/run only the lab's own-process and child-process fixture modes, unelevated.
2. Inspect logs for all policy guards, both APIs, external-process bypass, lease expiry, explicit disable, both owner crashes and reentrancy.
3. If desired, perform a separate **read-only** trace of the actual Shell host. That can verify identity/event delivery, not a first-frame veto or content classification.
4. Before any injection: obtain separate approval, review native DLL/framework provenance and license, arrange an independent disable/unload route and a process-lifetime gate. A verified native-intent provider is mandatory before arming suppression, not for an explicitly approved observation-only investigation. Keep Doky's production HUDs out of the experiment.
5. Only after approval, begin with injected observation-only mode. Then narrowly arm volume/brightness; test first/repeated/held/0/100% and elevated foreground, Quick Settings, notifications, airplane mode, snap/Task View, disable, bridge kill, Doky kill and a separately user-initiated Shell recreation. Record video/visual evidence before claiming no rendered frame. Do not cause an Explorer restart to test it.

No Windows binary, registry policy, persistent Shell preference, taskbar/watchdog setting or production build output may be changed by these tests. A successful fixture test does **not** prove real native OSD suppression or Shell stability. The safest shipping posture remains fail open until native intent discrimination and approved runtime validation pass.

Reference: https://github.com/ramensoftware/windhawk-mods/blob/main/mods/win10-volume-brightness-osd.wh.cpp

The optional Windhawk adapter is provided as source only. The build script exports a self-contained `.wh.cpp` under the ignored artifacts directory; **it does not compile, load or install a Windhawk module**. Its opt-in setting is false. Even after opt-in, the arm contract returns `ERROR_NOT_SUPPORTED` because an authenticated readiness transport and exact native-content intent provider have not been implemented or verified. This is an explicit capability lock, not a functioning Windows OSD suppressor. The shared hook/policy core is compiled/exercised by the native lab. Windhawk-specific ABI/link/unload behavior remains untested.

`--inspect-shell` is an optional read-only identity snapshot. The observed-build profile is restricted to OS build 26300 and requires the private read-only band/caption exports; all other profiles fail open. Even a matching native host has **unknown content intent**. HWND/PID values printed in a snapshot must never be hardcoded into an implementation.

## Reproduce the authorized lab stages

Run unelevated from the repository root:

```powershell
pwsh -NoProfile -File experiments/NativeOsdSuppression/Build.ps1 -RunFixtures
& artifacts/native-osd-poc/NativeOsdLab.exe --inspect-shell
```

Without `-RunFixtures`, the script only compiles the lab and exports the disabled adapter source. It does not install compilers, frameworks, modules, services or tasks. MSVC and Windows SDK are discovered from existing installations. All binaries, logs and exported single-file Windhawk source go under ignored `artifacts/native-osd-poc/`. Never import the exported `.wh.cpp` into Windhawk without additional approval.

The lab's children are the exact same lab executable, created from its own resolved executable path. They are atomically assigned to a fixture-only, non-inheritable kill-on-close job at process creation. A controller crash cannot leave its fixture descendants running. Forced termination in tests uses only immutable handles returned by those owned CreateProcess calls; it cannot target arbitrary PIDs or Explorer.

Configuration epochs prevent an in-flight older arm operation from overriding a newer disable. Both expired-owner checks use nonblocking zero-time process waits, and configuration races use a try-acquired lock in hooks. The lease-expiry test waits for the monotonic clock to pass the deadline instead of assuming Sleep(10) spans a GetTickCount64 tick.

## Actual results, 2026-11-10

On Windows build **26300.9818**, the x64 native lab compiled successfully with MSVC `/W4 /WX /permissive- /std:c++20` and Windows SDK 10.0.26100.0; no compiler warnings or errors were emitted.

| Authorized test | Observed result |
| --- | --- |
| External-process equivalence | **15 checks passed.** Controller-originated ShowWindow and SetWindowPos calls to a child HWND were blocked. Calls originating in that child bypassed the controller interception and made the same fixture visible. Two read-only SHOW callbacks saw `IsWindowVisible=true`. |
| Policy/lifecycle fixtures | **56 checks passed.** Both intended fixture surfaces were suppressed before the original API. Unknown and unrelated intent, wrong owner/class/creation time, missing intent/readiness, disabled/expired lease and stale arm completion failed open. Reentrancy and configuration contention passed. Both API paths recovered after forced bridge-owner and Doky-owner actor exits. A recreated producer required fresh identity/readiness. Kernel job cleanup removed owned descendants after a controller crash. |
| Real Shell identity, read-only | One native host matched the observed class/band/composition-bridge profile. Its content intent stayed `Unknown`; no show/hide/style/region action was attempted. No Explorer module was loaded. |
| Production preservation | Tracked production diff and hashes of untracked production source files matched the pre-experiment baseline. The existing Doky process remained responsive. No lab processes remained after completion. |

The 71 fixture assertions are not 71 native-Windows-OSD tests. Airplane/Quick Settings/notification/snap cases were controlled decoys, not experiments on real Shell surfaces. No real native OSD was suppressed, no frame/video claim was made, and no injected framework ABI/unload path was exercised. The optional Windhawk adapter remains source-only, disabled and hard-blocked from arming without verified native intent/readiness integration. `scripts/Validate.ps1` was not rerun because this lab is outside the solution and no foundation/production code changed.

## Feasibility and remaining risk

**External-process result:** no equivalent pre-display veto was achieved. The tested local import-table interposition affects only the caller's executable; it cannot rewrite calls in another process. WinEvent SHOW notification observes state and supplies no cancellation return. This does not prove that every imaginable external/private mechanism is impossible, but it rules out exporting the researched hook decision into an ordinary out-of-process observer as an equivalent implementation.

**In-process result:** the pre-show decision works in the controlled host. To apply the researched Windhawk mechanism to Explorer's own calls, code must run/interpose in Explorer. That requires further approval and native/framework validation; it is not a pure C#/external-process solution.

**Safety blocker:** the shared host does not prove volume/brightness content. Until a genuine exact intent provider is verified, activation must stay blocked. Recent WMI changes and learned sizes/thread/band signatures are insufficient to guarantee preservation of other hardware indicators. The prepared arm entry explicitly returns `ERROR_NOT_SUPPORTED`, rather than pretending suppression is active.

An injected native bug can crash or destabilize the Shell despite a fail-open policy. Future Windows builds or alternative API paths can bypass these hooks. Engine-managed trampoline lifetime and in-flight callback quiescence must be verified before unloading. The fixture recovery guarantee concerns the next **API show call**; Explorer may cache internal flyout-visible state and not issue another show immediately after a physical key. Therefore first-key recovery after real bridge/Doky termination, existing-visible-host behavior and zero-frame rendering remain required actual Shell tests after approval. No registry/binary patch, persistent foreign-window mutation or timer-based closing workaround was used to conceal these gaps.

Safest next step: review this source and the disabled adapter. If separately approved, use **observation-only injection first**, still incapable of arming, to validate call paths, intent and framework unload on this exact build. Only after that evidence and the readiness/lifetime protocol are verified should a narrowly scoped suppression trial be proposed. Nothing in this repository initiates that injection.
