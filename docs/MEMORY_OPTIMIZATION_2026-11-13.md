# Doky memory optimization report

Date 2026-11-13. Work was applied over the existing uncommitted Quick Settings implementation. Its task-entry files were saved outside the repository before edits. This report covers allocation improvements and the available validation. Comprehensive long-running and visual equivalence acceptance remains incomplete.

## Confirmed allocation costs and changes

| Owning file | Confirmed cost | Change and preservation evidence |
|---|---|---|
| `src/GlassDock.Core/Desktop/DockHoverMagnification.cs` | Temporary LINQ iterators, captured predicates, filtered item arrays, and result projection were allocated on every step | Reused a reference-identity membership set that is cleared every step, scanned the original items directly, and allocated only the returned frame snapshot in steady state. Each result remains independent of subsequent steps. Spring constants, elapsed-time clamping, focus ordering, pause/reset behavior, and reduced-motion behavior were preserved. |
| `src/GlassDock.Windows/Applications/WindowsApplicationIconService.cs` | Each native bitmap conversion allocated a bottom-up BGRA buffer and another full-size buffer for flipped rows | Requested top-down pixels directly from `GetDIBits` with a negative output height. The existing alpha and premultiplication processing uses that same buffer. Resolution, pixel format, cache policy, and native handle cleanup are preserved. |

No retained-memory leak was established. These changes remove confirmed transient allocations. The measured process working set does not establish a total RAM reduction.

## Repeated microbenchmarks

Release, .NET 10, Windows x64. Allocation measurements use `GC.GetAllocatedBytesForCurrentThread`. Five runs per case. Hover runs warm up first, then execute 30,000 steps against 20 icons with moving pointer positions. Original and optimized hover implementations were also measured in the same process. The bitmap workload performs 500 conversions per run against the same 256 by 256 native DIB and checks every resulting pixel.

| Metric | Before | After | Reduction |
|---|---:|---:|---:|
| Hover allocation per frame | 3,951.54 bytes | 504.00 bytes | 87.25% |
| Hover allocation over 30,000 frames | 113.05 MiB | 14.42 MiB | 98.64 MiB |
| 256 by 256 bitmap allocation per conversion | 524,368 bytes, steady runs | 262,200 bytes | 50.00% |
| Median warmed hover time, 30,000 steps, paired process | 75.09 ms | 67.18 ms | 10.5% |

The timing sample is a small synthetic check. It does not measure end-to-end input latency or GPU frame pacing. Allocation reductions repeat across runs and are attributable to the changed methods.

## Process measurements

These are observational idle samples from separate normal-mode application processes. Both used the existing local settings. Measurements used `System.Diagnostics.Process`, `Win32_PerfFormattedData_PerfProc_Process.WorkingSetPrivate`, and `dotnet-counters` 10.0.750501 with `EventCounters\System.Runtime`. No forced GC or working-set trimming was used. Before has 45 process samples and approximately 50 seconds of runtime counters. After has 25 process samples and approximately 35 seconds of runtime counters. Background desktop activity and process age differed, so the samples do not constitute a controlled whole-application comparison.

All memory values below use MiB, which is 1,048,576 bytes. Runtime counters originally expressed in decimal MB were converted to MiB.

| Requested metric | Before | After |
|---|---:|---:|
| Startup Private Working Set | Not captured under an equivalent scenario | Not captured under an equivalent scenario |
| Idle Private Working Set, median | 109.547 MiB | 111.539 MiB |
| Active Usage Private Working Set | Not measured in an equivalent interaction workload | Not measured in an equivalent interaction workload |
| Managed heap size, median | 32.974 MiB | 34.031 MiB |
| Private Bytes, median | 181.305 MiB | 182.613 MiB |
| Repeated interaction retained-memory growth | Not established | Not established |
| Idle allocation rate, median | 264,604 bytes/s | 204,840 bytes/s |
| Total Working Set, median | 267.906 MiB | 269.277 MiB |
| GC committed memory, median | 46.084 MiB | 45.773 MiB |

Private working set ranges were 107.984–116.840 MiB before and 109.586–112.859 MiB after. The median after value is 1.992 MiB higher, approximately 1.82%. A total RAM reduction in MB or percentage is therefore unproven. The idle allocation-rate difference is observational and cannot be attributed solely to these changes. Native committed memory and GPU residency were not independently measured. Subtracting the managed heap from Private Bytes would not provide that breakdown.

An additional 70-second inspection-mode baseline and startup samples were collected, but excluded from this comparison because inspection mode disables native OSD suppression. Further inspection-mode launches were stopped after the reported behavior regression.

## Validation

- Task-entry `scripts/Validate.ps1` built successfully. Core had 453 passing tests. Windows had 322 passing tests and one existing failure in `DockStackWiringTests.PopupUsesSharedServicesAndDismissalPaths`.
- The new hover allocation guard failed against the original implementation with 3,951,552 allocated bytes over 1,000 frames. It passes with the optimized implementation and verifies that previously returned frames remain unchanged.
- New native bitmap tests check row order for both bottom-up and top-down source DIBs. Existing opaque-black, transparency, and premultiplied-alpha tests remain enabled.
- Final `scripts/Validate.ps1` passed with 0 build warnings, 0 build errors, 454 Core tests, and 325 Windows tests.
- A differential harness compared original and optimized hover output over 30,000 steps with row additions/removals, pointer changes, pause, reset, reduced motion, NaN geometry, and invalid dimensions. Frame identities, scales, offsets, animation state, and content width matched.
- C# token comparison against the task-entry snapshot found exactly six files with executable/test-token changes, listed below. Other changed C# files contain comment removal. XAML and project XML were structurally equal after removing XML comments.
- PowerShell comment removal preserved non-comment tokens and parse results. String and here-string payloads were retained.
- Native laboratory build and owned fixtures passed. Output reported `fixtureOnly=true`, `injectionAttempted=false`, and `realShellTested=false`. This is fixture coverage of the existing recovery policy.
- `git diff --check` passed after generated comment whitespace was removed.

## Source cleanup and test updates

Removed explanatory comments from application, Core, Windows, tests, scripts, shader source, and native experiment source. This cleanup has no demonstrated runtime RAM benefit. Comment-shaped Windhawk metadata, settings, and its embedded readme were retained because external tooling consumes them. Third-party notice documents and compiler/preprocessor directives were retained.

Two source-wiring tests were updated. The existing stack test now checks the current launch call including its owning window handle. The input-helper test checks the actual consumed-key paths in place of a removed explanatory comment. No test was skipped or disabled.

Files with executable or test-token changes are listed here.

- `src/GlassDock.Core/Desktop/DockHoverMagnification.cs`
- `src/GlassDock.Windows/Applications/WindowsApplicationIconService.cs`
- `tests/GlassDock.Core.Tests/DockHoverMagnificationTests.cs`
- `tests/GlassDock.Windows.Tests/IconAlphaTests.cs`
- `tests/GlassDock.Windows.Tests/DockStackWiringTests.cs`
- `tests/GlassDock.Windows.Tests/InputHelperProtocolTests.cs`

## Native OSD regression and current state

The profiling launch used `--controls`. Existing `MonitorDockCoordinator` logic excludes inspection mode from both volume interception and brightness-lease readiness. That launch caused native volume and brightness popups to reappear. The optimized application was rebuilt and relaunched without `--controls` or `--safe-mode`. Its saved `SuppressNativeVolumeOsd` value is true. The focused volume/brightness suite passed 22 tests. Computer Use was stopped with the user's physical Escape key. After normal startup and bridge restoration, the user physically tested volume and brightness keys and confirmed that both show only Doky's popup.

The existing brightness bridge had been inactive. The user explicitly authorized an exception to the repository Explorer-modification restriction to restart that existing bridge. `Run-BrightnessBridge.ps1 -Enable` now owns the latest optimized Release application, with no inspection/safe-mode arguments. Its live status reports `Loaded=true`, application PID 13784, bridge PID 3084, and Explorer PID 9480. The shared lease reports readiness 1 with approximately 3,000 ms remaining, refreshed by the application. The user physically tested both keys and confirmed that both show only Doky's popup. The live bridge counter increased to 2 suppressed requests. This existing experimental brightness suppression depends on the bridge process remaining active. Its existing fail-open and lifetime checks remain in place.

## Remaining validation and audit limits

The audit inspected the hover path, icon extraction/cache ownership, installed-application index, retained window-frame cache, shared runtime ownership, utility timers/subscriptions, and rendering disposal paths. Existing bounded caches and existing native cleanup were reused. This was a targeted source and allocation audit, and does not establish complete heap/native ownership coverage for every object.

Full before/after UI screenshots, frame-pacing traces, repeated Quick Settings and Individual/Grid changes, Wi-Fi/Bluetooth interaction loops, physical utility toggles, settings reopen cycles, app switching, and an hours-long soak remain incomplete. Runtime comments were removed without altering UI property tokens, and the changed animation algorithm passed output equivalence checks, but complete visual and behavioral equivalence has not been demonstrated. Remaining leaks cannot be ruled out.

The application is left running in normal mode from the optimized Release build under the explicitly authorized existing brightness bridge. Changes remain uncommitted alongside the existing Quick Settings work.

## Comment-only files changed relative to task entry

- `experiments/NativeOsdSuppression/AdapterCompileCheck.cpp`
- `experiments/NativeOsdSuppression/brightness-suppression.wh.cpp`
- `experiments/NativeOsdSuppression/explorer-prototype.wh.cpp`
- `experiments/NativeOsdSuppression/NativeOsdLab.cpp`
- `experiments/NativeOsdSuppression/NativeShellIdentity.hpp`
- `experiments/NativeOsdSuppression/ObservationHooks.hpp`
- `experiments/NativeOsdSuppression/ObservationTests.cpp`
- `experiments/NativeOsdSuppression/PolicyHooks.hpp`
- `experiments/NativeOsdSuppression/Run-BrightnessBridge.ps1`
- `scripts/Build-Msi.ps1`
- `scripts/Diagnose-InputHelper.ps1`
- `scripts/Generate-DokyIcons.ps1`
- `scripts/Install-InputHelper.ps1`
- `scripts/Register-InputHelper.ps1`
- `src/GlassDock.App/App.xaml.cs`
- `src/GlassDock.App/Controls/AdaptiveAppIcon.cs`
- `src/GlassDock.App/Controls/GlassSurface.xaml.cs`
- `src/GlassDock.App/Controls/NotificationBadge.cs`
- `src/GlassDock.App/Controls/QuickSettingsPanel.cs`
- `src/GlassDock.App/Controls/UtilityButton.cs`
- `src/GlassDock.App/Controls/UtilityIcon.cs`
- `src/GlassDock.App/Controls/VerticalUtilityCapsule.cs`
- `src/GlassDock.App/Controls/WifiNetworkListPanel.cs`
- `src/GlassDock.App/Controls/WifiNetworkPanel.cs`
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.cs`
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.DragLens.cs`
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.GlassVisuals.cs`
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.Stacks.cs`
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.Utilities.cs`
- `src/GlassDock.App/Desktop/DockAnimationController.cs`
- `src/GlassDock.App/Desktop/DockAppContextMenuWindow.cs`
- `src/GlassDock.App/Desktop/DockControlPalette.cs`
- `src/GlassDock.App/Desktop/DockIconTransition.cs`
- `src/GlassDock.App/Desktop/DockStackWindow.cs`
- `src/GlassDock.App/Desktop/DokySharedRuntime.cs`
- `src/GlassDock.App/Desktop/GlassHomeWindow.cs`
- `src/GlassDock.App/Desktop/GlassHomeWindow.Dashboard.cs`
- `src/GlassDock.App/Desktop/GlassHomeWindow.Search.cs`
- `src/GlassDock.App/Desktop/HomeDashboardCard.cs`
- `src/GlassDock.App/Desktop/MonitorDockCoordinator.cs`
- `src/GlassDock.App/Desktop/PopupStyle.cs`
- `src/GlassDock.App/Desktop/PopupTheme.cs`
- `src/GlassDock.App/Desktop/SettingsWindow.GlassVisuals.cs`
- `src/GlassDock.App/Desktop/SettingsWindow.Shell.cs`
- `src/GlassDock.App/Desktop/SettingsWindow.xaml.cs`
- `src/GlassDock.App/Desktop/UtilityHudWindow.cs`
- `src/GlassDock.App/Desktop/UtilityPopupWindow.cs`
- `src/GlassDock.App/Desktop/WindowPreviewCoordinator.cs`
- `src/GlassDock.App/Desktop/WindowPreviewWindow.cs`
- `src/GlassDock.App/GlassDock.App.csproj`
- `src/GlassDock.App/Program.cs`
- `src/GlassDock.App/Rendering/ClearDockSpecular.cs`
- `src/GlassDock.App/Rendering/DesktopGlassBackdrop.cs`
- `src/GlassDock.App/Rendering/DockDragLensScene.cs`
- `src/GlassDock.App/Rendering/DokyLiquidGlassSurface.cs`
- `src/GlassDock.App/Rendering/GlassCompositionBrush.cs`
- `src/GlassDock.App/Rendering/GlassEffectGraph.cs`
- `src/GlassDock.App/Rendering/GlassVisualPreview.cs`
- `src/GlassDock.App/Rendering/IconRasterizer.cs`
- `src/GlassDock.App/Rendering/LiquidGlassDrawingTarget.cs`
- `src/GlassDock.App/Rendering/LiquidGlassRim.cs`
- `src/GlassDock.App/Rendering/LiquidGlassShader.cs`
- `src/GlassDock.App/Rendering/OptionalComposition.cs`
- `src/GlassDock.App/Rendering/PopupCompositionTrack.cs`
- `src/GlassDock.App/Rendering/PopupLiquidGlassSurface.cs`
- `src/GlassDock.App/Rendering/RefractionLayer.cs`
- `src/GlassDock.App/Rendering/Shaders/LiquidGlass.hlsl`
- `src/GlassDock.App/Rendering/StartupDiagnostics.cs`
- `src/GlassDock.App/Rendering/UtilityGlassSurface.cs`
- `src/GlassDock.App/Rendering/WifiNetworkContentEffect.cs`
- `src/GlassDock.App/Updates/ManualUpdateService.cs`
- `src/GlassDock.App/ViewModels/GlassLabViewModel.cs`
- `src/GlassDock.Core/Applications/AppActionPanelModel.cs`
- `src/GlassDock.Core/Applications/ApplicationIconFallback.cs`
- `src/GlassDock.Core/Applications/ApplicationSnapshotPump.cs`
- `src/GlassDock.Core/Applications/BadgeCoordinator.cs`
- `src/GlassDock.Core/Applications/DockApplication.cs`
- `src/GlassDock.Core/Applications/DockApplicationCollection.cs`
- `src/GlassDock.Core/Applications/DockApplicationItem.cs`
- `src/GlassDock.Core/Applications/DockApplicationMonitorFilter.cs`
- `src/GlassDock.Core/Applications/DockAppMenuState.cs`
- `src/GlassDock.Core/Applications/DockDragIntent.cs`
- `src/GlassDock.Core/Applications/DockPointerTarget.cs`
- `src/GlassDock.Core/Applications/DockStack.cs`
- `src/GlassDock.Core/Applications/DockWindowClick.cs`
- `src/GlassDock.Core/Applications/GlassSearch.cs`
- `src/GlassDock.Core/Applications/NotificationBadgeState.cs`
- `src/GlassDock.Core/Applications/VirtualDesktopPolicy.cs`
- `src/GlassDock.Core/Applications/WindowPreviewFrame.cs`
- `src/GlassDock.Core/Applications/WindowPreviewLayout.cs`
- `src/GlassDock.Core/Applications/WindowPreviewSession.cs`
- `src/GlassDock.Core/Desktop/ApplicationShutdownState.cs`
- `src/GlassDock.Core/Desktop/CaptureDrawGate.cs`
- `src/GlassDock.Core/Desktop/DockEdgeSlice.cs`
- `src/GlassDock.Core/Desktop/DockIconMotion.cs`
- `src/GlassDock.Core/Desktop/DockKeyboardShortcut.cs`
- `src/GlassDock.Core/Desktop/DockPerformanceDiagnostics.cs`
- `src/GlassDock.Core/Desktop/DockRelocationGuard.cs`
- `src/GlassDock.Core/Desktop/DockSizing.cs`
- `src/GlassDock.Core/Desktop/DockStateMachine.cs`
- `src/GlassDock.Core/Desktop/DockTransitionGeometry.cs`
- `src/GlassDock.Core/Desktop/DockTransitionTiming.cs`
- `src/GlassDock.Core/Desktop/DockWaveGeometry.cs`
- `src/GlassDock.Core/Desktop/DragLensMotion.cs`
- `src/GlassDock.Core/Desktop/GlassHomeSession.cs`
- `src/GlassDock.Core/Desktop/HomeDashboardLayout.cs`
- `src/GlassDock.Core/Desktop/InputLifecycleRecoveryGate.cs`
- `src/GlassDock.Core/Desktop/InputSignalMailbox.cs`
- `src/GlassDock.Core/Desktop/RetainedWindowSlot.cs`
- `src/GlassDock.Core/Desktop/TaskbarLease.cs`
- `src/GlassDock.Core/Desktop/UtilityPointerGesture.cs`
- `src/GlassDock.Core/Desktop/VolumeHudPlacement.cs`
- `src/GlassDock.Core/Desktop/WifiProfile.cs`
- `src/GlassDock.Core/Desktop/WifiScrollGeometry.cs`
- `src/GlassDock.Core/Desktop/WindowsKeyGesture.cs`
- `src/GlassDock.Core/Materials/DockMaterialRendering.cs`
- `src/GlassDock.Core/Materials/DockSpecularLighting.cs`
- `src/GlassDock.Core/Materials/GlassMaterial.cs`
- `src/GlassDock.Core/Materials/GlassMaterialPreset.cs`
- `src/GlassDock.Core/Materials/LiquidGlassMaterial.cs`
- `src/GlassDock.Core/Materials/LiquidGlassShaderState.cs`
- `src/GlassDock.Core/Materials/PopupMaterial.cs`
- `src/GlassDock.Core/Settings/DockAppearanceMode.cs`
- `src/GlassDock.Core/Settings/GlassDockSettings.cs`
- `src/GlassDock.Core/Settings/GlassDockSettingsSession.cs`
- `src/GlassDock.Core/Settings/GlassMaterialMode.cs`
- `src/GlassDock.Core/Settings/GlassVisualProfile.cs`
- `src/GlassDock.Core/Settings/SettingsCatalog.cs`
- `src/GlassDock.Core/Settings/SettingsShellAppearance.cs`
- `src/GlassDock.Watchdog/Program.cs`
- `src/GlassDock.Windows/Applications/ApplicationNative.cs`
- `src/GlassDock.Windows/Applications/DockPinStore.cs`
- `src/GlassDock.Windows/Applications/InstalledPinReconciler.cs`
- `src/GlassDock.Windows/Applications/PinTargetProbe.cs`
- `src/GlassDock.Windows/Applications/PreviewDiagnostics.cs`
- `src/GlassDock.Windows/Applications/ShellApplicationIdentityResolver.cs`
- `src/GlassDock.Windows/Applications/ShellApplicationMetadata.cs`
- `src/GlassDock.Windows/Applications/WindowFrameCache.cs`
- `src/GlassDock.Windows/Applications/WindowsApplicationIndex.cs`
- `src/GlassDock.Windows/Applications/WindowsApplicationLauncher.cs`
- `src/GlassDock.Windows/Applications/WindowsApplicationService.cs`
- `src/GlassDock.Windows/Applications/WindowsNotificationService.cs`
- `src/GlassDock.Windows/Applications/WindowsSettingsCatalog.cs`
- `src/GlassDock.Windows/Applications/WindowsToastBadgeProvider.cs`
- `src/GlassDock.Windows/Applications/WindowsVirtualDesktopQuery.cs`
- `src/GlassDock.Windows/Applications/WindowThumbnail.cs`
- `src/GlassDock.Windows/Desktop/AirplaneModeNative.cs`
- `src/GlassDock.Windows/Desktop/AirplaneModeUtilityService.cs`
- `src/GlassDock.Windows/Desktop/BluetoothDeviceStore.cs`
- `src/GlassDock.Windows/Desktop/BluetoothUtilityService.cs`
- `src/GlassDock.Windows/Desktop/BrightnessHardware.cs`
- `src/GlassDock.Windows/Desktop/BrightnessService.cs`
- `src/GlassDock.Windows/Desktop/CaptureBorderPermission.cs`
- `src/GlassDock.Windows/Desktop/DesktopCaptureSource.cs`
- `src/GlassDock.Windows/Desktop/DesktopWindowFocus.cs`
- `src/GlassDock.Windows/Desktop/DesktopWindowHighlight.cs`
- `src/GlassDock.Windows/Desktop/DndNative.cs`
- `src/GlassDock.Windows/Desktop/DndUtilityService.cs`
- `src/GlassDock.Windows/Desktop/EnergySaverNative.cs`
- `src/GlassDock.Windows/Desktop/EnergySaverUtilityService.cs`
- `src/GlassDock.Windows/Desktop/GlassHomeInput.cs`
- `src/GlassDock.Windows/Desktop/HomeDesktopEnvironment.cs`
- `src/GlassDock.Windows/Desktop/InputDesktopMonitor.cs`
- `src/GlassDock.Windows/Desktop/InteractiveGlassWindowHost.cs`
- `src/GlassDock.Windows/Desktop/MasterVolumeService.cs`
- `src/GlassDock.Windows/Desktop/NativeBrightnessOsdLease.cs`
- `src/GlassDock.Windows/Desktop/NativeVolumeOsdSuppression.cs`
- `src/GlassDock.Windows/Desktop/NightLightCloudStore.cs`
- `src/GlassDock.Windows/Desktop/NightLightRegistry.cs`
- `src/GlassDock.Windows/Desktop/NightLightUtilityService.cs`
- `src/GlassDock.Windows/Desktop/SystemControlAudio.Bluetooth.cs`
- `src/GlassDock.Windows/Desktop/SystemControlAudio.cs`
- `src/GlassDock.Windows/Desktop/SystemControlAudio.Volume.cs`
- `src/GlassDock.Windows/Desktop/TaskbarAutoHide.cs`
- `src/GlassDock.Windows/Desktop/TaskbarDevelopmentSession.cs`
- `src/GlassDock.Windows/Desktop/WifiNetworkControl.cs`
- `src/GlassDock.Windows/Desktop/WifiNetworkSelection.cs`
- `src/GlassDock.Windows/Desktop/WifiUtilityService.cs`
- `src/GlassDock.Windows/Desktop/WindowPreviewPlacement.cs`
- `src/GlassDock.Windows/Desktop/WindowsCompositionSupport.cs`
- `src/GlassDock.Windows/Desktop/WindowsInputHelperBridge.cs`
- `src/GlassDock.Windows/Desktop/WindowsInputHelperHost.cs`
- `src/GlassDock.Windows/Desktop/WindowsKeyboardService.cs`
- `src/GlassDock.Windows/Desktop/WindowsKeyHook.cs`
- `src/GlassDock.Windows/Desktop/WindowsMonitorService.cs`
- `src/GlassDock.Windows/Desktop/WindowsOverlayManager.cs`
- `src/GlassDock.Windows/Desktop/WindowsSystemTheme.cs`
- `src/GlassDock.Windows/Desktop/WindowsTaskbarController.cs`
- `src/GlassDock.Windows/Interop/NativeMethods.cs`
- `src/GlassDock.Windows/Settings/DokyInstanceLease.cs`
- `src/GlassDock.Windows/Settings/DokyUserData.cs`
- `src/GlassDock.Windows/Settings/GlassDockSettingsStore.cs`
- `src/GlassDock.Windows/Settings/WindowsStartupService.cs`
- `tests/GlassDock.Core.Tests/DesktopRemovalTests.cs`
- `tests/GlassDock.Core.Tests/DockReversalTests.cs`
- `tests/GlassDock.Core.Tests/DockSizingTests.cs`
- `tests/GlassDock.Core.Tests/DockWaveGeometryTests.cs`
- `tests/GlassDock.Core.Tests/GlassHomeSessionTests.cs`
- `tests/GlassDock.Core.Tests/GlassSearchTests.cs`
- `tests/GlassDock.Core.Tests/InputSignalMailboxTests.cs`
- `tests/GlassDock.Core.Tests/NotificationBadgeTests.cs`
- `tests/GlassDock.Core.Tests/WindowPreviewFrameTests.cs`
- `tests/GlassDock.Core.Tests/WindowPreviewTests.cs`
- `tests/GlassDock.Core.Tests/WindowsKeyGestureTests.cs`
- `tests/GlassDock.Rendering.Probe/Program.cs`
- `tests/GlassDock.Windows.Tests/BackdropModeSwitchLifecycleTests.cs`
- `tests/GlassDock.Windows.Tests/DesktopWindowHighlightTests.cs`
- `tests/GlassDock.Windows.Tests/DockAppearanceRuntimeWiringTests.cs`
- `tests/GlassDock.Windows.Tests/DockAppMenuWiringTests.cs`
- `tests/GlassDock.Windows.Tests/DockInteractionRegionTests.cs`
- `tests/GlassDock.Windows.Tests/DockStackStoreTests.cs`
- `tests/GlassDock.Windows.Tests/FileDropTests.cs`
- `tests/GlassDock.Windows.Tests/IconServiceTests.cs`
- `tests/GlassDock.Windows.Tests/InstalledPinReconciliationTests.cs`
- `tests/GlassDock.Windows.Tests/ManualUpdateServiceTests.cs`
- `tests/GlassDock.Windows.Tests/MonitorRelocationLifetimeTests.cs`
- `tests/GlassDock.Windows.Tests/NativeDesktopTestCollection.cs`
- `tests/GlassDock.Windows.Tests/NightLightUtilityTests.cs`
- `tests/GlassDock.Windows.Tests/OptionalCompositionTests.cs`
- `tests/GlassDock.Windows.Tests/PinRecoveryTests.cs`
- `tests/GlassDock.Windows.Tests/PopupAppearanceWiringTests.cs`
- `tests/GlassDock.Windows.Tests/SettingsShellTests.cs`
- `tests/GlassDock.Windows.Tests/VirtualDesktopQueryTests.cs`
- `tests/GlassDock.Windows.Tests/WifiDisconnectTests.cs`
- `tests/GlassDock.Windows.Tests/WindowsSettingsCatalogTests.cs`
