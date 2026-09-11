# Architecture
## Scope and dependency decision
Phase 0 establishes boundaries only. No product services exist yet.
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
src/GlassDock.App holds the minimal WinUI startup window and future views/viewmodels.
src/GlassDock.Core holds future platform-independent logic and contracts.
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
