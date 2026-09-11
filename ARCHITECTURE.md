# Architecture
The later user-authorized taskbar auto-hide and bare Windows-key fixes are described in [TASKBAR_AND_WINDOWS_KEY_FIX.md](docs/TASKBAR_AND_WINDOWS_KEY_FIX.md). That follow-up supersedes the original development-only suppression and deferred bare-key behavior described below.
## Scope and dependency decision
Phase 0 established the project boundaries. Phase 1 adds a standalone material laboratory. The separately authorized Phase 2–3 foundation adds a floating dock and bounded recovery experiments.
The requested conceptual flow App → Core → Windows describes runtime delegation.
Compile-time references deliberately invert the last arrow to keep Core independent:
- App → Core, Windows
- Windows → Core
- Licensing → Core
- Watchdog → Core, Windows
- Core → no project and no platform packages
- Core.Tests → Core; Windows.Tests → Windows

Future Core interfaces will be implemented by Windows services and supplied by App's composition root. Core must never reference Windows, WinUI, or Licensing.

## Repository
src/GlassDock.App holds the laboratory, reusable GlassSurface, desktop overlay UI and native rendering adapters.
src/GlassDock.Core holds platform-independent material values, presets, dock transitions, placement math and taskbar/keyboard contracts.
src/GlassDock.Windows isolates own-window interop, primary-monitor placement, registered hotkeys, taskbar visibility and recovery coordination.
src/GlassDock.Watchdog is a separate recovery executable; App builds and deploys it under Recovery via a build-only project reference.
src/GlassDock.Licensing is an empty platform-independent library reserved for future licensing.
tests contains independent Core and Windows test projects.
installer and website are documentation placeholders, not implementations.
scripts contains repeatable validation; .github/workflows contains Windows CI; docs contains decisions and validation.

## UI and services
Future flow: views → viewmodels → services → Windows abstraction → OS APIs.
App owns UI lifecycle and dependency composition. Do not scatter native calls in UI code.
Introduce dependency injection when services exist, without adding a container now.
Async UI operations use cancellation/revision checks. Development exceptions are written locally to development-error.log; no telemetry or application networking exists. Only an explicit taskbar test starts heartbeat and visibility checks.
Avoid speculative empty interfaces such as IDockService until their contracts can be tested.

## Development recovery architecture
App starts a child recovery process over redirected standard input/output. READY precedes HIDE; the child checks the parent's process name, session and startup time, then acquires a named session mutex. Only the child hides the taskbar. A manual-reset event provides independent emergency signaling.
The lease ends after parent exit, EOF, RESTORE, five seconds without a heartbeat, the 60-second maximum, or a taskbar identity change. App also restores if its helper fails. No process restarts are performed.
The independent --restore command works without App. No registry, appbar setting, startup or Explorer lifetime changes exist. Production suppression remains deferred; simultaneous process-pair failure requires the external recovery command. See docs/DESKTOP_RECOVERY_DESIGN.md.

## Licensing architecture (future)
Client → license API → database. Payment provider integration and authoritative state live on the server. Future offline grace and caching must account for tampering and failure. No network, activation, payments or Free/Pro logic is implemented.

## Toolchain and deployment
.NET 10, pinned SDK 10.0.401; Windows App SDK 2.4.0; Windows SDK target 10.0.26100.0.
The development executable is unpackaged x64, uses app-local Windows App SDK files, and relies on installed .NET 10. This avoids installing a Windows App Runtime as part of foundation testing.
Windows 10 build 19041 is the app's technical API floor, not a commercial support commitment. Test supported Windows 11 releases first. ARM64, multi-monitor/DPI behavior and release OS support need later validation.
Visual Studio with WinUI development tools is the intended full IDE; VS Code can edit sources. CLI MSBuild validation uses the installed .NET SDK.
Packaging, signing, distribution, installer and updater decisions are deferred to Phase 15. Never store signing keys in Git.

## Testing
Core tests can run without Windows desktop UI. Foundation tests inspect project dependencies and enforce platform boundaries rather than pretending to test unimplemented features.
Windows tests enforce isolation; OS visibility/recovery checks are explicit local integration experiments, excluded from automatic tests and CI.
CI restores locked NuGet graphs, builds Release and runs both test projects. Core tests cover stale animation completions, lifecycle and physical placement/DPI math. Local UI and recovery observations are recorded in docs/PHASE_2_3_VALIDATION.md.

## Phase 1 material laboratory
GlassMaterial is an immutable Core value record with finite-value normalization and bounded dimensions.
GlassMaterialPresets supplies Frosted, Clear and experimental Refractive configurations.
GlassLabViewModel owns session-only values; GlassLabView supplies labeled controls and five vector test scenes.
GlassSurface composes a XAML material rectangle, edge lighting, rim, content and a native DropShadow.
GlassCompositionBrush owns one native effect graph and its resources while connected. It uses Microsoft.UI.Composition and Win2D effect definitions, not a Canvas rendering loop.
Pipeline: in-app CreateBackdropBrush → GaussianBlurEffect → SaturationEffect → ExposureEffect → tint composite → weighted raw/processed blend.
Opacity is the processed-material blend weight; border, lighting, shadow and content remain independent. Brightness maps to exposure with log2.
Preset changes animate effect properties for 180 ms when Windows animations are enabled. Sliders update immediately; no polling or timers.
The shadow uses an independent rounded geometry mask. Unload disposes native resources. Unsupported effect creation produces an explicitly reported solid fallback.
The renderer lives in App because it owns XAML/Composition visual resources. OS window/shell services remain reserved for Windows; Core references neither UI nor Windows APIs.

RefractionLayer isolates the optical-stage capability. Native mode reports refraction unsupported, disables its slider and forces effective displacement to zero.
The Refractive preset adds edge illumination and depth cues only. Win2D DisplacementMapEffect is marked NoComposition.
True spatial refraction would require a separate texture/shader experiment and a defined source-sampling strategy; no custom engine is justified yet.
This phase samples pixels inside the application only. Desktop-behind-window transparency, production accessibility contrast, HDR, cross-GPU performance and device-loss recovery are not established by this laboratory.
See docs/PHASE_1_FINDINGS.md for observations and primary references.

## Phase 2–3 desktop overlay
DesktopOverlayWindow owns a fixed 640×144 DIP topmost, borderless, non-activating HWND. WindowsOverlayManager positions it using primary MonitorInfo.Monitor bounds, not work area or laboratory dimensions; DIP sizes use GetDpiForWindow. The visible pill is 120×5 DIP, centered with a default 24 DIP bottom gap. An idle native window region supplies a 200×44 DIP hit target; expanded interaction uses the whole bounded overlay. Pixels outside that region do not intercept input.

One GlassSurface changes from pill to 560×84 DIP dock in 200 ms. Seven UI-only placeholders fade in, and item hover scales to 1.08 over 110 ms. A 280 ms exit delay and revision-based state machine prevent stale animation completions. Reduced-motion mode removes the timed transitions. No idle pointer polling exists.

GlassEffectGraph is shared by both rendering adapters. The lab uses Microsoft.UI.Composition.CreateBackdropBrush. DesktopGlassBackdrop uses Windows.UI.Composition.CreateBackdropBrush as a custom SystemBackdrop, where the backing pixels are the desktop. An OS compositor visual-surface mask clips the same blur/saturation/exposure/tint graph to the animated rounded surface. GlassSurface still owns the rim, lighting, shadow and foreground content. This is native composition, with no desktop screenshot loop. The host-backdrop variant returned black on this test machine and is not used.

The OS compositor needs its own current-thread dispatcher queue. Own-window DWM alpha setup and background erasure live in WindowsOverlayManager; no other application windows are styled. Composition resources are released on disconnect, and the dispatcher queue is retained for App's lifetime.

IKeyboardService currently exposes only RegisterHotKey-based Ctrl+Alt+Space and Ctrl+Alt+F12 events. Bare Windows interception remains deferred; no low-level hook or keyboard filtering is implemented. The Home event opens a small development placeholder, not a launcher.
