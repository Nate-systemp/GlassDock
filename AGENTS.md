# Repository instructions
Current authorized milestone: Phase 2–3 floating desktop dock foundation, with bounded taskbar/recovery tests. Preserve the laboratory. Stop before the full launcher or other future product features.
- Taskbar suppression must remain an explicit watchdog-owned development lease with independent recovery. Do not claim production shell safety.
- Bare Windows-key interception is deferred; only registered development hotkeys are allowed in this milestone.
- Read PRODUCT_SPEC.md before implementing features, ARCHITECTURE.md before changing architecture, and ROADMAP.md before starting a milestone.
- Do not implement future phases early or add features merely because they seem useful.
- Keep UI, business logic, and Windows integration separate. Keep Windows-specific services and Win32 interop inside GlassDock.Windows whenever practical.
- Prefer small testable services, minimal dependencies, and no global mutable state.
- Never kill or modify Explorer.exe, modify Windows system files, permanently disable the taskbar, or replace the Windows shell.
- Do not add global hooks, shell changes, registry changes, networking, telemetry, licensing, payments, or auto-update in Phase 0.
- Future shell/taskbar behavior requires an explicit, tested emergency recovery path. Preserve Win+R, Win+E, Win+L and Ctrl+Alt+Delete.
- Never commit credentials, certificates, private signing keys, payment secrets, or personal configuration.
- Client-side licensing is never authoritative. Server secrets never belong in the client.
- Add interfaces only when a real use case establishes their contract; do not prebuild speculative services.
- Run scripts/Validate.ps1 for foundation changes. Document validation limits honestly.
- Each milestone must build, run, be tested, documented, and committed; stop before the next milestone.
