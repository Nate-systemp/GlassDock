# Dock interaction continuation — 2026-09-20

## Current implementation and causes

Continued the existing uncommitted context-menu work. Preserved app menu commands, glass styling, wave geometry, icon rendering, previews, capture policy, Home/search and taskbar recovery.

The interrupted implementation had separate root-menu and app-menu ownership, direct keyboard collapse bypassing ownership, pointer-exit resetting magnification, pending collapse/peek callbacks, and active animation timers that were not frozen. Its Idle placement callback and hide animation set indicator opacity to zero. Peek departure used 450 ms instead of the requested two seconds. Its stationary input-only peek path disabled the native pointer timer after making the HWND transparent, so the HWND could fail to reacquire hover. An animation corridor also intercepted pixels above/beside the moving pill. Repeated native outside samples restarted the collapse delay. Menu close used stale hover information, and application removal did not explicitly dismiss that app's flyout.

## Resulting interaction flow

- Expanded -> normal pointer-leave grace -> Collapsing -> Idle full pill -> two-second inactivity wait -> lowered peek. Lowering is a separate 210 ms position animation. Indicator opacity stays one; at bottom -2 DIP, three DIP remain on screen.
- Peek -> physical pointer movement into the current pill input shape -> Hovering full pill (180 ms rise). Hover does not expand the dock. Click raises/expands using the existing dock animation.
- Leaving the raised pill schedules one two-second countdown. Geometry movement alone cannot raise it again. Delayed work checks cancellation, current state, holds and actual native cursor position before acting.
- Both menu types acquire the same identity-based HashSet hold in Opening, before presentation. Acquisition cancels delayed work and pending preview hover, pauses magnification/wave at their current values, and freezes an in-progress dock transition without resetting scale or geometry. A peek menu raises the pill.
- Transition revisions reject old completions. On final hold release, query the native cursor once; resume from current values, with normal collapse grace outside. A held transition resumes expansion inside or normal delayed contraction outside.
- Menu release is deferred one dispatcher turn to avoid resetting visuals between A closing and B opening. Duplicate releases are harmless, and an already reopened flyout is not released. Flyout items are rebuilt on opening and cleared on closing. Changing window membership/pin state dismisses stale app menus; detach and disposal also hide them. Exit still follows normal recovery/disposal.
- Dock collection/layout reconciliation is deferred while a menu holds its icon anchor. On release, the latest collection is applied through the dispatcher, avoiding recursive collection removal while a preview/menu releases its hold.
- Bare-Win suppression is latched at physical key-down and checked again at key-up and posted-message dispatch. Menu-state revisions invalidate queued toggles across dismissal. Existing shortcut recognition/injection and recovery remain in place.

## Input and resource behavior

The pill uses a 120-DIP-wide input-only polygon with eight DIP directly below its five-DIP height. It follows every placement update; there is no swept corridor. Outside pixels pass through using the existing layered-window transparency path. Neither peek nor expanded input installs a visual clipping region. Screen bounds constrain the usable portion.

The existing native 50 ms pointer-reacquisition timer now remains available in peek as well as expanded mode. This is necessary because WS_EX_TRANSPARENT suppresses ordinary input delivery; disabling it previously stranded the peek handle. There is one existing sampler, not another poller or 60 FPS render loop. Animation timers stop when settled/held; no capture buffers were added. This changes idle sampler lifetime and still needs a controlled CPU baseline comparison.

The earlier app-menu actions (Open/Activate, New window, Show All Windows, per-window activation, Close Window/All, Pin/Unpin, Run as Administrator and Open File Location) remain implemented. Close All attempts every HWND even if one close request fails. Unpin and Remove are the same underlying operation, so there is no duplicate Remove command. The dock menu retains functional Settings/development controls, Home, recovery, laboratory and Exit. Peek preferences, size presets, appearance settings, Add Application, Lock and Restart were not implemented in the interrupted code and are not added by this interaction-bug continuation.

## Verification and limits

Release app launched as PID 28864 with the Settings window. The computer-use helper listed the dock and Settings but rejected a fresh dock binding twice: "window id ... no longer belongs to ...; current owner is ...", with identical owner strings. No UI input was sent after those errors. Consequently none of the requested visual/manual menu-cycle cases is claimed passed; Win/Win+Space interaction, 100 real menu cycles, first/last icon visuals, external app closure with a menu open, click-through into another application, and multi-monitor popup placement remain unverified. Placement remains the existing primary-monitor implementation.

Process samples during automated checks (not an isolated idle benchmark): at 11:35:09 local time, working set 215015424 bytes, private bytes 145383424, CPU 6.25 seconds, handles 1452; at 11:36:10, working set 221024256 bytes, private bytes 150044672, CPU 8.734375 seconds, handles 1485. That is about 205.1 -> 210.8 MiB working set, 138.6 -> 143.1 MiB private bytes and 2.48 CPU seconds over 61.8 seconds. Windows capture tests ran during this interval and created tracked windows. These samples do not demonstrate a leak, a stable plateau, the user's 77-94 MB baseline, or a leak-free 100-menu workload.

Automated coverage includes stale transition completion rejection, exit during a hold, 100 repeated held-collapse transitions, 100 native shape cycles, exact pill lower-extension inclusion/side/top exclusion, no visual clipping, and native transparency reacquisition. Existing minimized-frame retention and multi-monitor mirror tests also run in the Windows suite. Final command totals are recorded with the delivery report. The two previously known failures are WindowsKeyGestureTests.Existing_modifier_and_two_windows_keys_are_not_bare (line 49) and DesktopWindowHighlightTests.Overlay_is_hollow_nonactivating_and_leaves_targets_unchanged (reflection NullReferenceException, line 267).

## Complete modified-file inventory

Paths below are relative to C:\Dev\GlassDock. The delivered ZIP includes each complete file, preserving these directories, plus validation logs.

| File | Change |
| --- | --- |
| src/GlassDock.App/Desktop/DesktopOverlayWindow.cs | Shared holds; visible peek and two-second lowering; native pointer truth; frozen/resumed wave; deferred dock layout; guarded collapse and menu actions. |
| src/GlassDock.App/Desktop/DockAnimationController.cs | Hold magnification; retain current rendered values when freezing transitions or stopping indicator fades. |
| src/GlassDock.App/Desktop/WindowPreviewCoordinator.cs | Idempotent menu ownership, deferred close, stale-menu dismissal, disposal, current-state app menus and Show All reuse. |
| src/GlassDock.App/Desktop/DevelopmentWindow.cs | Preserved explicit --controls developer entry point; the product Dock Settings command now opens SettingsWindow. |
| src/GlassDock.App/ViewModels/DockApplicationsViewModel.cs | Preserved elevation/file-location wrappers from interrupted work. |
| src/GlassDock.Core/Applications/DockApplication.cs | Preserved corresponding application-service contract from interrupted work. |
| src/GlassDock.Core/Desktop/DockStateMachine.cs | Held-transition revision invalidation and resume decisions. |
| src/GlassDock.Windows/Applications/WindowsApplicationLauncher.cs | Preserved valid-file checks, runas and Explorer file selection from interrupted work. |
| src/GlassDock.Windows/Applications/WindowsApplicationService.cs | Preserved launcher delegation from interrupted work. |
| src/GlassDock.Windows/Desktop/WindowsKeyboardService.cs | Suppress current Win press and invalidate queued toggles across menu-state changes. |
| src/GlassDock.Windows/Desktop/WindowsOverlayManager.cs | Moving input-only pill polygon, lower extension, cursor sampling and continued transparency reacquisition. |
| src/GlassDock.Windows/Interop/NativeMethods.cs | GetWindowRgn for one-off input-shape queries. |
| tests/GlassDock.Core.Tests/DesktopTests.cs | Stale completions, held transitions and 100 repeated collapse/hold/resume cycles. |
| tests/GlassDock.Windows.Tests/DockInteractionRegionTests.cs | 100 native input cycles, precise extension/exclusion, no clipping and transparency reacquisition. |
| docs/DOCK_INPUT_AND_CAPTURE_REVIEW.md | Clarifies the input-only peek path. |
| docs/DOCK_INTERACTION_REVIEW.md | Root causes, final state flow, verification limits, measurements and file inventory. |

The user exited the test app normally; no GlassDock.App process remained before final build. No commit was made.

## Final validation results

- Full Release solution build: PASS, zero warnings and zero errors.
- Core suite: 84 passed, one known failure; Windows suite: 32 passed, one known failure. Total 116 passed, two known failures, no new failures.
- scripts/Validate.ps1: locked restore and build passed, then stopped on the known Core failure. Both suites were subsequently run together against the final build.
- git diff --check: PASS. Working changes are uncommitted; no unrelated tracked files were reset.
- The 100-cycle automated checks are state/native-region tests, not 100 real WinUI menu openings. Manual visual acceptance remains outstanding due to the helper binding failure described above.
