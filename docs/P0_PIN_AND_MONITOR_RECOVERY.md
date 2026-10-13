# P0 pin visibility and monitor relocation repair

Validation date: 2026-10-13. No commit, push, tag or release performed. Existing unrelated working-tree changes were preserved.

## Status and evidence

**Bug 1: IMPLEMENTED — MANUAL TEST PENDING.** The user confirmed that the missing-target pin shows a question-mark tile and Unpin is available. Restart and actual Unpin/layout verification remain pending.

The source-confirmed empty-slot chain was: persisted pins are deliberately retained by DockPinStore; shell artwork can be null or transparent; DockApplicationCollection forwarded absent artwork; AdaptiveAppIcon cleared its image and returned without rendering anything while its button retained width. Initial null-reference equality checks could also skip the first icon update, including stack layers. This was an invisible retained pin, not evidence that the layout had failed to remove a deleted collection item.

The repair supplies shared visible fallback artwork in both the collection and icon control, including initial construction. Pin availability propagates through item updates and unavailable targets get a recovery tooltip. Explicit Unpin continues through the existing collection/layout/persistence path. Missing paths are not proof of permanent uninstall: pins are retained rather than silently deleted. Network, removable, inaccessible and packaged identities remain uncertain; local missing targets are marked unavailable. Shortcut resolution is bounded. No user files are deleted.

**Bug 2: PARTIALLY FIXED.** The user confirmed Utilities opens correctly in the updated Follow Pointer check. The exact original monitor-transition crash was not reproduced or captured, and a complete stress pass was not confirmed.

Windows Application events from October 12 reported Microsoft.UI.Xaml.dll / 0xc000027b. Corresponding available managed traces identified NullReferenceException in SystemQuickSettingsWindow.AddVerticalSlider, through BuildDetachedModules, SetDetachedLayout and OpenQuickSettings. These were older panel-construction failures; they do not establish the reported monitor-move crash cause. Current detached panel construction already differs from that failing version.

Inspection did establish unsafe relocation paths: the destination monitor was resolved twice, permitting validation and placement to use different monitors; positioning failures could escape the UI timer; overlapping/reentrant relocation and disposed native windows were not guarded; dock-owned popups were not dismissed before the host moved; floating placement assumed a display lookup always returned a valid work area.

The patch resolves the destination once, validates HWND/geometry/DPI, serializes relocation on the existing UI thread, skips stopped/disposed work, and dismisses dock-owned Utilities before relocation so they can reopen normally. Detached Utilities retain their independent anchoring. Floating placement checks disposal after callbacks and tolerates missing display information. Expected native, COM and invalid-operation failures are logged with the current stage, monitor and Utilities mode, with throttling; cached metrics are invalidated so the existing timer can retry. Programming exceptions such as NullReferenceException are not blanket-suppressed. No extra timer, input hook or helper process was added.

## Files changed for this repair

| Full path | Purpose |
| --- | --- |
| C:\Dev\GlassDock\src\GlassDock.Core\Applications\ApplicationIconFallback.cs | Shared visible fallback and artwork validation. |
| C:\Dev\GlassDock\src\GlassDock.Core\Applications\DockApplication.cs | Pin availability model. |
| C:\Dev\GlassDock\src\GlassDock.Core\Applications\DockApplicationCollection.cs | Fallback selection and availability propagation. |
| C:\Dev\GlassDock\src\GlassDock.Core\Applications\DockApplicationItem.cs | Notify availability changes. |
| C:\Dev\GlassDock\src\GlassDock.Core\Desktop\DockRelocationGuard.cs | Reentrant/stopped relocation guard and selective recovery. |
| C:\Dev\GlassDock\src\GlassDock.Windows\Applications\DockPinStore.cs | Probe availability without removing user pins. |
| C:\Dev\GlassDock\src\GlassDock.Windows\Applications\PinTargetProbe.cs | Conservative local/shortcut availability checks. |
| C:\Dev\GlassDock\src\GlassDock.Windows\Desktop\WindowsOverlayManager.cs | Single-target placement, lifetime/geometry validation and relocation guard. |
| C:\Dev\GlassDock\src\GlassDock.App\Controls\AdaptiveAppIcon.cs | Visible initial/null/transparent artwork, including stack layers. |
| C:\Dev\GlassDock\src\GlassDock.App\Desktop\DesktopOverlayWindow.cs | Recoverable staged relocation, popup lifecycle and unavailable-pin tooltip. |
| C:\Dev\GlassDock\src\GlassDock.App\Desktop\FloatingUtilitiesWindow.cs | Disposal checks and safe display lookup. |
| C:\Dev\GlassDock\tests\GlassDock.Core.Tests\MissingPinArtworkTests.cs | Artwork, availability, ordering and removal regression tests. |
| C:\Dev\GlassDock\tests\GlassDock.Core.Tests\DockRelocationGuardTests.cs | Reentry, repeated transitions, recovery, stop and programming-error behavior. |
| C:\Dev\GlassDock\tests\GlassDock.Windows.Tests\PinRecoveryTests.cs | Shortcut/target deletion, recovery, uncertain identities, persistence and stack removal. |
| C:\Dev\GlassDock\tests\GlassDock.Windows.Tests\MonitorRelocationLifetimeTests.cs | Invalid/disposed native host checks across monitor modes. |
| C:\Dev\GlassDock\docs\P0_PIN_AND_MONITOR_RECOVERY.md | This report. |

DesktopOverlayWindow.cs already had unrelated edits; FloatingUtilitiesWindow.cs was an existing untracked implementation. This list does not claim ownership of their earlier changes.

## Automated validation

- Debug and Release solution builds: passed, zero warnings and errors.
- Full suite in each configuration: 593 passed, 2 failed, 595 total. All 23 newly added cases pass.
- C:\Dev\GlassDock\scripts\Validate.ps1: restore and Release build passed; test stage failed on the same two existing assertions.
- git diff --check: passed (only existing line-ending notices).

Unchanged failing assertions:

1. C:\Dev\GlassDock\tests\GlassDock.Windows.Tests\DockAppMenuWiringTests.cs:63 expects the literal `MenuCornerRadius = 9;`; the existing implementation uses `9 * MenuSizeScale` (also present in HEAD).
2. C:\Dev\GlassDock\tests\GlassDock.Windows.Tests\DockAppearanceRuntimeWiringTests.cs:42 expects an appearance call using `dockAppearance`; pre-existing detached Utilities changes use `utilityMode`.

Neither test was weakened. A new shortcut fixture initially collided with parallel COM tests; it now uses the repository's existing serialized native-test collection and passes. Automated tests simulate failures and invalid hosts; they do not prove hardware monitor, capture, or actual uninstall behavior.

Evidence logs:

- C:\Dev\GlassDock\artifacts\p0-pin-monitor-debug-build.log
- C:\Dev\GlassDock\artifacts\p0-pin-monitor-release-build.log
- C:\Dev\GlassDock\artifacts\p0-pin-monitor-debug-tests.log
- C:\Dev\GlassDock\artifacts\p0-pin-monitor-debug-windows-tests.log
- C:\Dev\GlassDock\artifacts\p0-pin-monitor-release-tests.log
- C:\Dev\GlassDock\artifacts\p0-pin-monitor-validate.log

## Runtime verification and remaining checks

The updated Debug process remained running and responsive after the user's check. Confirmed by the user: Utilities reopens, missing pin has visible question-mark artwork, and Unpin is available. The reply did not establish an exact transition count, all appearance modes, mixed DPI or restart persistence.

Still required:

1. Delete a disposable pinned target, refresh and restart Doky; verify fallback and valid pins survive. Restore the target and verify artwork recovery. Explicitly Unpin and verify compact width/order, including an invalid stack member.
2. Repeat at least 20 Follow Pointer transitions with Utilities open and closed, then reopen it. Check Dark, Light and Clear (and remaining modes for regression coverage).
3. Check two/three monitors, mixed DPI, negative coordinates, monitor disconnect/reconnect and transitions during popup disposal.
4. Exercise virtual desktop switching/removal, capture recovery, previews, fullscreen policy and existing keyboard/watchdog behavior. These were not manually revalidated in this repair.
5. If a failure recurs, collect the new stage-specific relocation exception and Windows Application event at the matching time. The original monitor crash remains unproven; no permanent-crash-fix claim is made.
