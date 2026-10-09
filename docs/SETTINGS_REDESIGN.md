# Doky Settings redesign

Implemented in the existing Settings window, October 7, 2026. No commit, packaging or release is part of this work.

## Architecture found and retained

The dock owns a `RetainedWindowSlot<SettingsWindow>`. The window previously contained eight XAML pages, forced Light colors, editable controls with an Apply action, and immediate-save handlers for Pin Dock and badges. `GlassDockSettingsSession` is the normalized application-wide state. `GlassDockSettingsStore` serializes it atomically to the existing user profile. Startup, recovery and updater actions already had dedicated implementations; those are reused.

The dock and utilities use `DesktopGlassBackdrop`, shared material presets, `UtilityPopupStyle`, `GlassSurface`, and `DockControlPalette`. The Clear dock and drag lens additionally use the existing GPU shader and desktop capture surface. Settings reuses the material infrastructure; it does not create a capture session or another dock.

## Shell, navigation and search

One retained XAML page tree, fixed icon sidebar, one vertical page ScrollViewer, integrated native caption controls and a practical 880×640 DIP minimum. Pages are shown/hidden in place. The existing centered/foreground open behavior is preserved. Closing unsubscribes from the settings session and releases the Settings backdrop/native host.

`SettingsCatalog` defines the nine pages and a bounded, case-insensitive, multiword index over actual controls and feature guidance. Suggestions navigate to the page and bring the target into view; focus moves to controls when possible. It does not search the filesystem or invent options.

| Page | Contents |
| --- | --- |
| General | Existing Windows startup registration and profile continuity information. |
| Appearance | Live material preview using up to six existing dock app icons, five mode tiles, Clear edge-refraction control, all-mode highlight position, and applicable shared material tuning. |
| Dock | Icon size, magnification scale, spacing, Pin Dock, auto-hide and peek delays. |
| Apps & Stacks | Stack/pinning instructions and existing notification-badge toggle, permission request and status. No changes to stack persistence. |
| Behavior | Hover wave and truthful descriptions of current activation, preview, file-drop and keyboard interactions. No unsupported toggles. |
| Displays | Existing Primary / Follow Pointer / Follow Active Window / All Displays enum mapping, bottom spacing and explanations. |
| System | Existing taskbar restore/resume, restart, Safe Mode and exit actions. Helper connection status is explicitly unavailable here; opening the page requests no elevation. |
| Advanced | Actual Settings renderer status, profile file path, confirmed appearance/placement/editable-preference resets. |
| About | Doky logo, version, retained Velopack updater state/actions and existing repository link. |

## Live appearance and persistence

There is no second theme preference. `settingsSession.Changed` updates retained brushes, control theme, selected navigation/tile states, preview and the existing backdrop. Dark and Light are opaque; Light uses Doky's warm neutral palette. Frosted/Acrylic use the shared desktop material with a restrained reading scrim.

**User-requested Clear exception:** when the dock is Clear, Settings uses the Frosted preset for readability. `SettingsShellAppearance.Resolve` returns a rendering choice only; it does not change the user's dock appearance or saved material. The preview still represents the selected dock appearance.

Mode tiles save through the existing transaction immediately. Number fields/toggles also save through that path. Slider gestures update the preview, then persist when released; keyboard edits save without polling. Initialization is guarded from saving. The Save changes action remains as an explicit retry/commit route. Failures display an error and reload saved values. Startup registration rollback remains intact.

The schema remains backward compatible. Two default-initialized fields were added at the user's request:

- `ClearRefractionStrength`: 0–20, default **12**, matching the approved stronger rim distortion.
- `SpecularHighlightAngle`: 0–360°, default **45°**, matching the existing top-left / bottom-right catches.

The Clear GPU shader and native specular rim share the same angle projection. The geometry stays attached to the existing hover-wave mask. All five dock appearances use the saved angle. The drag lens retains its independent material. Settings does not reset user files or modify dock-pins storage.

## Preview and accessibility

The preview is an in-app material surface with existing app artwork, not a second dock HWND. It reflects appearance, icon size/spacing, applicable blur/opacity, and highlight angle. Clear refraction is verified on the actual dock; this preview does not capture/refract the desktop. No fake unread numbers, playback state or running-app counts are added.

Controls retain keyboard focus visuals and automation names. Selected modes/navigation expose item status, status messages use polite live announcements, text wraps, and numeric formatting suppresses floating-point artifacts. Reset dialogs default to Cancel. Native resize/minimize/maximize/close remain available.

## Files changed for this task

- `src/GlassDock.App/Desktop/SettingsWindow.xaml`: complete retained nine-page shell and controls.
- `src/GlassDock.App/Desktop/SettingsWindow.xaml.cs`: navigation, live transactions, reset confirmations, optical values; earlier centering work preserved.
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.cs`: pass representative existing icons to Settings and apply the two requested optical settings; other pending dock work preserved.
- `src/GlassDock.Windows/Desktop/InteractiveGlassWindowHost.cs`: opt-in preservation of normal Settings chrome and DIP minimum size; existing utility defaults unchanged.
- `src/GlassDock.Core/Settings/GlassDockSettings.cs` and `GlassDockSettingsSession.cs`: safe persisted optical defaults, normalization and reset defaults.
- `src/GlassDock.Core/Materials/LiquidGlassMaterial.cs`: bounded lighting angle.
- `src/GlassDock.App/Rendering/ClearDockSpecular.cs`, `DesktopGlassBackdrop.cs`, `DokyLiquidGlassSurface.cs`, `Shaders/LiquidGlass.hlsl`, `Shaders/LiquidGlass.bin`: requested live optical settings, native/GPU projection parity.
- `tests/GlassDock.Windows.Tests/GlassDockSettingsStoreTests.cs`: old-profile compatibility and persistence test.
- `tests/GlassDock.Windows.Tests/AppActionPanelWiringTests.cs`: update a stale assertion for the previously authorized “Unstack all apps” label discovered by full-suite validation.

Added:

- `src/GlassDock.App/Desktop/SettingsWindow.Shell.cs`
- `src/GlassDock.Core/Settings/SettingsCatalog.cs`
- `src/GlassDock.Core/Settings/SettingsShellAppearance.cs`
- `src/GlassDock.Core/Materials/DockSpecularLighting.cs`
- `tests/GlassDock.Core.Tests/SettingsCatalogTests.cs`
- `tests/GlassDock.Core.Tests/DockSpecularLightingTests.cs`
- `tests/GlassDock.Windows.Tests/SettingsShellTests.cs`
- This report.

Temporary transformation scripts and before-edit copies are under ignored `artifacts/settings-redesign`; they are not runtime dependencies.

## Validation and limits

- Debug solution build: zero warnings/errors.
- Debug tests: **393 passed** (241 Core, 152 Windows).
- `scripts/Validate.ps1`: locked restore, Release build with zero warnings/errors, **393 Release tests passed**.
- HLSL compiled with `scripts/Build-LiquidShader.ps1`.
- Test coverage includes navigation/search destinations, one page scroll surface, display enum mapping, session notification behavior, all five Settings material choices, confirmed-reset wiring, settings compatibility/persistence, and native/shader highlight projection parity.
- Runtime: an earlier iteration's Settings window was opened and visually inspected. That exposed excessive transparency in Clear and absent sliders; both were corrected. The final build was launched and its path verified. Live screenshots confirmed readable Frosted Settings, visible working slider controls, integrated caption buttons, and clean General/Appearance layouts. The user is inspecting the UI. Full appearance switching, slider persistence, search/focus, resize/DPI, close/reopen/persistence, reset cancellation, and actual dock optical changes still require confirmation.
- Development executable: `C:\Dev\GlassDock\src\GlassDock.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\GlassDock.App.exe`.

Remaining manual regression checks: stacking/reorder/unstack, external file-drop, badges, previews, magnification, Pin Dock/auto-hide/AppBar, multi-monitor modes, bare Win/Win+Space/normal Windows shortcuts, utilities, Home, repeated Settings lifecycle and sustained resource behavior. Automated tests do not establish these desktop behaviors.

No full-window GPU refraction, new helper installer, stack-behavior preference, language selector or unsupported dock scale/corner-radius option was invented. The helper's live connection state is not currently exposed to Settings. General feature descriptions are intentionally informational where the backend provides no preference.

