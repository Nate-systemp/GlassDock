# Architecture
## Scope and dependency decision
Phase 0 established the project boundaries. Phase 1 adds a standalone material laboratory.
The requested conceptual flow App → Core → Windows describes runtime delegation.
Compile-time references deliberately invert the last arrow to keep Core independent:
- App → Core, Windows
- Windows → Core
- Licensing → Core
- Watchdog → Core
- Core → no project and no platform packages
- Core.Tests → Core; Windows.Tests → Windows

Future Core interfaces will be implemented by Windows services and supplied by App's composition root. Core must never reference Windows, WinUI, or Licensing.

## Repository
src/GlassDock.App holds the laboratory, reusable GlassSurface and native rendering adapter.
src/GlassDock.Core holds platform-independent material values, presets and validation.
src/GlassDock.Windows holds future Windows service implementations, Win32/PInvoke, discovery, monitor and status integrations.
src/GlassDock.Watchdog is a separate executable that immediately exits successfully in Phase 0.
src/GlassDock.Licensing is an empty platform-independent library reserved for future licensing.
tests contains independent Core and Windows test projects.
installer and website are documentation placeholders, not implementations.
scripts contains repeatable validation; .github/workflows contains Windows CI; docs contains decisions and validation.

## UI and services
Future flow: views → viewmodels → services → Windows abstraction → OS APIs.
App owns UI lifecycle and dependency composition. Do not scatter native calls in UI code.
Introduce dependency injection when services exist, without adding a container now.
Future long-running operations use async APIs and cancellation tokens. Future structured logging is provided at composition boundaries, uses event identifiers, redacts sensitive data, and remains local unless explicitly authorized. No logging service, telemetry or polling runs now.
Avoid speculative empty interfaces such as IDockService until their contracts can be tested.

## Reliability architecture (future)
Watchdog may start App, monitor process termination, restore normal Windows behavior, and attempt one or two bounded restarts. Repeated failure must leave the standard Windows desktop usable.
Restoration must be idempotent and support emergency recovery, normal shutdown, partial startup and watchdog failure. IPC protocol and process ownership are future design decisions.
Phase 10 cannot enable taskbar hiding until recovery prerequisites are demonstrated. Phase 11 remains the dedicated watchdog milestone; ordering is not permission to ship unsafe Phase 10 behavior.

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
Windows tests currently enforce isolation only; real OS integration tests will require an explicitly controlled Windows environment later.
CI restores locked NuGet graphs, builds Release and runs both test projects. A local smoke check starts and closes only the foundation window. UI and shell behavior tests belong to their respective milestones.

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
