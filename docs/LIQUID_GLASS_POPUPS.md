# Shared Clear popup rendering

## Scope

App menus, Calendar, Quick Settings, Hidden Tray, stack contents and window-preview
containers opt into the existing DokyLiquidGlassSurface only in Clear. Home and
Settings are unchanged. Layouts, commands, input, animations, thumbnail capture
and window activation are unchanged.

PopupLiquidGlassSurface ties the retained renderer to the existing window's
visibility, bounds and appearance events. The shared native backdrop owns the
exact geometry; the same outline clips the GPU result. LiquidGlassDrawingTarget
uses a transparent CompositionDrawingSurface beneath the existing content. It
inherits the root's current transform/fade rather than maintaining a second
animation clock. Preview paging explicitly reattaches this single retained layer.
Native DWM thumbnails and cached preview images are not shader inputs.

Clear uses the dock's existing shader, dispersion, diffusion and rim highlights.
DockAppearanceSettings now carries the saved refraction strength and highlight
angle to popups, including live updates. A shared UtilityMaterial readability
profile adds Gaussian blur (8 DIPs) and exposure (-1.25 stops) before the optical
shader. The captured image stays visible; there is no opaque rectangle or separate
color scrim. The blur source is cropped to the popup plus a 64-DIP sampling halo.
Dark, Light, Frosted and Acrylic do not start this GPU path.

Follow-up: Clear popup highlights use intensity 0.26 (dock default 0.16), keeping
the same narrow rim and saved light direction. This improves visibility without
widening the stroke, adding glow, or lighting the whole perimeter. Native Clear
fallback uses the dock's thin diagonal specular catches too.

Only a successfully drawn GPU frame suppresses the native body. Capture/shader or
device errors release GPU resources and leave the existing native material usable.
Hiding, closing, or switching away from Clear stops capture and restores display
affinity. Returning to Clear permits retry. Monitor changes use the existing
renderer teardown/restart and monitor-local sampling. The GPU body can become
available after opening begins; until then native glass remains visible.

## Cost and limitations

No new timers or frame callbacks: existing capture arrivals and geometry events
coalesce draws. Shader, crop, blur, exposure and output resources are retained.
Each visible Clear popup owns a two-frame monitor capture pool, as the current
dock renderer does; sessions are not shared across windows. This adds roughly
16 MiB of frame buffers at 1080p or 63 MiB at 4K per visible popup, plus output
and driver allocations. Hidden retained popups own no capture pool. Blur adds GPU
cost despite its bounded crop. Multi-popup/high-refresh performance needs runtime
measurement; this is not a claim of zero overhead or leak-free soak behavior.

Windows' capture indicator remains enabled and Clear popups are excluded from
screen recordings to prevent feedback. Protected content, remote sessions,
unsupported adapters and HDR/color management retain existing limitations.
Blur softens the captured background (including optical fringe detail), never the
popup's text, controls or application thumbnail pixels. The native fallback has
blur and glass styling but no true refraction.

## Files changed for this task

- Rendering/PopupLiquidGlassSurface.cs (new lifecycle adapter)
- Rendering/LiquidGlassDrawingTarget.cs (new transparent GPU output)
- Rendering/DokyLiquidGlassSurface.cs (reuse optics with cropped readability pass)
- Rendering/DesktopGlassBackdrop.cs (publish popup geometry/appearance)
- Desktop/UtilityPopupStyle.cs (shared appearance notification)
- Desktop/CalendarPopoverWindow.cs
- Desktop/SystemQuickSettingsWindow.cs
- Desktop/SystemTrayWindow.cs
- Desktop/DockAppContextMenuWindow.cs
- Desktop/DockStackWindow.cs
- Desktop/WindowPreviewWindow.cs
- Desktop/DesktopOverlayWindow.cs (forward complete session appearance)
- Core/Settings/DockAppearanceSettings.cs
- Core/Settings/GlassDockSettingsSession.cs
- Core/Materials/UtilityMaterial.cs
- tests/GlassDock.Core.Tests/UtilityMaterialTests.cs
- tests/GlassDock.Windows.Tests/UtilityPopupAppearanceWiringTests.cs
- docs/LIQUID_GLASS_POPUPS.md

App-relative directories above are under src/GlassDock.App; Core is under
src/GlassDock.Core. Existing unrelated working-tree changes are preserved.

## Validation

Final Debug build: zero warnings/errors. Debug tests: 408/408 (255 Core, 153 Windows).
Tests cover optical settings propagation/normalization, shared renderer wiring,
Clear/visibility gating and retained preview-layer ownership. These do not prove
visual correctness. `scripts/Validate.ps1` passed locked restore, Release build
with zero warnings/errors and 408/408 Release tests. The initial runtime log
confirmed Calendar's GPU path reached an active frame. User requested stronger
popup highlights; the updated Debug build was launched afterward and its process
path verified:
`C:\Dev\GlassDock\src\GlassDock.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\GlassDock.App.exe`.
Final highlight appearance and the full popup/mode matrix remain pending user
verification. No performance or resource-soak certification is claimed.

Pending visual checks: open each popup over bright and dark text in Clear; verify
blur, rim distortion, legibility and sharp thumbnails. Change through all five
appearances while open; verify no stale GPU layer. Open/close repeatedly, page
previews, activate/close a disposable window and test outside click/Escape. Check
second-monitor/DPI positioning and memory/GPU behavior after repeated transitions.

## Hidden Tray activation follow-up

The user accepted the popup highlights, then reported GHelper not opening.
A focused trace confirmed PointerPressed and Button.Click with input enabled;
the new glass was not intercepting the click. The item came from registry fallback,
which returned directly to EXE launch without trying the live accessibility action.
Process.Start success was reported even though no application window appeared.

Registry-discovered entries now try the same native overflow action as other tray
entries. If EXE fallback is necessary, its working directory is the executable's
folder rather than Doky's directory. GHelper 0.285's [startup source](https://github.com/seerge/g-helper/blob/main/app/Program.cs)
uses the working directory to distinguish interactive display from background
startup. There is no app-name-specific activation branch. Existing visible-window
activation is retained. A bounded tray-activation.log records action requests and
results; the temporary PointerPressed trace was removed.

Changed: SystemTrayWindow.cs, TrayActivationWiringTests.cs and this note.
Debug/Release builds pass with zero warnings/errors; 410/410 tests pass in each
configuration, including repository validation. The corrected Debug build was
launched and its path verified (PID 24536). The user replied "works" to the
GHelper/other-tray-app check. GHelper's trace confirms the native accessibility
action was unavailable and the corrected EXE fallback ran; GHelper opening is
user-verified. The response did not identify a second app, so coverage of other
tray applications remains unspecified.
