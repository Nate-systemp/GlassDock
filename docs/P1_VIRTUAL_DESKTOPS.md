# P1 virtual desktop awareness

## Corrected trigger: desktop removal

The user clarified that the reproduction is create Desktop 2 → Task View → close Desktop 2, rather than ordinary switching. Logs were retrieved again: the newest available development-error.log and Application/WER events still describe the same 2026-10-10 12:09:32 preview index exception below. No newer post-patch failure, COM exception, or desktop-deletion dump was available. The precise deletion event interleaving remains unverified; do not interpret the WER combase bucket as proof of a membership-query crash.

Pipeline audit: foreground/show/hide/cloak WinEvents signal the existing discovery worker. It serially enumerates windows and creates a fresh apartment-local membership query each time; no desktop GUID is cached. Windows moved from a deleted desktop are therefore queried again on the next event-driven pass, or the existing three-second fallback. COM failure/invalid HWND handling remains as tested below. Complete snapshots pass through pin/stack grouping to the UI, which updates indicators and then preview sessions. The keyboard service does not handle desktop deletion or invoke membership COM; its existing HWND eligibility guard remains unchanged.

Additional correction: `ApplicationSnapshotPump` replaces the view-model's one-dispatch-per-snapshot queue. It keeps only the newest pending snapshot, serializes the entire apply/event-handler operation even under nested UI message pumping, and drops queued updates after disposal. Filter refreshes reuse the newest snapshot rather than racing a stale captured snapshot. This closes an independently reproducible reentrant collection-update hazard without disabling membership tracking, recreating surfaces, hiding Doky or restarting anything.

Files changed in this deletion follow-up:
- src/GlassDock.Core/Applications/ApplicationSnapshotPump.cs
- src/GlassDock.App/ViewModels/DockApplicationsViewModel.cs
- tests/GlassDock.Core.Tests/DesktopRemovalTests.cs
- docs/P1_VIRTUAL_DESKTOPS.md

Four new tests cover 40 simulated removals/migrations with pin identity preserved, preview geometry/page consistency, nested collection-change refresh, 200 concurrent publications, disposal, and dispatcher rejection/retry. Existing real-HWND destruction, COM failure/recovery and enumeration-race tests are also rerun. Simulation cannot verify Shell desktop deletion.

Deletion follow-up validation: Debug and Release builds succeeded, zero warnings/errors. Complete suites in both configurations: 366 Core passed, 185 Windows passed, one unchanged menu-radius assertion failed (551 passed total). An initial Debug test started before the build finished copying recovery dependencies and also failed their hash check; rerunning after the completed build resolved that test without code/assertion changes. scripts/Validate.ps1 remains failed solely on the documented unrelated menu assertion.

Required manual acceptance (still pending): repeat at least 20 times: Win+Ctrl+D → open Chrome/Explorer/VS Code windows → Win+Tab → close that desktop with X → verify the original Doky PID survives and moved windows, pins, indicators and previews are correct. Also close the desktop containing Doky, repeat with a preview open and Pin Dock enabled, and exercise rapid operations. Native desktop UI testing is unavailable in this session. P0 remains open until this passes; no claim of a verified runtime fix.

## P0 regression investigation — 2026-10-10

Status: targeted correction implemented; actual 20-cycle desktop-switch acceptance test remains pending. The desktop crash was not interactively reproduced here. No native desktop-control surface is available in this session, and Doky was not automatically restarted.

Evidence retrieved:
- `src/GlassDock.App/bin/Debug/net10.0-windows10.0.26100.0/win-x64/development-error.log`, modified 12:09:32 local time, records `System.ArgumentOutOfRangeException` (parameter `index`), `System.SZArrayHelper.get_Item`, `WindowPreviewWindow.Draw():447`, then the constructor's `SizeChanged` callback at line 117.
- Windows Application event 1000 at exactly 12:09:32 records GlassDock.App.exe PID 0x30F4, Microsoft.UI.Xaml.dll, exception 0xc000027b. Matching WER event 1001 at 12:09:38 reports underlying 8000000b. Report ID: c2d52acc-64fc-4b52-87a9-a8d1fb0362b4. The archive retains Report.wer; no matching minidump was available in WER Temp.
- This proves an out-of-range preview layout access in the UI callback, not a membership COM exception. The previous integration increased membership churn on desktop switches. Source inspection found rebuilds reading the mutable session separately for layout counts and actual cards around reentrant native/XAML calls. Exact event interleaving was not captured in a debugger.

Correction:
- Capture one membership list for both endpoint layouts and cards using `WindowPreviewFrame`. Clamp stale page offsets against that snapshot.
- Defer refresh/rebuild while drawing or rebuilding. Draw from a captured card/layout set; invalidate a drawing frame if its resources are cleared. No catch-and-ignore of the out-of-range exception and no forced dock hiding/restart.
- Queue click-time launch/activation discovery on the existing discovery worker, coalescing same-app requests. Both ordinary discovery and click rechecks now query virtual desktops on that worker, never on the UI thread. Returning true means the request was accepted, not that Windows ultimately granted activation. Direct per-window activation remains unchanged.
- Validate HWND both before and after membership COM calls. COM/disconnected-object failures return unknown and preserve the existing fallback. COM ownership remains scoped to an enumeration on its originating apartment.
- Tests cover 200 simulated membership/page transitions, immutable render geometry, destroyed HWNDs, COM failure/recovery, concurrent real enumeration/refresh, and refresh/shutdown. These are not substitutes for physical desktop switching.

Files changed for this regression: `src/GlassDock.App/Desktop/WindowPreviewWindow.cs`, `src/GlassDock.Core/Applications/WindowPreviewFrame.cs`, `src/GlassDock.Windows/Applications/WindowsApplicationService.cs`, `src/GlassDock.Windows/Applications/WindowsVirtualDesktopQuery.cs`, `tests/GlassDock.Core.Tests/WindowPreviewFrameTests.cs`, `tests/GlassDock.Windows.Tests/VirtualDesktopQueryTests.cs`, and this document. All eight original integration files were inspected; policy, app metadata, keyboard/helper eligibility and existing desktop-policy tests otherwise remain unchanged. Screenshot protection, pinned placement, keyboard hooks and watchdog are not modified.

Separate menu test investigation: the test asserts four unscaled literal constants and unscaled row padding; the current pre-existing menu source intentionally multiplies geometry by MenuSizeScale=0.85. This source-text expectation conflicts with the earlier requested compact menu. Neither production menu geometry nor assertions are changed in this crash patch.

Manual acceptance still required: launch the rebuilt Debug executable normally; put Chrome, File Explorer and VS Code on separate desktops; perform at least 20 Desktop 1 → 2 → 1 cycles, including rapid switches. Repeat with a preview open, multiple preview pages, Pin Dock, current/all-desktop taskbar settings, and an elevated window. Verify the same PID stays running, pins/order remain, previews and indicators refresh, Win shortcuts work, and no new development-error/WER event appears. Test Chrome profile separation and no duplicate launch when only an off-desktop window exists. Do not declare P0 closed until this passes.

Final regression validation: Debug and Release solution builds passed with zero warnings/errors. Each full suite: 362 Core passed; 185 Windows passed, 1 unrelated menu-source assertion failed (547 passed total). All new/updated regression cases passed. `scripts/Validate.ps1` remains red only because of that unchanged menu test. No commit, push or release.

## Original integration audit (historical; regression notes above supersede click-time and validation details)

- DONE (existing code): window identity/PID lifetime validation, foreground MRU selection, activate/restore/minimize, pinned apps/stacks, snapshot-driven previews, monitor filtering, keyboard/helper dispatch.
- PARTIAL: browser profiles remain separate when Windows supplies distinct AUMIDs; executable-only identity cannot reliably distinguish profiles. This implementation preserves the existing matching rules.
- NOT IMPLEMENTED before this change: explicit virtual desktop membership or taskbar preference filtering. WindowsApplicationService discarded every DWM-cloaked window. An app on another desktop could consequently be treated as closed during the launch recheck.
- Runtime validation of desktop switching, elevated apps, games and keyboard combinations remains pending; source inspection is not a manual pass.

## Implementation

WindowsApplicationService enumerates supported IVirtualDesktopManager membership using an apartment-local, disposable COM query. Valid shell-cloaked windows on another desktop remain known, while app/inherited-cloaked windows are excluded. Each snapshot filters running windows before combining them with pins and applying existing stack/order logic. All-desktop visible windows remain included, even if their assigned home desktop differs. Existing view-model reconciliation and preview refresh are reused; no surfaces or animations are recreated here.

Explorer's VirtualDesktopTaskbarFilter preference is read-only, best effort: 0 shows all desktops; missing, inaccessible or unknown values select current desktop. This registry value is not a documented API contract. The public membership API is documented at https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager . No private COM interface is used; MoveWindowToDesktop is only declared to match the COM vtable and is never called.

Foreground, show/hide, cloak/uncloak and desktop-switch WinEvents request reconciliation. EVENT_SYSTEM_DESKTOPSWITCH is a hint, not a guaranteed virtual desktop subscription. The existing three-second fallback handles missed events, preference changes, desktop removal and Explorer recovery. A signal arriving during coalescing is no longer erased. No timer or keyboard hook is added. COM instances are released after each enumeration and retried on the next pass.

LaunchOrActivate's existing fresh enumeration now sees other-desktop windows too, preventing an automatic duplicate launch when only remote windows exist. Existing live HWND/cloak validation rejects interaction with unavailable desktop windows. Elevated restore now shares that validation so a rejected ordinary request cannot bypass it through the helper. Explicit New window/Open actions remain explicit launches. No desktop switch or window move is attempted. Cross-desktop selection currently fails safely using the existing failure status; use Task View to switch first.

## Changed files for this task

- src/GlassDock.Core/Applications/DockApplication.cs
- src/GlassDock.Core/Applications/VirtualDesktopPolicy.cs
- src/GlassDock.Windows/Applications/WindowsApplicationService.cs
- src/GlassDock.Windows/Applications/WindowsVirtualDesktopQuery.cs
- src/GlassDock.Windows/Desktop/WindowsKeyboardService.cs
- tests/GlassDock.Core.Tests/VirtualDesktopTests.cs
- tests/GlassDock.Windows.Tests/VirtualDesktopQueryTests.cs
- docs/P1_VIRTUAL_DESKTOPS.md

Existing uncommitted files and unrelated changes are preserved. No commit or push.

## Limits and manual checklist

Validation on this checkout: Debug and Release builds succeeded with zero warnings/errors. Both configurations: Core 360 passed; Windows 181 passed, 1 failed. All 12 new virtual desktop cases pass, including a real Windows COM membership query, invalid HWND rejection and query recreation. The remaining failure is the unrelated pre-existing DockAppMenuWiringTests.GlassAppMenuMatchesSilverJumpListReferenceWithoutChangingSolidThemes assertion expecting `MenuCornerRadius = 9;`, while the current user-modified menu uses `9 * MenuSizeScale`. Neither that menu nor its assertion was changed for this task. scripts/Validate.ps1 therefore reports failure at the Windows suite. No desktop switching was performed by the smoke test.

The documented interface cannot enumerate/switch virtual desktops, subscribe to their lifecycle, or programmatically pin Doky to every desktop. If Windows hides Doky's own window during switching, use Task View → Show this window on all desktops; automatic cross-desktop presence is not implemented. Off-desktop thumbnail content may be stale/unavailable. API failure falls back to the former uncloaked-window discovery behavior; duplicate protection for undiscoverable windows cannot be guaranteed. The pre-existing synchronous click-time enumeration is retained and can still be delayed by Shell/COM. Refresh is eventually consistent, not an atomic transaction during Windows' transition animation.

1. Create two desktops in Task View. Put two windows of one app on different desktops, plus one different app on each; keep a pinned closed app and a stack.
2. Set Windows Settings → System → Multitasking → Desktops → taskbar windows to current desktop. Switch with Win+Ctrl+Left/Right repeatedly. Pins/order remain; indicators and previews follow the active desktop (allow up to approximately 3.2 seconds if no event arrives).
3. Move a window using Task View, close a window, remove a desktop. Check indicators/previews and stack members refresh without duplicate items or animation resets.
4. Select all desktops in Windows' taskbar preference. Verify both windows appear; off-desktop selection must not move a window or start a duplicate. Switch with Task View to activate it. Marking an app window Show on all desktops should keep it represented everywhere.
5. In current-only mode click a pinned app whose sole window is elsewhere: no duplicate launch. Explicit New window remains available. Verify local active-window minimize, minimized restore, MRU and separate Chrome profile identities.
6. Exercise Win, Win+T, Win+1–9, Win+Tab, Win+L, Win+Shift+S and desktop switching; test an elevated app. Check helper/watchdog remain healthy.
7. Repeat across monitors/DPI settings, sleep/wake and a user-initiated Explorer restart. Do not kill Explorer as an automated test. Check fullscreen games and all five appearances, stacks, drag lens and screenshot visibility.

Actual desktop switching, all-desktop pinning, gameplay, multi-monitor, sleep/wake and elevated interaction are not claimed verified by the automated API smoke test.
