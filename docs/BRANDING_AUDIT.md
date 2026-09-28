# Doky branding audit

Current source audit, 2026-09-28. Class 1 = intentionally internal/technical; class 2 = product-facing branding requiring replacement.

Class 2 remaining in active application branding: **0**. Settings/About labels, titles, descriptions, update messages and accessibility labels use Doky. The displayed settings-file path remains the real compatibility path; OS exception details may also contain real filenames. These are technical information, not branding.

Changes in this cleanup:
- Watchdog error: `Parent identity does not match GlassDock.App.` → `Parent identity does not match the Doky application.`
- Installer build instruction: `Exit GlassDock normally before building the installer.` → `Exit Doky normally before building the installer.`
- Verification banner: `Building GlassDock...` → `Building Doky...`
- Settings documentation: `GlassDock Settings` → `Doky Settings`.
- Startup documentation: `Exit GlassDock` → `Exit Doky`; product reference changed to Doky.

## Class 1: retained meanings

- Namespaces, classes, callbacks, project/solution references, assembly names, manifest identity, test namespaces/fixtures and dependency locks.
- `Natesystemp.GlassDock`, GitHub repository/update URL, installer IDs and permanent UpgradeCode.
- Executable/DLL/PRI names, install directories, settings/pins/log directories and actual recovery commands.
- Single-instance/recovery/taskbar mutexes and events; startup registry value `GlassDock`; process identity checks.
- Debugger thread names and source comments, developer documentation and historical validation records (not shipped UI).
- Existing compiled outputs, published releases, backups, diagnostics and IDE caches are historical/generated artifacts. They were not rewritten or repackaged; old binaries can still display old branding.

The complete repository text search included ignored/generated files. Five locked `.vs` FileContentIndex files could not be read. `.git` history is excluded because historical commits must not be rewritten. Binary/compressed payload contents are not a source-text audit.

## Source occurrence inventory

Each row below classifies all listed matching lines as **class 1**. Line numbers are a snapshot before adding this audit document. Generated outputs, old publish/release folders, backups, IDE files and diagnostic artifacts are covered by the artifact classification above. The existing untracked `branding/` assets were left untouched.

| File | Matching lines | Classification |
| --- | --- | --- |
| `.gitignore` | 29, 30, 31 | 1 — build / persistence / installer identity |
| `AGENTS.md` | 7 | 1 — technical documentation / historical record |
| `ARCHITECTURE.md` | 17, 18, 19, 20, 21, 56, 94 | 1 — technical documentation / historical record |
| `docs/CONTROL_CENTER.md` | 10, 12 | 1 — technical documentation / historical record |
| `docs/DESKTOP_RECOVERY_DESIGN.md` | 9 | 1 — technical documentation / historical record |
| `docs/DOCK_INPUT_AND_CAPTURE_REVIEW.md` | 14 | 1 — technical documentation / historical record |
| `docs/DOCK_INTERACTION_REVIEW.md` | 38, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 59 | 1 — technical documentation / historical record |
| `docs/GLASS_HOME_SEARCH.md` | 9, 25, 35, 76, 77, 78, 79, 80, 86, 87, 88, 93 | 1 — technical documentation / historical record |
| `docs/PHASE_2_3_VALIDATION.md` | 32, 33, 36, 40 | 1 — technical documentation / historical record |
| `docs/SETTINGS_FOUNDATION.md` | 9, 48, 50 | 1 — technical documentation / historical record |
| `docs/SETTINGS_UI.md` | 9, 10, 43, 72 | 1 — technical documentation / historical record |
| `docs/STARTUP_COMPATIBILITY.md` | 17, 18, 48, 52 | 1 — technical documentation / historical record |
| `docs/TASKBAR_AND_WINDOWS_KEY_FIX.md` | 7, 13 | 1 — technical documentation / historical record |
| `docs/TOOLCHAIN.md` | 8 | 1 — technical documentation / historical record |
| `docs/UTILITY_GLASS_VALIDATION.md` | 23 | 1 — technical documentation / historical record |
| `GlassDock.sln` | 8, 10, 12, 14, 16, 20, 22 | 1 — build / persistence / installer identity |
| `installer/wix/GlassDock.Installer.wixproj` | 8 | 1 — build / persistence / installer identity |
| `installer/wix/Package.wxs` | 2, 9, 10, 13, 17, 20, 21, 22, 25, 26, 29 | 1 — build / persistence / installer identity |
| `installer/wix/README.md` | 17, 21 | 1 — technical documentation / historical record |
| `README.md` | 12, 13, 23, 24, 28 | 1 — technical documentation / historical record |
| `REVERT-APP-WINDOW-ANIMATION.ps1` | 3, 5, 13 | 1 — build / persistence / installer identity |
| `scripts/Build-Msi.ps1` | 8, 10, 11, 30, 43, 49, 51, 58, 65 | 1 — build / persistence / installer identity |
| `scripts/GlassDock.iss` | 1, 8, 21, 29, 30, 45 | 1 — build / persistence / installer identity |
| `scripts/Test-SearchIndex.ps1` | 9, 14, 15 | 1 — build / persistence / installer identity |
| `scripts/Validate.ps1` | 6, 8, 11 | 1 — build / persistence / installer identity |
| `src/GlassDock.App/app.manifest` | 7 | 1 — build / persistence / installer identity |
| `src/GlassDock.App/App.xaml` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/App.xaml.cs` | 3, 4, 5, 7, 17, 28, 71, 72, 99 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Controls/AdaptiveAppIcon.cs` | 2, 3, 9 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Controls/GlassSurface.xaml` | 2 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Controls/GlassSurface.xaml.cs` | 2, 3, 4, 12 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/CalendarPopoverWindow.cs` | 1, 2, 3, 4, 5, 12 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/DesktopOverlayWindow.cs` | 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 25, 99, 100, 191, 192, 328, 1004, 1600, 2064, 2066, 2166, 2171, 2522, 2621, 2622, 2623, 2669, 2670, 2753, 2816, 2860 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/DevelopmentWindow.cs` | 6 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/DockAnimationController.cs` | 2, 3, 10, 14, 147, 148, 149 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/GlassHomeWindow.cs` | 3, 4, 5, 6, 7, 8, 9, 17 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/SettingsWindow.xaml` | 2 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/SettingsWindow.xaml.cs` | 1, 2, 3, 8, 10, 14, 15, 21, 23, 32, 33, 38, 40, 48, 50, 107, 220, 449, 466, 468, 469, 470, 471, 472, 473, 474, 475, 476, 488, 490, 491, 502, 505 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/SystemControlStyle.cs` | 7 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/SystemQuickSettingsWindow.cs` | 1, 2, 3, 4, 5, 15, 17, 62 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/SystemTrayWindow.cs` | 1, 7, 8, 9, 10, 11, 19, 22, 561, 576, 579, 630, 704, 1263, 1374, 1433 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/UtilityPopupPresentation.cs` | 3, 4, 10 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/UtilityPopupStyle.cs` | 1, 2, 3, 9, 16, 18 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/WindowPreviewCoordinator.cs` | 1, 2, 3, 8 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/WindowPreviewWindow.cs` | 2, 3, 4, 5, 6, 7, 16, 1255 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Desktop/WindowsSystemControlService.cs` | 5 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/GlassDock.App.csproj` | 15, 31, 32, 33, 39, 49, 56, 63 | 1 — build / persistence / installer identity |
| `src/GlassDock.App/packages.lock.json` | 175 | 1 — build / persistence / installer identity |
| `src/GlassDock.App/Program.cs` | 5 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Rendering/DesktopGlassBackdrop.cs` | 6, 7, 13, 719, 724 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Rendering/GlassCompositionBrush.cs` | 2, 9 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Rendering/GlassEffectGraph.cs` | 1, 6 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Rendering/IconRasterizer.cs` | 1, 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Rendering/OptionalComposition.cs` | 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Rendering/PopupCompositionTrack.cs` | 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Rendering/RefractionLayer.cs` | 1, 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Rendering/StartupDiagnostics.cs` | 1, 9 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Updates/ManualUpdateService.cs` | 4, 8, 9, 98, 127 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/ViewModels/DockApplicationsViewModel.cs` | 2, 5 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/ViewModels/GlassLabViewModel.cs` | 1, 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Views/GlassLabView.xaml` | 2, 5 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.App/Views/GlassLabView.xaml.cs` | 1, 2, 3, 14 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Applications/DockApplication.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Applications/DockApplicationCollection.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Applications/DockApplicationItem.cs` | 4 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Applications/GlassSearch.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Applications/WindowPreviewLayout.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Applications/WindowPreviewSession.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/ApplicationShutdownState.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/DesktopPlacement.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/DockStateMachine.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/DockWaveGeometry.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/GlassHomeSession.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/IKeyboardService.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/ITaskbarController.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/PopupMorph.cs` | 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/RetainedWindowSlot.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/TaskbarLease.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/UtilityPopupRequests.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Desktop/WindowsKeyGesture.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Materials/GlassMaterial.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Materials/GlassMaterialPreset.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Materials/UtilityMaterial.cs` | 1, 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/README.md` | 1 | 1 — technical documentation / historical record |
| `src/GlassDock.Core/Settings/DockAppearanceMode.cs` | 1 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Settings/DockAppearanceSettings.cs` | 1, 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Settings/GlassDockSettings.cs` | 1, 11, 14, 76 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Settings/GlassDockSettingsSession.cs` | 1, 12, 14, 18, 20, 22, 25, 44, 46, 50, 57, 59, 60, 61, 63, 77, 94, 116, 117, 119, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 135, 139, 140, 145, 146, 151, 153 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Core/Settings/GlassMaterialMode.cs` | 2, 4, 20, 21, 25, 26 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Licensing/GlassDock.Licensing.csproj` | 6 | 1 — build / persistence / installer identity |
| `src/GlassDock.Licensing/README.md` | 1 | 1 — technical documentation / historical record |
| `src/GlassDock.Watchdog/GlassDock.Watchdog.csproj` | 7, 8 | 1 — build / persistence / installer identity |
| `src/GlassDock.Watchdog/packages.lock.json` | 24 | 1 — build / persistence / installer identity |
| `src/GlassDock.Watchdog/Program.cs` | 2, 3, 35, 105 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Watchdog/README.md` | 1 | 1 — technical documentation / historical record |
| `src/GlassDock.Windows/Applications/ApplicationNative.cs` | 4 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/DockPinStore.cs` | 2, 4, 6, 12, 170, 216 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/PreviewDiagnostics.cs` | 3, 4, 6, 13 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/ShellApplicationMetadata.cs` | 4, 6 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/WindowFrameCache.cs` | 3, 4, 11, 961 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/WindowsApplicationIconService.cs` | 3, 5, 7 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/WindowsApplicationIndex.cs` | 2, 4, 67, 178 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/WindowsApplicationLauncher.cs` | 2, 4, 16 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/WindowsApplicationService.cs` | 6, 7, 9, 45, 70 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/WindowsSettingsCatalog.cs` | 1, 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Applications/WindowThumbnail.cs` | 3, 4, 6 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/DesktopWindowFocus.cs` | 3, 4, 5, 7 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/DesktopWindowHighlight.cs` | 1, 6, 36 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/DisplayBrightnessControl.cs` | 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/GlassHomeInput.cs` | 2, 3, 5 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/InteractiveGlassWindowHost.cs` | 2, 4, 8 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/SystemRadioControls.cs` | 7 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/TaskbarAutoHide.cs` | 3, 5, 10, 52 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/TaskbarDevelopmentSession.cs` | 3, 99 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/TaskbarRecovery.cs` | 3, 7, 8 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/WifiNetworkControl.cs` | 5 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/WindowPreviewPlacement.cs` | 3, 5, 7 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/WindowsCompositionSupport.cs` | 3 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/WindowsKeyboardService.cs` | 1, 2, 5, 44, 128, 136, 187, 330, 367 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/WindowsOverlayManager.cs` | 3, 4, 5, 7, 42, 316, 460 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Desktop/WindowsTaskbarController.cs` | 3, 4, 6, 179, 199 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/GlassDock.Windows.csproj` | 7 | 1 — build / persistence / installer identity |
| `src/GlassDock.Windows/Interop/NativeMethods.cs` | 4 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/README.md` | 1 | 1 — technical documentation / historical record |
| `src/GlassDock.Windows/Settings/GlassDockSettingsStore.cs` | 2, 4, 6, 7, 19, 23, 34, 49, 54, 67, 94 | 1 — code symbols, comments or compatibility identifiers |
| `src/GlassDock.Windows/Settings/WindowsStartupService.cs` | 3, 6, 12 | 1 — code symbols, comments or compatibility identifiers |
| `tests/GlassDock.Core.Tests/ApplicationShutdownStateTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/ArchitectureTests.cs` | 4, 12, 15 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/DesktopTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/DockApplicationTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/DockWaveGeometryTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/GlassDock.Core.Tests.csproj` | 13 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/GlassDockSettingsTests.cs` | 1, 2, 5, 7, 17, 26, 27, 31, 33, 51, 72, 87, 102, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 138, 139, 140, 141, 142, 143, 144, 145, 151, 158, 160, 168, 192, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 221, 229 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/GlassHomeSessionTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/GlassMaterialTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/GlassSearchTests.cs` | 1, 2, 5 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/PopupMorphTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/RetainedWindowSlotTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/TaskbarLeaseTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/UtilityMaterialTests.cs` | 1, 2, 5, 15 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/UtilityPopupRequestsTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/WindowPreviewTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Core.Tests/WindowsKeyGestureTests.cs` | 1, 4 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/ArchitectureTests.cs` | 4, 14, 17, 18, 19, 36, 39, 41 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/DesktopShutdownRegressionTests.cs` | 3, 11, 18 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/DesktopStartupRegressionTests.cs` | 3, 11, 18 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/DesktopWindowHighlightTests.cs` | 3, 5, 6, 8, 255, 257 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/DockAppearanceRuntimeWiringTests.cs` | 3, 11, 18, 24 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/DockInteractionRegionTests.cs` | 2, 5 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/DockOrderTests.cs` | 1, 2, 5 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/GlassDock.Windows.Tests.csproj` | 8, 9, 16 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/GlassDockSettingsStoreTests.cs` | 2, 3, 6, 8, 40, 52, 61, 98, 119, 120, 121, 122, 142, 166, 167, 168, 169, 170, 171, 172, 195, 204, 243, 251 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/IconAlphaTests.cs` | 3, 4, 7 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/IconServiceTests.cs` | 2, 5 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/ManualUpdateServiceTests.cs` | 1, 5 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/OptionalCompositionTests.cs` | 2, 5 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/packages.lock.json` | 115 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/SystemControlTests.cs` | 4, 7 | 1 — test symbols, paths or identity fixtures |
| `tests/GlassDock.Windows.Tests/WindowsSettingsCatalogTests.cs` | 1, 2, 5 | 1 — test symbols, paths or identity fixtures |

